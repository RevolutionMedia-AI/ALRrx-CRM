using ALRrx.Application.UseCases;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ALRrx.Infrastructure.Export;

public sealed class DashboardPdfService : IDashboardPdfService
{
    public string Format => "dashboard-pdf";
    public string ContentType => "application/pdf";

    static DashboardPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GenerateDashboardPdf(DashboardPdfData data)
    {
        var document = new DashboardReportDocument(data);
        return document.GeneratePdf();
    }
}

internal sealed class DashboardReportDocument : IDocument
{
    private readonly DashboardPdfData _data;

    private static readonly Dictionary<string, string> SemanticColors = new()
    {
        ["volume"] = "#1E293B",
        ["operation"] = "#4F46E5",
        ["positive"] = "#10B981",
        ["negative"] = "#EF4444",
    };

    private static readonly (string Label, string Category)[] KpiCategories =
    [
        ("Leads Dialed", "volume"),
        ("Total Calls", "volume"),
        ("Avg Handle Time", "operation"),
        ("occupancy", "operation"),
        ("Sales Today", "positive"),
        ("Leads Contacted", "positive"),
        ("contact rate", "positive"),
        ("Contacts", "positive"),
        ("No Contacts", "negative"),
    ];

    private static string GetKpiCategory(string label)
    {
        var lower = label.ToLower();
        foreach (var (key, cat) in KpiCategories)
            if (lower.Contains(key.ToLower())) return cat;
        if (lower.Contains("sales")) return "positive";
        if (lower.Contains("no contact")) return "negative";
        if (lower.Contains("contact") && !lower.Contains("rate")) return "positive";
        if (lower.Contains("contact rate") || lower.Contains("rate")) return "positive";
        if (lower.Contains("handle time") || lower.Contains("aht")) return "operation";
        if (lower.Contains("leads dialed")) return "volume";
        if (lower.Contains("leads contacted")) return "positive";
        return "volume";
    }

    private static string GetCategoryIcon(string category) => category switch
    {
        "volume" => "📊",
        "operation" => "⚙️",
        "positive" => "✅",
        "negative" => "⚠️",
        _ => "📊"
    };

    private static string GetCategoryTitle(string category) => category switch
    {
        "volume" => "Volume & Activity",
        "operation" => "Operation & Productivity",
        "positive" => "Positive Results",
        "negative" => "Losses & Missed Contacts",
        _ => category
    };

    public DashboardReportDocument(DashboardPdfData data)
    {
        _data = data;
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.MarginTop(30);
            page.MarginBottom(25);
            page.MarginLeft(25);
            page.MarginRight(25);
            page.DefaultTextStyle(ts => ts.FontSize(9).FontFamily("Inter"));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("ALTRX — Operations Report").FontSize(18).Bold().FontColor(Colors.Blue.Darken2);
                    c.Item().Text($"Period: {_data.Period}").FontSize(10).FontColor(Colors.Grey.Darken1);
                });
                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().Text("RevolutionMedia Reports").FontSize(10).Bold().FontColor(Colors.Grey.Darken2);
                    c.Item().Text($"Generated: {_data.GeneratedAt} UTC").FontSize(8).FontColor(Colors.Grey.Lighten1);
                });
            });
            col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor(Colors.Blue.Lighten3);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(col =>
        {
            col.Spacing(12);
            col.Item().Element(ComposeKpiCards);
            if (_data.ContactData != null)
                col.Item().Element(ComposeContactSummary);
            if (_data.Dispositions.Count > 0)
                col.Item().Element(ComposeDispositionsTable);
            if (_data.Agents.Count > 0)
                col.Item().Element(ComposeAgentTable);
        });
    }

    #pragma warning disable CS0618
    private void ComposeKpiCards(IContainer container)
    {
        var kpis = _data.Kpis;
        var categoryOrder = new[] { "positive", "negative", "volume", "operation" };
        var grouped = kpis.GroupBy(k => GetKpiCategory(k.Label)).ToDictionary(g => g.Key, g => g.ToList());

        container.Column(col =>
        {
            col.Spacing(10);
            foreach (var category in categoryOrder)
            {
                if (!grouped.TryGetValue(category, out var items) || items.Count == 0) continue;
                var color = SemanticColors[category];

                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Row(r =>
                        {
                            r.ConstantItem(8).Height(8).Background(color);
                            r.RelativeItem().PaddingLeft(6).AlignMiddle().Text(GetCategoryTitle(category)).FontSize(9).SemiBold().FontColor(color);
                        });
                        c.Item().PaddingTop(4).Grid(grid =>
                        {
                            grid.Columns(items.Count <= 3 ? items.Count : 3);
                            grid.HorizontalSpacing(8);
                            grid.VerticalSpacing(6);

                            foreach (var kpi in items)
                            {
                                var isSales = kpi.Label.ToLower().Contains("sales");
                                var fontSize = isSales ? 22 : 18;
                                grid.Item().Background(color + "10").Border(1).BorderColor(color + "30").Padding(8).Column(inner =>
                                {
                                    inner.Item().Text(kpi.Label).FontSize(7).FontColor(Colors.Grey.Darken1).Medium();
                                    inner.Item().Text(kpi.Value).FontSize(fontSize).Bold().FontColor(color);
                                    if (kpi.Trend != null)
                                    {
                                        var isPositive = !kpi.Trend.StartsWith('-');
                                        inner.Item().Text($"{(isPositive ? "▲" : "▼")} {kpi.Trend}").FontSize(8)
                                            .FontColor(isPositive ? SemanticColors["positive"] : SemanticColors["negative"]);
                                    }
                                });
                            }
                        });
                    });
                });
            }
        });
    }
#pragma warning restore CS0618

    private void ComposeContactSummary(IContainer container)
    {
        var c = _data.ContactData!;
        container.Background(SemanticColors["positive"] + "08").Border(1).BorderColor(SemanticColors["positive"] + "30").Padding(10).Column(col =>
        {
            col.Item().Text("Contact vs No Contact").FontSize(10).Bold().FontColor(Colors.Grey.Darken2);
#pragma warning disable CS0618
            col.Item().PaddingTop(6).Grid(grid =>
            {
                grid.Columns(3);
                grid.HorizontalSpacing(24);

                grid.Item().Column(inner =>
                {
                    inner.Item().Text(c.Contacts).FontSize(16).Bold().FontColor(SemanticColors["positive"]);
                    inner.Item().Text("Contacts").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                });
                grid.Item().Column(inner =>
                {
                    inner.Item().Text(c.NoContacts).FontSize(16).Bold().FontColor(SemanticColors["negative"]);
                    inner.Item().Text("No Contacts").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                });
                grid.Item().Column(inner =>
                {
                    inner.Item().Text(c.ContactRate).FontSize(16).Bold().FontColor(SemanticColors["operation"]);
                    inner.Item().Text("Contact Rate").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                });
            });
#pragma warning restore CS0618
        });
    }

    private void ComposeDispositionsTable(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Text("Call Dispositions").FontSize(11).Bold().FontColor(Colors.Grey.Darken2);
            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2);
                    c.RelativeColumn();
                    c.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6).Text("Disposition").FontSize(7.5f).SemiBold().FontColor(Colors.Grey.Darken2);
                    header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6).AlignCenter().Text("Total").FontSize(7.5f).SemiBold().FontColor(Colors.Grey.Darken2);
                    header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6).AlignRight().Text("%").FontSize(7.5f).SemiBold().FontColor(Colors.Grey.Darken2);
                });

                for (var i = 0; i < _data.Dispositions.Count; i++)
                {
                    var row = _data.Dispositions[i];
                    var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                    table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).Text(row.Status).FontSize(8).FontColor(Colors.Grey.Darken1);
                    table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).AlignCenter().Text(row.Total).FontSize(8).FontColor(Colors.Grey.Darken1);
                    table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).AlignRight().Text(row.Percentage).FontSize(8).FontColor(Colors.Grey.Darken1);
                }
            });
        });
    }

    private void ComposeAgentTable(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Text("Agent Performance").FontSize(11).Bold().FontColor(Colors.Grey.Darken2);
            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2);
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6).Text("Agent").FontSize(7.5f).SemiBold().FontColor(Colors.Grey.Darken2);
                    header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6).AlignCenter().Text("Calls").FontSize(7.5f).SemiBold().FontColor(Colors.Grey.Darken2);
                    header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6).AlignCenter().Text("Sales").FontSize(7.5f).SemiBold().FontColor(Colors.Grey.Darken2);
                    header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6).AlignCenter().Text("Contacts").FontSize(7.5f).SemiBold().FontColor(Colors.Grey.Darken2);
                    header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6).AlignCenter().Text("Conv %").FontSize(7.5f).SemiBold().FontColor(Colors.Grey.Darken2);
                    header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6).AlignCenter().Text("AHT").FontSize(7.5f).SemiBold().FontColor(Colors.Grey.Darken2);
                });

                for (var i = 0; i < _data.Agents.Count; i++)
                {
                    var a = _data.Agents[i];
                    var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                    table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).Text(a.Name).FontSize(8).SemiBold().FontColor(Colors.Grey.Darken1);
                    table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).AlignCenter().Text(a.CallsHandled).FontSize(8).FontColor(Colors.Grey.Darken1);
                    table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).AlignCenter().Text(a.SalesMade).FontSize(8).SemiBold().FontColor(SemanticColors["emerald"]);
                    table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).AlignCenter().Text(a.Contacts).FontSize(8).FontColor(Colors.Grey.Darken1);
                    table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).AlignCenter().Text(a.Conversion).FontSize(8).FontColor(Colors.Grey.Darken1);
                    table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).AlignCenter().Text(a.Aht).FontSize(8).FontColor(Colors.Grey.Darken1);
                }
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().PaddingTop(8).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
            col.Item().Row(row =>
            {
                row.RelativeItem().Text("RevolutionMedia Reports — ALTRX Dashboard").FontSize(7).FontColor(Colors.Grey.Lighten1);
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.Span("Page ").FontSize(7).FontColor(Colors.Grey.Lighten1);
                    text.CurrentPageNumber().FontSize(7).FontColor(Colors.Grey.Lighten1);
                    text.Span(" / ").FontSize(7).FontColor(Colors.Grey.Lighten1);
                    text.TotalPages().FontSize(7).FontColor(Colors.Grey.Lighten1);
                });
            });
        });
    }

    }