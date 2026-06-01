using System.Globalization;
using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ALRrx.Infrastructure.Import;

public class GoogleSheetsImportService : IGoogleSheetsImportService
{
    private const string CacheKey = "GoogleSheets_Sales_All";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GoogleSheetsImportService> _logger;
    private readonly string _spreadsheetId;
    private readonly string _gid;

    public GoogleSheetsImportService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IConfiguration configuration,
        ILogger<GoogleSheetsImportService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
        _spreadsheetId = configuration["GoogleSheets:SpreadsheetId"] ?? "";
        _gid = configuration["GoogleSheets:Gid"] ?? "0";
    }

    public async Task<List<SaleRecord>> GetSalesAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKey, out List<SaleRecord>? cached) && cached != null)
        {
            return cached;
        }

        try
        {
            var url = $"https://docs.google.com/spreadsheets/d/{_spreadsheetId}/export?format=csv&gid={_gid}";
            var client = _httpClientFactory.CreateClient("GoogleSheets");
            var response = await client.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google Sheets devolvió código {StatusCode}", response.StatusCode);
                return new List<SaleRecord>();
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,
                BadDataFound = null,
                HeaderValidated = null,
                TrimOptions = TrimOptions.Trim,
            };

            using var reader = new StreamReader(stream);
            using var csv = new CsvReader(reader, config);

            await csv.ReadAsync();
            csv.ReadHeader();

            var records = new List<SaleRecord>();
            while (await csv.ReadAsync())
            {
                try
                {
                    var record = new SaleRecord
                    {
                        Timestamp = ParseDate(csv.GetField("Timestamp")),
                        SellerName = csv.GetField("Your name:")?.Trim() ?? "",
                        SaleDate = ParseDate(csv.GetField("Date of sale:")),
                        CustomerEmail = csv.GetField("Customer's email:")?.Trim() ?? "",
                        Package = csv.GetField("Package Sold")?.Trim() ?? "",
                        Amount = ParseAmount(csv.GetField("Sale Amount: (ex. $498)")),
                    };

                    if (!string.IsNullOrWhiteSpace(record.SellerName) && record.Amount > 0)
                    {
                        records.Add(record);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Fila omitida por error de parseo");
                }
            }

            _cache.Set(CacheKey, records, CacheDuration);
            _logger.LogInformation("Google Sheets: {Count} ventas cargadas", records.Count);
            return records;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener datos de Google Sheets");
            return new List<SaleRecord>();
        }
    }

    private static DateTime ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DateTime.MinValue;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return dt;
        return DateTime.MinValue;
    }

    private static decimal ParseAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0m;
        var cleaned = value.Replace("$", "").Replace(",", "").Trim();
        if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
            return amount;
        return 0m;
    }
}
