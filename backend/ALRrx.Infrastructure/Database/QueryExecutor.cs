using ALRrx.Application.Interfaces;
using ALRrx.Domain.Entities;
using ALRrx.Domain.Interfaces;
using ALRrx.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace ALRrx.Infrastructure.Database;

public sealed class QueryExecutor : IQueryService
{
    private readonly IDatabaseConnection _dbConnection;
    private readonly ILogger<QueryExecutor> _logger;
    private readonly Dictionary<string, QueryDefinition> _queries = new()
    {
        ["ventas_hoy"] = new QueryDefinition
        {
            Id = "ventas_hoy",
            Name = "Sales Today",
            Description = "Total sales count for the period",
            Category = "Dashboard",
            SqlTemplate = """
                SELECT COUNT(*) AS Sales_Today
                FROM (
                    SELECT vl.status, vl.call_date
                    FROM vicidial_log vl
                    JOIN vicidial_users vu ON vl.user = vu.user
                    WHERE DATE(vl.call_date) BETWEEN @Start AND @End
                    AND vl.status = 'SALE'
                    AND vu.user_group = 'ALTRX'

                    UNION ALL

                    SELECT cl.status, cl.call_date
                    FROM vicidial_closer_log cl
                    JOIN vicidial_users vu ON cl.user = vu.user
                    WHERE DATE(cl.call_date) BETWEEN @Start AND @End
                    AND cl.status = 'SALE'
                    AND vu.user_group = 'ALTRX'
                ) calls
                """
        },
        ["agent_performance"] = new QueryDefinition
        {
            Id = "agent_performance",
            Name = "Agent Performance",
            Description = "Calls Handled, Contacts, Sales, Conversion %, AHT per agent",
            Category = "Agents",
            SqlTemplate = """
                SELECT
                    vl.user AS `User`,
                    vu.full_name AS Name,
                    COUNT(*) AS Calls_Handled,
                    SUM(CASE WHEN vl.status = 'SALE' THEN 1 ELSE 0 END) AS Sales_Made,
                    SUM(CASE
                        WHEN vl.status IN ('SALE','NSALE','NSLBO','NSLIC','NSLMC','NSLNI','NSLPO','NSLWC','CALLBK','ITST','NTQLFY')
                        THEN 1 ELSE 0 END) AS Contacts,
                    ROUND(
                        SUM(CASE WHEN vl.status = 'SALE' THEN 1 ELSE 0 END) * 100.0 /
                        NULLIF(SUM(CASE
                            WHEN vl.status IN ('SALE','NSALE','NSLBO','NSLIC','NSLMC','NSLNI','NSLPO','NSLWC','CALLBK','ITST','NTQLFY')
                            THEN 1 ELSE 0 END), 0)
                    , 2) AS Conversion_Percentage,
                    SEC_TO_TIME(AVG(vl.length_in_sec)) AS AHT
                FROM vicidial_log vl
                JOIN vicidial_users vu ON vl.user = vu.user
                WHERE DATE(vl.call_date) BETWEEN @Start AND @End
                AND vu.user_group = 'ALTRX'
                GROUP BY vl.user, vu.full_name
                ORDER BY Sales_Made DESC
                """
        },
        ["staffing"] = new QueryDefinition
        {
            Id = "staffing",
            Name = "Staffing",
            Description = "Active ALTRX agents with live status",
            Category = "Agents",
            SqlTemplate = """
                SELECT
                    ug.group_name AS Supervisor,
                    vu.user AS Emp_Number,
                    vu.full_name AS Name,
                    vu.user AS User,
                    COALESCE(vla.status, 'OFFLINE') AS Status,
                    vla.last_call_time,
                    vla.last_update_time
                FROM vicidial_users vu
                LEFT JOIN vicidial_user_groups ug ON vu.user_group = ug.user_group
                LEFT JOIN vicidial_live_agents vla ON vu.user = vla.user
                WHERE vu.user_group = 'ALTRX'
                AND vu.active = 'Y'
                ORDER BY ug.group_name, vu.full_name
                """
        },
        ["aht_daily"] = new QueryDefinition
        {
            Id = "aht_daily",
            Name = "AHT (Daily)",
            Description = "Average handle time across all agents today",
            Category = "Dashboard",
            SqlTemplate = """
                SELECT
                    ROUND(AVG(talk_sec + dispo_sec + dead_sec) / 60, 1) AS AHT_Minutes
                FROM vicidial_agent_log
                WHERE event_time >= @Start AND event_time < @End
                """
        },
        ["occupancy_rate"] = new QueryDefinition
        {
            Id = "occupancy_rate",
            Name = "Occupancy Rate",
            Description = "Occupancy percentage across all agents today",
            Category = "Dashboard",
            SqlTemplate = """
                SELECT
                    ROUND(
                        (SUM(talk_sec + dispo_sec + dead_sec) /
                         NULLIF(SUM(pause_sec + wait_sec + talk_sec + dispo_sec + dead_sec), 0)) * 100
                    , 1) AS Occupancy_Pct
                FROM vicidial_agent_log
                WHERE event_time >= @Start AND event_time < @End
                """
        },
        ["all_calls"] = new QueryDefinition
        {
            Id = "all_calls",
            Name = "All Calls",
            Description = "All calls from ALTRX group in the period",
            Category = "Calls",
            SqlTemplate = """
                SELECT
                    vl.call_date,
                    vl.user,
                    vu.full_name AS Name,
                    vl.status,
                    vl.lead_id,
                    vl.length_in_sec
                FROM vicidial_log vl
                JOIN vicidial_users vu ON vl.user = vu.user
                WHERE DATE(vl.call_date) BETWEEN @Start AND @End
                AND vu.user_group = 'ALTRX'
                ORDER BY vl.call_date DESC
                """
        },
        ["dispositions"] = new QueryDefinition
        {
            Id = "dispositions",
            Name = "Dispositions",
            Description = "Call status breakdown with totals and percentages",
            Category = "Dashboard",
            SqlTemplate = """
                SELECT
                    status AS Disposition,
                    COUNT(*) AS Total,
                    ROUND(COUNT(*) * 100.0 / SUM(COUNT(*)) OVER(), 2) AS Percentage
                FROM (
                    SELECT vl.status, vl.call_date
                    FROM vicidial_log vl
                    JOIN vicidial_users vu ON vl.user = vu.user
                    WHERE DATE(vl.call_date) BETWEEN @Start AND @End
                    AND vu.user_group = 'ALTRX'

                    UNION ALL

                    SELECT cl.status, cl.call_date
                    FROM vicidial_closer_log cl
                    JOIN vicidial_users vu ON cl.user = vu.user
                    WHERE DATE(cl.call_date) BETWEEN @Start AND @End
                    AND vu.user_group = 'ALTRX'
                ) calls
                GROUP BY status
                ORDER BY Total DESC
                """
        },
        ["contact_vs_nocontact"] = new QueryDefinition
        {
            Id = "contact_vs_nocontact",
            Name = "Contact vs No Contact",
            Description = "Contact and no-contact summary",
            Category = "Dashboard",
            SqlTemplate = """
                SELECT
                    SUM(CASE
                        WHEN status IN ('SALE','NSALE','NSLBO','NSLIC','NSLMC','NSLNI','NSLPO','NSLWC','CALLBK','ITST','NTQLFY')
                        THEN 1 ELSE 0 END) AS Contact,
                    SUM(CASE
                        WHEN status NOT IN ('SALE','NSALE','NSLBO','NSLIC','NSLMC','NSLNI','NSLPO','NSLWC','CALLBK','ITST','NTQLFY')
                        THEN 1 ELSE 0 END) AS No_Contact,
                    COUNT(*) AS Total_Calls
                FROM (
                    SELECT vl.status, vl.call_date
                    FROM vicidial_log vl
                    JOIN vicidial_users vu ON vl.user = vu.user
                    WHERE DATE(vl.call_date) BETWEEN @Start AND @End
                    AND vu.user_group = 'ALTRX'

                    UNION ALL

                    SELECT cl.status, cl.call_date
                    FROM vicidial_closer_log cl
                    JOIN vicidial_users vu ON cl.user = vu.user
                    WHERE DATE(cl.call_date) BETWEEN @Start AND @End
                    AND vu.user_group = 'ALTRX'
                ) calls
                """
        },
        ["leads_contact_rate"] = new QueryDefinition
        {
            Id = "leads_contact_rate",
            Name = "Leads Contact Rate",
            Description = "Leads dialed, contacted, and contact rate",
            Category = "Dashboard",
            SqlTemplate = """
                SELECT
                    COUNT(DISTINCT lead_id) AS Total_Dialed_Leads,
                    COUNT(DISTINCT CASE
                        WHEN status IN ('SALE','NSALE','NSLBO','NSLIC','NSLMC','NSLNI','NSLPO','NSLWC','CALLBK','ITST','NTQLFY','HNGUP')
                        THEN lead_id
                    END) AS Contacted_Leads,
                    ROUND(
                        COUNT(DISTINCT CASE
                            WHEN status IN ('SALE','NSALE','NSLBO','NSLIC','NSLMC','NSLNI','NSLPO','NSLWC','CALLBK','ITST','NTQLFY','HNGUP')
                            THEN lead_id
                        END) * 100.0 / COUNT(DISTINCT lead_id)
                    , 1) AS Contact_Rate
                FROM (
                    SELECT lead_id, status FROM vicidial_log
                    WHERE DATE(call_date) BETWEEN @Start AND @End

                    UNION ALL

                    SELECT lead_id, status FROM vicidial_closer_log
                    WHERE DATE(call_date) BETWEEN @Start AND @End
                ) calls
                """
        }
    };

    public QueryExecutor(IDatabaseConnection dbConnection, ILogger<QueryExecutor> logger)
    {
        _dbConnection = dbConnection;
        _logger = logger;
    }

    public IReadOnlyCollection<QueryDefinition> GetAvailableQueries()
        => _queries.Values.ToList().AsReadOnly();

    public async Task<ReportResult> ExecuteQueryAsync(string queryId, TimeRange timeRange, CancellationToken ct = default)
    {
        if (!_queries.TryGetValue(queryId, out var query))
            throw new KeyNotFoundException($"Query '{queryId}' not found");

        _logger.LogInformation("Executing query: {QueryName} | Start: {Start:yyyy-MM-dd HH:mm:ss} | End: {End:yyyy-MM-dd HH:mm:ss}", query.Name, timeRange.Start, timeRange.End);

        await using var connection = (MySqlConnection)await _dbConnection.GetConnectionAsync(ct);
        await using var cmd = new MySqlCommand(query.SqlTemplate, connection);

        if (query.SqlTemplate.Contains("@Start"))
            cmd.Parameters.AddWithValue("@Start", timeRange.Start);
        if (query.SqlTemplate.Contains("@End"))
            cmd.Parameters.AddWithValue("@End", timeRange.End);

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var columns = new List<string>();
        for (var i = 0; i < reader.FieldCount; i++)
            columns.Add(reader.GetName(i));

        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
                row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return new ReportResult
        {
            ReportName = query.Name,
            Columns = [.. columns],
            Rows = [.. rows],
            GeneratedAt = DateTime.UtcNow,
            TimeRange = new TimeRangeExecuted
            {
                Start = timeRange.Start,
                End = timeRange.End
            }
        };
    }
}
