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

    private const string SalesColumns =
        "Id, LeadId, SalesRep, SaleDate, ClientPhone, ClientName, ClientEmail, Bundle, Amount, CreatedAt";

    public async Task EnsureTableAsync(CancellationToken ct = default)
    {
        const string createTable = """
            CREATE TABLE IF NOT EXISTS vicidial_form_sales (
                Id INT AUTO_INCREMENT PRIMARY KEY,
                LeadId INT NULL,
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
                INDEX idx_date (SaleDate),
                INDEX idx_lead_id (LeadId)
            )
            """;

        await using var connection = await GetOpenConnectionAsync(ct);
        await using (var createCmd = new MySqlCommand(createTable, connection))
        {
            await createCmd.ExecuteNonQueryAsync(ct);
        }
        _logger.LogInformation("vicidial_form_sales table ready");

        await EnsureLeadIdColumnAsync(connection, ct);
        await EnsureLeadIdIndexAsync(connection, ct);
    }

    private async Task EnsureLeadIdColumnAsync(MySqlConnection connection, CancellationToken ct)
    {
        const string checkSql = """
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'vicidial_form_sales'
              AND COLUMN_NAME = 'LeadId'
            """;

        await using var checkCmd = new MySqlCommand(checkSql, connection);
        var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct)) > 0;

        if (exists)
        {
            _logger.LogInformation("vicidial_form_sales.LeadId column already present");
            return;
        }

        const string alterSql = """
            ALTER TABLE vicidial_form_sales
                ADD COLUMN LeadId INT NULL AFTER Id
            """;
        await using var alterCmd = new MySqlCommand(alterSql, connection);
        await alterCmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("vicidial_form_sales.LeadId column added");
    }

    private async Task EnsureLeadIdIndexAsync(MySqlConnection connection, CancellationToken ct)
    {
        const string checkSql = """
            SELECT COUNT(*) FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'vicidial_form_sales'
              AND INDEX_NAME = 'idx_lead_id'
            """;

        await using var checkCmd = new MySqlCommand(checkSql, connection);
        var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct)) > 0;

        if (exists)
        {
            _logger.LogInformation("vicidial_form_sales idx_lead_id index already present");
            return;
        }

        const string alterSql = """
            ALTER TABLE vicidial_form_sales
                ADD INDEX idx_lead_id (LeadId)
            """;
        await using var alterCmd = new MySqlCommand(alterSql, connection);
        await alterCmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("vicidial_form_sales idx_lead_id index added");
    }

    public async Task<int> InsertAsync(VicidialSaleRequest request, string bundleDisplayName, CancellationToken ct = default)
    {
        var source = request.LeadId.HasValue ? "VicidialForm" : "ManualForm";

        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand("""
            INSERT INTO vicidial_form_sales
                (LeadId, SalesRep, SaleDate, ClientPhone, ClientName, ClientEmail, Bundle, Amount, Source)
            VALUES
                (@LeadId, @SalesRep, @SaleDate, @ClientPhone, @ClientName, @ClientEmail, @Bundle, @Amount, @Source)
            """, connection);

        cmd.Parameters.Add("@LeadId", MySqlDbType.Int32).Value = (object?)request.LeadId ?? DBNull.Value;
        cmd.Parameters.AddWithValue("@SalesRep", request.SalesRep.Trim());
        cmd.Parameters.AddWithValue("@SaleDate", request.SaleDate);
        cmd.Parameters.AddWithValue("@ClientPhone", request.ClientPhone.Trim());
        cmd.Parameters.AddWithValue("@ClientName", request.ClientName.Trim());
        cmd.Parameters.AddWithValue("@ClientEmail", request.ClientEmail.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@Bundle", bundleDisplayName);
        cmd.Parameters.AddWithValue("@Amount", request.Amount);
        cmd.Parameters.Add("@Source", MySqlDbType.VarChar).Value = source;

        await cmd.ExecuteNonQueryAsync(ct);
        var newId = cmd.LastInsertedId;
        _logger.LogInformation("Vicidial sale recorded: LeadId={LeadId} | {SalesRep} | {Bundle} | ${Amount} | Id={Id} | Source={Source}",
            request.LeadId, request.SalesRep, bundleDisplayName, request.Amount, newId, source);
        return Convert.ToInt32(newId);
    }

    public async Task<List<VicidialSaleDto>> GetBySalesRepAsync(string salesRep, string? from, string? to, int limit, CancellationToken ct = default)
    {
        var clampedLimit = Math.Clamp(limit, 1, 1000);
        var sql = $"SELECT {SalesColumns} FROM vicidial_form_sales WHERE SalesRep = @SalesRep";
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
            results.Add(MapSale(reader));
        }
        return results;
    }

    public async Task<List<VicidialSaleDto>> GetByLeadIdAsync(int leadId, int limit, CancellationToken ct = default)
    {
        if (leadId <= 0) return new List<VicidialSaleDto>();
        var clampedLimit = Math.Clamp(limit, 1, 1000);
        const string sql = """
            SELECT Id, LeadId, SalesRep, SaleDate, ClientPhone, ClientName, ClientEmail, Bundle, Amount, CreatedAt
            FROM vicidial_form_sales
            WHERE LeadId = @LeadId
            ORDER BY SaleDate DESC
            LIMIT 500
            """;
        var finalSql = sql.Replace("LIMIT 500", $"LIMIT {clampedLimit}");

        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(finalSql, connection);
        cmd.Parameters.Add("@LeadId", MySqlDbType.Int32).Value = leadId;

        var results = new List<VicidialSaleDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapSale(reader));
        }
        return results;
    }

    public async Task<List<VicidialSaleDto>> GetAllAsync(string? from, string? to, int limit, CancellationToken ct = default)
    {
        var clampedLimit = Math.Clamp(limit, 1, 1000);
        var sql = $"SELECT {SalesColumns} FROM vicidial_form_sales WHERE 1=1";
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
                results.Add(MapSale(reader));
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

        var listSql = $"SELECT {SalesColumns} FROM vicidial_form_sales {where} ORDER BY SaleDate DESC LIMIT {clampedLimit}";

        await using var connection = await GetOpenConnectionAsync(ct);
        await using var listCmd = new MySqlCommand(listSql, connection);
        if (!string.IsNullOrWhiteSpace(from)) listCmd.Parameters.Add("@From", MySqlDbType.VarChar).Value = from;
        if (!string.IsNullOrWhiteSpace(to)) listCmd.Parameters.Add("@To", MySqlDbType.VarChar).Value = to;

        var results = new List<VicidialSaleDto>();
        await using (var reader = await listCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                results.Add(MapSale(reader));
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

        if (request.LeadId.HasValue)
        {
            sets.Add("LeadId = @LeadId");
            parameters.Add(new MySqlParameter("@LeadId", MySqlDbType.Int32) { Value = request.LeadId.Value });
        }
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

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM vicidial_form_sales WHERE Id = @Id";
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.Add("@Id", MySqlDbType.Int32).Value = id;
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Vicidial sale #{Id} deleted, rows affected: {Rows}", id, affected);
        return affected > 0;
    }

    private static VicidialSaleDto MapSale(MySqlDataReader reader)
    {
        var leadIdOrdinal = reader.GetOrdinal("LeadId");
        int? leadId = reader.IsDBNull(leadIdOrdinal) ? null : reader.GetInt32(leadIdOrdinal);

        return new VicidialSaleDto
        {
            Id = reader.GetInt32("Id"),
            LeadId = leadId,
            SalesRep = reader.GetString("SalesRep"),
            SaleDate = reader.GetDateTime("SaleDate"),
            ClientPhone = reader.GetString("ClientPhone"),
            ClientName = reader.GetString("ClientName"),
            ClientEmail = reader.GetString("ClientEmail"),
            Bundle = reader.GetString("Bundle"),
            Amount = reader.GetDecimal("Amount"),
            CreatedAt = reader.GetDateTime("CreatedAt"),
        };
    }
}
