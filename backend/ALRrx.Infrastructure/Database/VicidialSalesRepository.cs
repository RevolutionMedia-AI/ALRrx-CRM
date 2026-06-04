using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace ALRrx.Infrastructure.Database;

public sealed class VicidialSalesRepository : IVicidialSalesRepository
{
    private readonly FormDbConnectionFactory _dbConnection;
    private readonly ILogger<VicidialSalesRepository> _logger;

    public VicidialSalesRepository(FormDbConnectionFactory dbConnection, ILogger<VicidialSalesRepository> logger)
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

    public async Task<int> InsertAsync(VicidialSaleRequest request, string bundleDisplayName, CancellationToken ct = default)
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
        cmd.Parameters.AddWithValue("@Bundle", bundleDisplayName);
        cmd.Parameters.AddWithValue("@Amount", request.Amount);

        await cmd.ExecuteNonQueryAsync(ct);
        var newId = cmd.LastInsertedId;
        _logger.LogInformation("Vicidial sale recorded: {SalesRep} | {Bundle} | ${Amount} | Id={Id}", request.SalesRep, bundleDisplayName, request.Amount, newId);
        return Convert.ToInt32(newId);
    }

    public async Task<List<VicidialSaleDto>> GetBySalesRepAsync(string salesRep, string? from, string? to, int limit, CancellationToken ct = default)
    {
        var clampedLimit = Math.Clamp(limit, 1, 1000);
        var sql = "SELECT Id, SalesRep, SaleDate, ClientPhone, ClientName, ClientEmail, Bundle, Amount, CreatedAt FROM vicidial_form_sales WHERE SalesRep = @SalesRep";
        if (!string.IsNullOrWhiteSpace(from)) sql += " AND SaleDate >= STR_TO_DATE(@From, '%Y-%m-%d %H:%i:%s')";
        if (!string.IsNullOrWhiteSpace(to)) sql += " AND SaleDate < STR_TO_DATE(@To, '%Y-%m-%d %H:%i:%s')";
        sql += $" ORDER BY SaleDate DESC LIMIT {clampedLimit}";

        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.Add("@SalesRep", MySqlDbType.VarChar).Value = salesRep;
        if (!string.IsNullOrWhiteSpace(from)) cmd.Parameters.Add("@From", MySqlDbType.VarChar).Value = from;
        if (!string.IsNullOrWhiteSpace(to)) cmd.Parameters.Add("@To", MySqlDbType.VarChar).Value = to;

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

    public async Task<List<VicidialSaleDto>> GetAllAsync(string? from, string? to, int limit, CancellationToken ct = default)
    {
        var clampedLimit = Math.Clamp(limit, 1, 1000);
        var sql = "SELECT Id, SalesRep, SaleDate, ClientPhone, ClientName, ClientEmail, Bundle, Amount, CreatedAt FROM vicidial_form_sales WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(from)) sql += " AND SaleDate >= STR_TO_DATE(@From, '%Y-%m-%d %H:%i:%s')";
        if (!string.IsNullOrWhiteSpace(to)) sql += " AND SaleDate < STR_TO_DATE(@To, '%Y-%m-%d %H:%i:%s')";
        sql += $" ORDER BY SaleDate DESC LIMIT {clampedLimit}";

        _logger.LogInformation("Vicidial GetAllAsync SQL: {Sql} (from={From}, to={To})", sql, from, to);

        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(sql, connection);
        if (!string.IsNullOrWhiteSpace(from)) cmd.Parameters.Add("@From", MySqlDbType.VarChar).Value = from;
        if (!string.IsNullOrWhiteSpace(to)) cmd.Parameters.Add("@To", MySqlDbType.VarChar).Value = to;

        try
        {
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
            _logger.LogInformation("Vicidial GetAllAsync returned {Count} rows", results.Count);
            return results;
        }
        catch (MySqlException ex)
        {
            _logger.LogError(ex, "Vicidial GetAllAsync SQL error. Code={Code}, Number={Number}, Message={Message}", ex.ErrorCode, ex.Number, ex.Message);
            throw;
        }
    }

    public async Task<SalesSummaryDto> GetSummaryAsync(string? from, string? to, int limit, CancellationToken ct = default)
    {
        var clampedLimit = Math.Clamp(limit, 1, 1000);
        var where = "WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(from)) where += " AND SaleDate >= STR_TO_DATE(@From, '%Y-%m-%d %H:%i:%s')";
        if (!string.IsNullOrWhiteSpace(to)) where += " AND SaleDate < STR_TO_DATE(@To, '%Y-%m-%d %H:%i:%s')";

        var listSql = $"SELECT Id, SalesRep, SaleDate, ClientPhone, ClientName, ClientEmail, Bundle, Amount, CreatedAt FROM vicidial_form_sales {where} ORDER BY SaleDate DESC LIMIT {clampedLimit}";

        await using var connection = await GetOpenConnectionAsync(ct);
        await using var listCmd = new MySqlCommand(listSql, connection);
        if (!string.IsNullOrWhiteSpace(from)) listCmd.Parameters.Add("@From", MySqlDbType.VarChar).Value = from;
        if (!string.IsNullOrWhiteSpace(to)) listCmd.Parameters.Add("@To", MySqlDbType.VarChar).Value = to;

        var results = new List<VicidialSaleDto>();
        await using (var reader = await listCmd.ExecuteReaderAsync(ct))
        {
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
        }

        var totalSales = results.Sum(r => r.Amount);
        var sellers = results.Select(r => r.SalesRep).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
        var packages = results.Select(r => r.Bundle).Where(b => !string.IsNullOrWhiteSpace(b)).Distinct().OrderBy(b => b).ToList();
        var first = results.FirstOrDefault();

        return new SalesSummaryDto
        {
            TotalSales = totalSales,
            TotalCount = results.Count,
            AllSales = results.Select(r => new SaleRecord
            {
                Timestamp = r.CreatedAt,
                SellerName = r.SalesRep,
                SaleDate = r.SaleDate,
                CustomerEmail = r.ClientEmail,
                Package = r.Bundle,
                Amount = r.Amount,
            }).ToList(),
            LastSale = first == null ? null : new SaleRecord
            {
                Timestamp = first.CreatedAt,
                SellerName = first.SalesRep,
                SaleDate = first.SaleDate,
                CustomerEmail = first.ClientEmail,
                Package = first.Bundle,
                Amount = first.Amount,
            },
            AvailableSellers = sellers,
            AvailablePackages = packages,
        };
    }

    public async Task<bool> UpdateAsync(int id, VicidialSaleUpdateRequest request, string bundleDisplayName, CancellationToken ct = default)
    {
        var sets = new List<string>();
        var parameters = new List<MySqlParameter>
        {
            new("@Id", MySqlDbType.Int32) { Value = id },
        };

        if (request.SaleDate.HasValue)
        {
            sets.Add("SaleDate = @SaleDate");
            parameters.Add(new MySqlParameter("@SaleDate", MySqlDbType.DateTime) { Value = request.SaleDate.Value });
        }
        if (request.ClientPhone != null)
        {
            sets.Add("ClientPhone = @ClientPhone");
            parameters.Add(new MySqlParameter("@ClientPhone", MySqlDbType.VarChar) { Value = request.ClientPhone.Trim() });
        }
        if (request.ClientName != null)
        {
            sets.Add("ClientName = @ClientName");
            parameters.Add(new MySqlParameter("@ClientName", MySqlDbType.VarChar) { Value = request.ClientName.Trim() });
        }
        if (request.ClientEmail != null)
        {
            sets.Add("ClientEmail = @ClientEmail");
            parameters.Add(new MySqlParameter("@ClientEmail", MySqlDbType.VarChar) { Value = request.ClientEmail.Trim().ToLowerInvariant() });
        }
        if (request.Bundle != null)
        {
            sets.Add("Bundle = @Bundle");
            parameters.Add(new MySqlParameter("@Bundle", MySqlDbType.VarChar) { Value = bundleDisplayName });
        }
        if (request.Amount.HasValue)
        {
            sets.Add("Amount = @Amount");
            parameters.Add(new MySqlParameter("@Amount", MySqlDbType.Decimal) { Value = request.Amount.Value });
        }

        if (sets.Count == 0) return false;

        var sql = $"UPDATE vicidial_form_sales SET {string.Join(", ", sets)} WHERE Id = @Id";
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddRange(parameters.ToArray());
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Vicidial sale #{Id} updated by {Email}: fields={Fields}", id, request.EditorEmail, sets.Count);
        return affected > 0;
    }
}
