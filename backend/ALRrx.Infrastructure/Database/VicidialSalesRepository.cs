using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace ALRrx.Infrastructure.Database;

public sealed class VicidialSalesRepository : IVicidialSalesRepository
{
    private readonly IDatabaseConnection _dbConnection;
    private readonly ILogger<VicidialSalesRepository> _logger;

    public VicidialSalesRepository(IDatabaseConnection dbConnection, ILogger<VicidialSalesRepository> logger)
    {
        _dbConnection = dbConnection;
        _logger = logger;
    }

    private async Task<MySqlConnection> GetOpenConnectionAsync(CancellationToken ct)
    {
        return (MySqlConnection)await _dbConnection.GetConnectionAsync(ct);
    }

    public async Task EnsureTableAsync(CancellationToken ct = default)
    {
        const string createTable = """
            CREATE TABLE IF NOT EXISTS vicidial_form_sales (
                Id INT AUTO_INCREMENT PRIMARY KEY,
                SalesRep VARCHAR(100) NOT NULL,
                SaleDate DATETIME NOT NULL,
                ClientPhone VARCHAR(50) NOT NULL,
                ClientName VARCHAR(255) NOT NULL,
                ClientEmail VARCHAR(255) NOT NULL,
                Bundle VARCHAR(30) NOT NULL,
                Amount DECIMAL(10,2) NOT NULL,
                Source VARCHAR(20) NOT NULL DEFAULT 'VicidialForm',
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                INDEX idx_rep_date (SalesRep, SaleDate),
                INDEX idx_date (SaleDate)
            )
            """;

        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(createTable, connection);
        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("vicidial_form_sales table ready");
    }

    public async Task<int> InsertAsync(VicidialSaleRequest request, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand("""
            INSERT INTO vicidial_form_sales
                (SalesRep, SaleDate, ClientPhone, ClientName, ClientEmail, Bundle, Amount, Source)
            VALUES
                (@SalesRep, @SaleDate, @ClientPhone, @ClientName, @ClientEmail, @Bundle, @Amount, 'VicidialForm')
            """, connection);

        cmd.Parameters.AddWithValue("@SalesRep", request.SalesRep.Trim());
        cmd.Parameters.AddWithValue("@SaleDate", request.SaleDate);
        cmd.Parameters.AddWithValue("@ClientPhone", request.ClientPhone.Trim());
        cmd.Parameters.AddWithValue("@ClientName", request.ClientName.Trim());
        cmd.Parameters.AddWithValue("@ClientEmail", request.ClientEmail.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@Bundle", request.Bundle.ToDisplayName());
        cmd.Parameters.AddWithValue("@Amount", request.Amount);

        var newId = await cmd.ExecuteScalarAsync(ct);
        _logger.LogInformation("Vicidial sale recorded: {SalesRep} | {Bundle} | ${Amount}", request.SalesRep, request.Bundle, request.Amount);
        return Convert.ToInt32(newId);
    }

    public async Task<List<VicidialSaleDto>> GetBySalesRepAsync(string salesRep, DateTime? from, DateTime? to, int limit, CancellationToken ct = default)
    {
        var sql = """
            SELECT Id, SalesRep, SaleDate, ClientPhone, ClientName, ClientEmail, Bundle, Amount, CreatedAt
            FROM vicidial_form_sales
            WHERE SalesRep = @SalesRep
            """;

        if (from.HasValue) sql += " AND SaleDate >= @From";
        if (to.HasValue) sql += " AND SaleDate < @To";
        sql += " ORDER BY SaleDate DESC LIMIT @Limit";

        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@SalesRep", salesRep);
        cmd.Parameters.AddWithValue("@Limit", limit);
        if (from.HasValue) cmd.Parameters.AddWithValue("@From", from.Value);
        if (to.HasValue) cmd.Parameters.AddWithValue("@To", to.Value);

        var results = new List<VicidialSaleDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new VicidialSaleDto
            {
                Id = reader.GetInt32("Id"),
                SalesRep = reader.GetString("SalesRep"),
                SaleDate = reader.GetDateTime("SaleDate"),
                ClientPhone = reader.GetString("ClientPhone"),
                ClientName = reader.GetString("ClientName"),
                ClientEmail = reader.GetString("ClientEmail"),
                Bundle = reader.GetString("Bundle"),
                Amount = reader.GetDecimal("Amount"),
                CreatedAt = reader.GetDateTime("CreatedAt"),
            });
        }
        return results;
    }

    public async Task<List<VicidialSaleDto>> GetAllAsync(DateTime? from, DateTime? to, int limit, CancellationToken ct = default)
    {
        var sql = """
            SELECT Id, SalesRep, SaleDate, ClientPhone, ClientName, ClientEmail, Bundle, Amount, CreatedAt
            FROM vicidial_form_sales
            WHERE 1=1
            """;

        if (from.HasValue) sql += " AND SaleDate >= @From";
        if (to.HasValue) sql += " AND SaleDate < @To";
        sql += " ORDER BY SaleDate DESC LIMIT @Limit";

        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Limit", limit);
        if (from.HasValue) cmd.Parameters.AddWithValue("@From", from.Value);
        if (to.HasValue) cmd.Parameters.AddWithValue("@To", to.Value);

        var results = new List<VicidialSaleDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new VicidialSaleDto
            {
                Id = reader.GetInt32("Id"),
                SalesRep = reader.GetString("SalesRep"),
                SaleDate = reader.GetDateTime("SaleDate"),
                ClientPhone = reader.GetString("ClientPhone"),
                ClientName = reader.GetString("ClientName"),
                ClientEmail = reader.GetString("ClientEmail"),
                Bundle = reader.GetString("Bundle"),
                Amount = reader.GetDecimal("Amount"),
                CreatedAt = reader.GetDateTime("CreatedAt"),
            });
        }
        return results;
    }
}
