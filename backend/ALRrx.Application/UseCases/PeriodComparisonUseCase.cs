using ALRrx.Application.DTOs;
using ALRrx.Application.Helpers;
using ALRrx.Domain.Interfaces;

namespace ALRrx.Application.UseCases;

public sealed class PeriodComparisonUseCase
{
    private readonly IQueryService _queryService;

    public PeriodComparisonUseCase(IQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<PeriodComparisonResponseDto> ExecuteAsync(TimeFilterDto filter1, TimeFilterDto filter2, CancellationToken ct = default)
    {
        var range1 = TimeFilterHelper.BuildTimeRange(filter1);
        var range2 = TimeFilterHelper.BuildTimeRange(filter2);

        var period1Tasks = await Task.WhenAll(
            _queryService.ExecuteQueryAsync("ventas_hoy", range1, ct),
            _queryService.ExecuteQueryAsync("contact_vs_nocontact", range1, ct),
            _queryService.ExecuteQueryAsync("dispositions", range1, ct),
            _queryService.ExecuteQueryAsync("agent_performance", range1, ct),
            _queryService.ExecuteQueryAsync("leads_contact_rate", range1, ct)
        );

        var period2Tasks = await Task.WhenAll(
            _queryService.ExecuteQueryAsync("ventas_hoy", range2, ct),
            _queryService.ExecuteQueryAsync("contact_vs_nocontact", range2, ct),
            _queryService.ExecuteQueryAsync("dispositions", range2, ct),
            _queryService.ExecuteQueryAsync("agent_performance", range2, ct),
            _queryService.ExecuteQueryAsync("leads_contact_rate", range2, ct)
        );

        var period1Kpis = BuildKpis(period1Tasks);
        var period2Kpis = BuildKpis(period2Tasks);
        var kpiChanges = CalculateKpiChanges(period1Kpis, period2Kpis);

        var agents1 = BuildAgentRows(period1Tasks[3].Rows);
        var agents2 = BuildAgentRows(period2Tasks[3].Rows);
        var agentComparisons = BuildAgentComparisons(agents1, agents2);

        var period1Dispositions = BuildDispositionRows(period1Tasks[2].Rows);
        var period2Dispositions = BuildDispositionRows(period2Tasks[2].Rows);

        var contactComparison = BuildContactComparison(period1Tasks[1].Rows, period2Tasks[1].Rows);

        return new PeriodComparisonResponseDto
        {
            Period1Label = FormatPeriodLabel(filter1),
            Period2Label = FormatPeriodLabel(filter2),
            Period1Kpis = period1Kpis,
            Period2Kpis = period2Kpis,
            KpiChanges = kpiChanges,
            Agents = agentComparisons,
            Period1Dispositions = period1Dispositions,
            Period2Dispositions = period2Dispositions,
            ContactComparison = contactComparison
        };
    }

    private static List<KpiRow> BuildKpis(Task<QueryResult>[] tasks)
    {
        var kpis = new List<KpiRow>();

        if (tasks[0].Rows.Length > 0)
        {
            var r = tasks[0].Rows[0];
            kpis.Add(new KpiRow { Label = "Sales Today", Value = r.GetValueOrDefault("Sales_Today")?.ToString() ?? "0", Color = "#10B981" });
        }

        if (tasks[1].Rows.Length > 0)
        {
            var r = tasks[1].Rows[0];
            kpis.Add(new KpiRow { Label = "Contacts", Value = r.GetValueOrDefault("Contact")?.ToString() ?? "0", Color = "#3B82F6" });
            kpis.Add(new KpiRow { Label = "No Contacts", Value = r.GetValueOrDefault("No_Contact")?.ToString() ?? "0", Color = "#E11D48" });
            kpis.Add(new KpiRow { Label = "Total Calls", Value = r.GetValueOrDefault("Total_Calls")?.ToString() ?? "0", Color = "#F59E0B" });
        }

        if (tasks[4].Rows.Length > 0)
        {
            var r = tasks[4].Rows[0];
            kpis.Add(new KpiRow { Label = "Leads Dialed", Value = r.GetValueOrDefault("Total_Dialed_Leads")?.ToString() ?? "0", Color = "#3B82F6" });
            kpis.Add(new KpiRow { Label = "Leads Contacted", Value = r.GetValueOrDefault("Contacted_Leads")?.ToString() ?? "0", Color = "#10B981" });
            kpis.Add(new KpiRow { Label = "Contact Rate", Value = $"{r.GetValueOrDefault("Contact_Rate") ?? 0}%", Color = "#8B5CF6" });
        }

        return kpis;
    }

    private static List<KpiRow> CalculateKpiChanges(List<KpiRow> period1, List<KpiRow> period2)
    {
        var changes = new List<KpiRow>();
        foreach (var p1 in period1)
        {
            var p2 = period2.FirstOrDefault(k => k.Label == p1.Label);
            if (p2 == null) continue;

            var v1 = ParseDouble(p1.Value);
            var v2 = ParseDouble(p2.Value);
            var change = v1 != 0 ? ((v2 - v1) / v1 * 100) : 0;
            var direction = change >= 0 ? "+" : "";

            changes.Add(new KpiRow
            {
                Label = p1.Label,
                Value = $"{direction}{change:F1}%",
                Color = change >= 0 ? "#10B981" : "#E11D48"
            });
        }
        return changes;
    }

    private static List<AgentComparisonRow> BuildAgentComparisons(List<AgentRow> agents1, List<AgentRow> agents2)
    {
        var result = new List<AgentComparisonRow>();
        var agentsByUser = agents2.ToDictionary(a => a.User, a => a);

        foreach (var a1 in agents1)
        {
            var v1Calls = ParseInt(a1.CallsHandled);
            var v2Calls = agentsByUser.TryGetValue(a1.User, out var a2) ? ParseInt(a2.CallsHandled) : 0;
            var v1Sales = ParseInt(a1.SalesMade);
            var v2Sales = agentsByUser.TryGetValue(a1.User, out var a2sales) ? ParseInt(a2sales.SalesMade) : 0;

            result.Add(new AgentComparisonRow
            {
                Name = a1.Name,
                User = a1.User,
                Period1Calls = v1Calls,
                Period2Calls = v2Calls,
                CallsChange = v2Calls - v1Calls,
                CallsChangePct = v1Calls != 0 ? (double)(v2Calls - v1Calls) / v1Calls * 100 : 0,
                Period1Sales = v1Sales,
                Period2Sales = v2Sales,
                SalesChange = v2Sales - v1Sales,
                SalesChangePct = v1Sales != 0 ? (double)(v2Sales - v1Sales) / v1Sales * 100 : 0
            });
        }

        return result;
    }

    private static List<AgentRow> BuildAgentRows(QueryResultRow[] rows)
    {
        return rows.Select(r => new AgentRow
        {
            Name = r.GetValueOrDefault("Name")?.ToString() ?? r.GetValueOrDefault("User")?.ToString() ?? "",
            User = r.GetValueOrDefault("User")?.ToString() ?? "",
            CallsHandled = r.GetValueOrDefault("Calls_Handled")?.ToString() ?? "0",
            SalesMade = r.GetValueOrDefault("Sales_Made")?.ToString() ?? "0",
        }).ToList();
    }

    private static List<DispositionRow> BuildDispositionRows(QueryResultRow[] rows)
    {
        return rows.Select(r => new DispositionRow
        {
            Status = r.GetValueOrDefault("Disposition")?.ToString() ?? "",
            Total = r.GetValueOrDefault("Total")?.ToString() ?? "0",
            Percentage = r.GetValueOrDefault("Percentage")?.ToString() ?? "0",
        }).ToList();
    }

    private static ContactComparison? BuildContactComparison(QueryResultRow[] rows1, QueryResultRow[] rows2)
    {
        if (rows1.Length == 0 || rows2.Length == 0) return null;

        var r1 = rows1[0];
        var r2 = rows2[0];
        var c1 = ParseInt(r1.GetValueOrDefault("Contact")?.ToString() ?? "0");
        var c2 = ParseInt(r2.GetValueOrDefault("Contact")?.ToString() ?? "0");
        var n1 = ParseInt(r1.GetValueOrDefault("No_Contact")?.ToString() ?? "0");
        var n2 = ParseInt(r2.GetValueOrDefault("No_Contact")?.ToString() ?? "0");
        var t1 = c1 + n1;
        var t2 = c2 + n2;

        return new ContactComparison
        {
            Period1Contacts = c1,
            Period2Contacts = c2,
            Period1NoContacts = n1,
            Period2NoContacts = n2,
            Period1Rate = t1 > 0 ? $"{((decimal)c1 / t1 * 100):F1}%" : "0%",
            Period2Rate = t2 > 0 ? $"{((decimal)c2 / t2 * 100):F1}%" : "0%"
        };
    }

    private static string FormatPeriodLabel(TimeFilterDto filter)
    {
        if (filter.Period == "Custom" && filter.CustomStart.HasValue && filter.CustomEnd.HasValue)
            return $"{filter.CustomStart.Value:yyyy-MM-dd} to {filter.CustomEnd.Value:yyyy-MM-dd}";
        return filter.Period;
    }

    private static double ParseDouble(string s)
    {
        s = s.Replace("%", "").Trim();
        return double.TryParse(s, out var d) ? d : 0;
    }

    private static int ParseInt(string s)
    {
        return int.TryParse(s, out var i) ? i : 0;
    }
}