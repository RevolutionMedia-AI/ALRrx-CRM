using ALRrx.Application.DTOs;
using ALRrx.Domain.Interfaces;
using ALRrx.Domain.ValueObjects;

namespace ALRrx.Application.UseCases;

public interface IDashboardPdfService
{
    byte[] GenerateDashboardPdf(DashboardPdfData data);
}

public interface IDashboardExcelService
{
    string Format { get; }
    string ContentType { get; }
    byte[] GenerateDashboardExcel(DashboardPdfData data);
}

public sealed class ExportDashboardUseCase
{
    private readonly IQueryService _queryService;
    private readonly IDashboardPdfService _pdfService;

    public ExportDashboardUseCase(IQueryService queryService, IDashboardPdfService pdfService)
    {
        _queryService = queryService;
        _pdfService = pdfService;
    }

    public async Task<byte[]> ExecuteAsync(TimeFilterDto filter, CancellationToken ct = default)
    {
        var data = await BuildDataAsync(filter, ct);
        return _pdfService.GenerateDashboardPdf(data);
    }

    public async Task<DashboardPdfData> BuildDataAsync(TimeFilterDto filter, CancellationToken ct = default)
    {
        var timeRange = BuildTimeRange(filter);

        var tasks = await Task.WhenAll(
            _queryService.ExecuteQueryAsync("ventas_hoy", timeRange, ct),
            _queryService.ExecuteQueryAsync("contact_vs_nocontact", timeRange, ct),
            _queryService.ExecuteQueryAsync("dispositions", timeRange, ct),
            _queryService.ExecuteQueryAsync("agent_performance", timeRange, ct),
            _queryService.ExecuteQueryAsync("all_calls", timeRange, ct),
            _queryService.ExecuteQueryAsync("aht_daily", timeRange, ct),
            _queryService.ExecuteQueryAsync("occupancy_rate", timeRange, ct),
            _queryService.ExecuteQueryAsync("leads_contact_rate", timeRange, ct)
        );

        var salesResult = tasks[0];
        var contactResult = tasks[1];
        var dispositionsResult = tasks[2];
        var agentResult = tasks[3];
        var callsResult = tasks[4];

        var kpis = new List<KpiRow>();

        var leadsResult = tasks[7];
        if (leadsResult.Rows.Length > 0)
        {
            var lr = leadsResult.Rows[0];
            kpis.Add(new KpiRow { Label = "Leads Dialed", Value = lr.GetValueOrDefault("Total_Dialed_Leads")?.ToString() ?? "0", Color = "#3B82F6" });
            kpis.Add(new KpiRow { Label = "Leads Contacted", Value = lr.GetValueOrDefault("Contacted_Leads")?.ToString() ?? "0", Color = "#10B981" });
            kpis.Add(new KpiRow { Label = "Contact Rate", Value = $"{lr.GetValueOrDefault("Contact_Rate") ?? 0}%", Color = "#8B5CF6" });
        }

        if (salesResult.Rows.Length > 0)
            kpis.Add(new KpiRow { Label = "Sales Today", Value = salesResult.Rows[0].GetValueOrDefault("Sales_Today")?.ToString() ?? "0", Color = "#10B981" });

        if (contactResult.Rows.Length > 0)
        {
            var cr = contactResult.Rows[0];
            kpis.Add(new KpiRow { Label = "Contacts", Value = cr.GetValueOrDefault("Contact")?.ToString() ?? "0", Color = "#3B82F6" });
            kpis.Add(new KpiRow { Label = "No Contacts", Value = cr.GetValueOrDefault("No_Contact")?.ToString() ?? "0", Color = "#E11D48" });
            kpis.Add(new KpiRow { Label = "Total Calls", Value = cr.GetValueOrDefault("Total_Calls")?.ToString() ?? "0", Color = "#F59E0B" });
        }

        if (tasks[5].Rows.Length > 0)
            kpis.Add(new KpiRow { Label = "Avg Handle Time", Value = $"{tasks[5].Rows[0].GetValueOrDefault("AHT_Minutes") ?? 0} min", Color = "#3B82F6" });

        if (tasks[6].Rows.Length > 0)
            kpis.Add(new KpiRow { Label = "Occupancy", Value = $"{tasks[6].Rows[0].GetValueOrDefault("Occupancy_Pct") ?? 0}%", Color = "#8B5CF6" });

        ContactSummary? contactData = null;
        if (contactResult.Rows.Length > 0)
        {
            var cr2 = contactResult.Rows[0];
            var totalCalls = Convert.ToInt32(cr2.GetValueOrDefault("Total_Calls") ?? 0);
            var contacts = Convert.ToInt32(cr2.GetValueOrDefault("Contact") ?? 0);
            var noContacts = Convert.ToInt32(cr2.GetValueOrDefault("No_Contact") ?? 0);
            var rate = totalCalls > 0 ? $"{((decimal)contacts / totalCalls * 100):F1}%" : "0%";
            contactData = new ContactSummary { Contacts = contacts.ToString(), NoContacts = noContacts.ToString(), ContactRate = rate };
        }

        var dispositions = dispositionsResult.Rows.Select(r => new DispositionRow
        {
            Status = r.GetValueOrDefault("Disposition")?.ToString() ?? "",
            Total = r.GetValueOrDefault("Total")?.ToString() ?? "0",
            Percentage = r.GetValueOrDefault("Percentage")?.ToString() ?? "0",
        }).ToList();

        var agents = agentResult.Rows.Select(r => new AgentRow
        {
            Name = r.GetValueOrDefault("Name")?.ToString() ?? r.GetValueOrDefault("User")?.ToString() ?? "",
            User = r.GetValueOrDefault("User")?.ToString() ?? "",
            CallsHandled = r.GetValueOrDefault("Calls_Handled")?.ToString() ?? "0",
            SalesMade = r.GetValueOrDefault("Sales_Made")?.ToString() ?? "0",
            Contacts = r.GetValueOrDefault("Contacts")?.ToString() ?? "0",
            Conversion = r.GetValueOrDefault("Conversion_Percentage")?.ToString() ?? "0",
            Aht = r.GetValueOrDefault("AHT")?.ToString() ?? "--",
        }).ToList();

        var recentCalls = callsResult.Rows.Take(20).Select(r => new CallRow
        {
            Agent = r.GetValueOrDefault("Name")?.ToString() ?? r.GetValueOrDefault("user")?.ToString() ?? "",
            Duration = FormatDuration(r.GetValueOrDefault("length_in_sec")),
            Disposition = r.GetValueOrDefault("status")?.ToString() ?? "",
        }).ToList();

        var data = new DashboardPdfData
        {
            Period = filter.Period,
            Kpis = kpis,
            Agents = agents,
            Dispositions = dispositions,
            RecentCalls = recentCalls,
            ContactData = contactData,
        };

        return data;
    }

    private static string FormatDuration(object? secondsObj)
    {
        if (secondsObj == null) return "--";
        var seconds = Convert.ToInt32(secondsObj);
        var m = seconds / 60;
        var s = seconds % 60;
        return $"{m}m {s}s";
    }

    private static TimeRange BuildTimeRange(TimeFilterDto filter)
    {
        if (Enum.TryParse<Domain.Enums.TimePeriod>(filter.Period, out var period))
            return period == Domain.Enums.TimePeriod.Custom
                ? TimeRange.FromCustom(filter.CustomStart!.Value, filter.CustomEnd!.Value)
                : TimeRange.FromPeriod(period);

        return TimeRange.FromPeriod(Domain.Enums.TimePeriod.Today);
    }
}

public sealed class DashboardPdfData
{
    public string Period { get; init; } = "Today";
    public string GeneratedAt { get; init; } = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
    public List<KpiRow> Kpis { get; init; } = [];
    public List<AgentRow> Agents { get; init; } = [];
    public List<DispositionRow> Dispositions { get; init; } = [];
    public List<CallRow> RecentCalls { get; init; } = [];
    public ContactSummary? ContactData { get; init; }
}

public sealed class KpiRow
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public string? Trend { get; init; }
    public string Color { get; init; } = "#3B82F6";
}

public sealed class AgentRow
{
    public string Name { get; init; } = "";
    public string User { get; init; } = "";
    public string CallsHandled { get; init; } = "0";
    public string SalesMade { get; init; } = "0";
    public string Contacts { get; init; } = "0";
    public string Conversion { get; init; } = "0";
    public string Aht { get; init; } = "--";
}

public sealed class DispositionRow
{
    public string Status { get; init; } = "";
    public string Total { get; init; } = "0";
    public string Percentage { get; init; } = "0";
}

public sealed class CallRow
{
    public string Agent { get; init; } = "";
    public string Duration { get; init; } = "--";
    public string Disposition { get; init; } = "";
}

public sealed class ContactSummary
{
    public string Contacts { get; init; } = "0";
    public string NoContacts { get; init; } = "0";
    public string ContactRate { get; init; } = "0%";
}