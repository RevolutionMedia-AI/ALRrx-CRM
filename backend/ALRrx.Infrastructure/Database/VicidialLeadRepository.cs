using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace ALRrx.Infrastructure.Database;

public sealed class VicidialLeadRepository : IVicidialLeadRepository
{
    private readonly IDatabaseConnection _dbConnection;
    private readonly ILogger<VicidialLeadRepository> _logger;

    public VicidialLeadRepository(IDatabaseConnection dbConnection, ILogger<VicidialLeadRepository> logger)
    {
        _dbConnection = dbConnection;
        _logger = logger;
    }

    public async Task<VicidialLeadDto?> GetByIdAsync(int leadId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT lead_id, first_name, last_name, phone_number, email
            FROM vicidial_list
            WHERE lead_id = @LeadId
            LIMIT 1
            """;

        await using var connection = (MySqlConnection)await _dbConnection.GetConnectionAsync(ct);
        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.Add("@LeadId", MySqlDbType.Int32).Value = leadId;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            _logger.LogInformation("vicidial_list lookup: lead_id={LeadId} returned no rows", leadId);
            return null;
        }

        return MapLead(reader);
    }

    public async Task<Dictionary<int, VicidialLeadDto>> GetByIdsAsync(IEnumerable<int> leadIds, CancellationToken ct = default)
    {
        var ids = leadIds.Where(id => id > 0).Distinct().ToList();
        var result = new Dictionary<int, VicidialLeadDto>();
        if (ids.Count == 0) return result;

        var parameters = ids.Select((id, idx) => new MySqlParameter($"@L{idx}", MySqlDbType.Int32) { Value = id }).ToArray();
        var placeholders = string.Join(", ", parameters.Select(p => p.ParameterName));
        var sql = $"""
            SELECT lead_id, first_name, last_name, phone_number, email
            FROM vicidial_list
            WHERE lead_id IN ({placeholders})
            """;

        await using var connection = (MySqlConnection)await _dbConnection.GetConnectionAsync(ct);
        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddRange(parameters);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var lead = MapLead(reader);
            result[lead.LeadId] = lead;
        }
        _logger.LogInformation("vicidial_list batch lookup: requested={Requested}, found={Found}", ids.Count, result.Count);
        return result;
    }

    private static VicidialLeadDto MapLead(MySqlDataReader reader)
    {
        return new VicidialLeadDto
        {
            LeadId = reader.GetInt32(reader.GetOrdinal("lead_id")),
            FirstName = SafeTrim(reader, "first_name"),
            LastName = SafeTrim(reader, "last_name"),
            PhoneNumber = SafeTrim(reader, "phone_number"),
            Email = SafeTrim(reader, "email"),
        };
    }

    private static string SafeTrim(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal)) return string.Empty;
        return reader.GetString(ordinal).Trim();
    }
}
