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
            Description = "Total sales count for the period (includes all SALE variants + archive tables)",
            Category = "Dashboard",
            SqlTemplate = """
                SELECT COUNT(*) AS Sales_Today
                FROM (
                    SELECT vl.lead_id
                    FROM vicidial_log vl
                    INNER JOIN vicidial_users vu ON vl.user = vu.user
                    WHERE vu.user_group = 'ALTRX'
                    AND (UPPER(vl.status) LIKE 'SALE%' OR UPPER(vl.status) = 'UPSELL' OR UPPER(vl.status) = 'XFER-SALE')
                    AND DATE(vl.call_date) BETWEEN @Start AND @End

                    UNION ALL

                    SELECT cl.lead_id
                    FROM vicidial_closer_log cl
                    INNER JOIN vicidial_users vu ON cl.user = vu.user
                    WHERE vu.user_group = 'ALTRX'
                    AND (UPPER(cl.status) LIKE 'SALE%' OR UPPER(cl.status) = 'UPSELL' OR UPPER(cl.status) = 'XFER-SALE')
                    AND DATE(cl.call_date) BETWEEN @Start AND @End

                    UNION ALL

                    SELECT vla.lead_id
                    FROM vicidial_log_archive vla
                    INNER JOIN vicidial_users vu ON vla.user = vu.user
                    WHERE vu.user_group = 'ALTRX'
                    AND (UPPER(vla.status) LIKE 'SALE%' OR UPPER(vla.status) = 'UPSELL' OR UPPER(vla.status) = 'XFER-SALE')
                    AND DATE(vla.call_date) BETWEEN @Start AND @End

                    UNION ALL

                    SELECT cla.lead_id
                    FROM vicidial_closer_log_archive cla
                    INNER JOIN vicidial_users vu ON cla.user = vu.user
                    WHERE vu.user_group = 'ALTRX'
                    AND (UPPER(cla.status) LIKE 'SALE%' OR UPPER(cla.status) = 'UPSELL' OR UPPER(cla.status) = 'XFER-SALE')
                    AND DATE(cla.call_date) BETWEEN @Start AND @End
                ) AS todas_las_ventas
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
                    user AS `User`,
                    full_name AS Name,
                    SUM(Calls_Handled) AS Calls_Handled,
                    SUM(Sales_Made) AS Sales_Made,
                    SUM(Contacts) AS Contacts,
                    ROUND(SUM(Sales_Made) * 100.0 / NULLIF(SUM(Contacts), 0), 2) AS Conversion_Percentage,
                    SEC_TO_TIME(AVG(AHT)) AS AHT
                FROM (
                    SELECT
                        vl.user,
                        vu.full_name,
                        COUNT(*) AS Calls_Handled,
                        SUM(CASE
                            WHEN UPPER(vl.status) LIKE 'SALE%' OR UPPER(vl.status) = 'UPSELL' OR UPPER(vl.status) = 'XFER-SALE'
                            THEN 1 ELSE 0
                        END) AS Sales_Made,
                        SUM(CASE
                            WHEN vl.status IN ('SALE','NSALE','NSLBO','NSLIC','NSLMC','NSLNI','NSLPO','NSLWC','CALLBK','ITST','NTQLFY')
                            THEN 1 ELSE 0 END) AS Contacts,
                        AVG(vl.length_in_sec) AS AHT
                    FROM vicidial_log vl
                    JOIN vicidial_users vu ON vl.user = vu.user
                    WHERE DATE(vl.call_date) BETWEEN @Start AND @End
                    AND vu.user_group = 'ALTRX'
                    GROUP BY vl.user, vu.full_name

                    UNION ALL

                    SELECT
                        cl.user,
                        vu.full_name,
                        COUNT(*) AS Calls_Handled,
                        SUM(CASE
                            WHEN UPPER(cl.status) LIKE 'SALE%' OR UPPER(cl.status) = 'UPSELL' OR UPPER(cl.status) = 'XFER-SALE'
                            THEN 1 ELSE 0
                        END) AS Sales_Made,
                        SUM(CASE
                            WHEN cl.status IN ('SALE','NSALE','NSLBO','NSLIC','NSLMC','NSLNI','NSLPO','NSLWC','CALLBK','ITST','NTQLFY')
                            THEN 1 ELSE 0 END) AS Contacts,
                        AVG(cl.length_in_sec) AS AHT
                    FROM vicidial_closer_log cl
                    JOIN vicidial_users vu ON cl.user = vu.user
                    WHERE DATE(cl.call_date) BETWEEN @Start AND @End
                    AND vu.user_group = 'ALTRX'
                    GROUP BY cl.user, vu.full_name
                ) combined
                GROUP BY user, full_name
                ORDER BY Sales_Made DESC
                """
        },
        ["staffing"] = new QueryDefinition
        {
            Id = "staffing",
            Name = "Staffing",
            Description = "Active ALTRX agents with live status, current lead, campaign and pause reason",
            Category = "Agents",
            SqlTemplate = """
                SELECT
                    ug.group_name AS Supervisor,
                    vu.user AS Emp_Number,
                    vu.full_name AS Name,
                    vu.user AS User,
                    COALESCE(vla.status, 'OFFLINE') AS Status,
                    vla.lead_id AS Current_Lead_Id,
                    vla.campaign_id AS Current_Campaign_Id,
                    vc.campaign_name AS Current_Campaign_Name,
                    vc.dial_method AS Dial_Method,
                    vla.pause_code AS Pause_Code,
                    vla.last_call_time,
                    vla.last_update_time
                FROM vicidial_users vu
                LEFT JOIN vicidial_user_groups ug ON vu.user_group = ug.user_group
                LEFT JOIN vicidial_live_agents vla ON vu.user = vla.user
                LEFT JOIN vicidial_campaigns vc ON vla.campaign_id = vc.campaign_id
                WHERE vu.user_group = 'ALTRX'
                AND vu.active = 'Y'
                ORDER BY ug.group_name, vu.full_name
                """
        },

        ["agent_leaderboard"] = new QueryDefinition
        {
            Id = "agent_leaderboard",
            Name = "Agent Leaderboard (Today)",
            Description = "Top agents by total sales (VICIdial dispositions + form-registered)",
            Category = "Agents",
            SqlTemplate = """
                SELECT
                    user AS User,
                    full_name AS Name,
                    SUM(Sales_Made) AS ViciSales,
                    SUM(Contacts) AS Contacts,
                    ROUND(SUM(Sales_Made) * 100.0 / NULLIF(SUM(Contacts), 0), 2) AS Conversion_Percentage
                FROM (
                    SELECT
                        vl.user,
                        vu.full_name,
                        SUM(CASE WHEN UPPER(vl.status) LIKE 'SALE%' OR UPPER(vl.status) = 'UPSELL' OR UPPER(vl.status) = 'XFER-SALE' THEN 1 ELSE 0 END) AS Sales_Made,
                        SUM(CASE WHEN vl.status IN ('SALE','NSALE','NSLBO','NSLIC','NSLMC','NSLNI','NSLPO','NSLWC','CALLBK','ITST','NTQLFY') THEN 1 ELSE 0 END) AS Contacts
                    FROM vicidial_log vl
                    JOIN vicidial_users vu ON vl.user = vu.user
                    WHERE DATE(vl.call_date) BETWEEN @Start AND @End
                    AND vu.user_group = 'ALTRX'
                    GROUP BY vl.user, vu.full_name

                    UNION ALL

                    SELECT
                        cl.user,
                        vu.full_name,
                        SUM(CASE WHEN UPPER(cl.status) LIKE 'SALE%' OR UPPER(cl.status) = 'UPSELL' OR UPPER(cl.status) = 'XFER-SALE' THEN 1 ELSE 0 END) AS Sales_Made,
                        SUM(CASE WHEN cl.status IN ('SALE','NSALE','NSLBO','NSLIC','NSLMC','NSLNI','NSLPO','NSLWC','CALLBK','ITST','NTQLFY') THEN 1 ELSE 0 END) AS Contacts
                    FROM vicidial_closer_log cl
                    JOIN vicidial_users vu ON cl.user = vu.user
                    WHERE DATE(cl.call_date) BETWEEN @Start AND @End
                    AND vu.user_group = 'ALTRX'
                    GROUP BY cl.user, vu.full_name
                ) combined
                GROUP BY user, full_name
                ORDER BY ViciSales DESC
                LIMIT 5
                """
        },

        ["queue_metrics"] = new QueryDefinition
        {
            Id = "queue_metrics",
            Name = "Queue Metrics (Today)",
            Description = "Service level, abandon rate, queue depth and longest wait for inbound",
            Category = "Calls",
            SqlTemplate = """
                SELECT
                    (SELECT COUNT(*) FROM vicidial_live_agents WHERE status = 'QUEUE') AS Queue_Depth,
                    (SELECT COUNT(*) FROM vicidial_closer_log WHERE call_date >= @Start AND call_date < @End) AS Total_Inbound_Calls,
                    (SELECT COUNT(*) FROM vicidial_closer_log WHERE call_date >= @Start AND call_date < @End AND length_in_sec <= 20) AS Calls_Under_20s,
                    (SELECT COUNT(*) FROM vicidial_closer_log WHERE call_date >= @Start AND call_date < @End AND (status LIKE 'ABANDON%' OR status = 'DROP' OR status = 'NA')) AS Abandoned,
                    (SELECT COUNT(*) FROM vicidial_closer_log WHERE call_date >= @Start AND call_date < @End AND (status = 'QUEUE')) AS Calls_Queued
                FROM DUAL
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
        },
        ["vicidial_call_type_sales"] = new QueryDefinition
        {
            Id = "vicidial_call_type_sales",
            Name = "VICIdial OUTBOUND and INBOUND Sales",
            Description = "Per-agent breakdown of SALE dispositions by call direction (OUTBOUND from vicidial_log, INBOUND from vicidial_closer_log). Excludes TEST DUMMY.",
            Category = "Calls",
            SqlTemplate = """
                SELECT
                    agent_id AS Agent_Id,
                    agent_name AS Agent_Name,
                    SUM(CASE WHEN call_type = 'OUTBOUND' THEN 1 ELSE 0 END) AS Outbound_Sales,
                    SUM(CASE WHEN call_type = 'INBOUND' THEN 1 ELSE 0 END) AS Inbound_Sales,
                    ROUND(SUM(CASE WHEN call_type = 'OUTBOUND' THEN 1 ELSE 0 END) * 100.0 / NULLIF(COUNT(*), 0), 2) AS Outbound_Pct,
                    ROUND(SUM(CASE WHEN call_type = 'INBOUND' THEN 1 ELSE 0 END) * 100.0 / NULLIF(COUNT(*), 0), 2) AS Inbound_Pct
                FROM (
                    SELECT
                        'OUTBOUND' AS call_type,
                        vl.user AS agent_id,
                        vu.full_name AS agent_name
                    FROM vicidial_log vl
                    LEFT JOIN vicidial_users vu
                        ON vl.user = vu.user
                    WHERE vl.status = 'SALE'
                      AND vu.full_name <> 'TEST DUMMY'
                      AND DATE(vl.call_date) BETWEEN @Start AND @End

                    UNION ALL

                    SELECT
                        'INBOUND' AS call_type,
                        vcl.user AS agent_id,
                        vu.full_name AS agent_name
                    FROM vicidial_closer_log vcl
                    LEFT JOIN vicidial_users vu
                        ON vcl.user = vu.user
                    WHERE vcl.status = 'SALE'
                      AND vu.full_name <> 'TEST DUMMY'
                      AND DATE(vcl.call_date) BETWEEN @Start AND @End
                ) AS combined
                GROUP BY agent_id, agent_name
                ORDER BY (Outbound_Sales + Inbound_Sales) DESC, agent_name ASC
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
        if (queryId.StartsWith("agent_history:", StringComparison.Ordinal))
        {
            return await ExecuteAgentHistoryAsync(queryId["agent_history:".Length..], timeRange, ct);
        }

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

    private async Task<ReportResult> ExecuteAgentHistoryAsync(string user, TimeRange timeRange, CancellationToken ct)
    {
        const string sql = """
            SELECT
                vl.call_date,
                vl.length_in_sec AS Length_Sec,
                vl.status,
                vl.lead_id,
                COALESCE(vl.phone_number, '') AS Phone,
                vl.comments
            FROM vicidial_log vl
            WHERE vl.user = @User
              AND vl.call_date >= @Start AND vl.call_date < @End

            UNION ALL

            SELECT
                cl.call_date,
                cl.length_in_sec AS Length_Sec,
                cl.status,
                cl.lead_id,
                COALESCE(cl.phone_number, '') AS Phone,
                cl.comments
            FROM vicidial_closer_log cl
            WHERE cl.user = @User
              AND cl.call_date >= @Start AND cl.call_date < @End

            ORDER BY call_date DESC
            LIMIT 200
            """;

        _logger.LogInformation("Executing agent history: {User} | {Start:yyyy-MM-dd HH:mm:ss} -> {End:yyyy-MM-dd HH:mm:ss}", user, timeRange.Start, timeRange.End);

        await using var connection = (MySqlConnection)await _dbConnection.GetConnectionAsync(ct);
        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@User", user);
        cmd.Parameters.AddWithValue("@Start", timeRange.Start);
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
            ReportName = $"Call History — {user}",
            Columns = [.. columns],
            Rows = [.. rows],
            GeneratedAt = DateTime.UtcNow,
            TimeRange = new TimeRangeExecuted { Start = timeRange.Start, End = timeRange.End }
        };
    }
}
