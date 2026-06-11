using ALRrx.Application.Helpers;
using ALRrx.Application.UseCases;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ALRrx.Infrastructure.Export;

public sealed class TwilioPdfService : ITwilioPdfService
{
    public string Format => "twilio-pdf";
    public string ContentType => "application/pdf";

    static TwilioPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GenerateTwilioPdf(TwilioExportData data) =>
        new TwilioReportDocument(data).GeneratePdf();
}

internal sealed class TwilioReportDocument : IDocument
{
    private readonly TwilioExportData _data;

    public TwilioReportDocument(TwilioExportData data) => _data = data;

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
                    c.Item().Text("ALRrx — Twilio Costs Report").FontSize(18).Bold().FontColor("#3B82F6");
                    c.Item().Text($"Period: {_data.Period}").FontSize(10).FontColor(Colors.Grey.Darken1);
                });
                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().Text("RevolutionMedia Reports").FontSize(10).Bold().FontColor(Colors.Grey.Darken2);
                    c.Item().Text($"Generated: {_data.GeneratedAt} {TimeZoneHelper.Label}").FontSize(8).FontColor(Colors.Grey.Lighten1);
                });
            });
            col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor("#3B82F6");
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(col =>
        {
            col.Spacing(12);
            col.Item().Element(ComposeKpiCards);
            if (_data.Daily.Count > 0) col.Item().Element(ComposeDailyTable);
            if (_data.RecentCalls.Count > 0) col.Item().Element(ComposeRecentCallsTable);
        });
    }

    private void ComposeKpiCards(IContainer container)
    {
        var s = _data.Summary;
        var totalCostDisplay = FormatCost(s.TotalCost);
        var costPerMin = s.TotalMinutes > 0 ? s.TotalCost / s.TotalMinutes : 0;

        container.Column(col =>
        {
            col.Item().Text("Key Metrics").FontSize(12).Bold().FontColor(Colors.Grey.Darken2);
            col.Item().PaddingTop(6).Grid(grid =>
            {
                grid.Columns(4);
                grid.HorizontalSpacing(8);
                grid.VerticalSpacing(8);

                KpiCell(grid, "Total Spend", totalCostDisplay, "#3B82F6");
                KpiCell(grid, "Calls", s.TotalCalls.ToString("N0"), "#10B981",
                    $"{s.InboundCalls} in · {s.OutboundCalls} out");
                KpiCell(grid, "Minutes", s.TotalMinutes.ToString("N0"), "#F59E0B",
                    s.TotalCalls > 0 ? $"~{s.TotalMinutes / s.TotalCalls} min/call" : "—");
                KpiCell(grid, "Cost / Minute", FormatCost(costPerMin), "#94A3B8", "blended average");
            });
        });
    }

    private static void KpiCell(GridDescriptor grid, string label, string value, string color, string sub = "")
    {
        grid.Item().Background("#F8F9FA").Border(1).BorderColor(color + "40").Padding(8).Column(c =>
        {
            c.Item().Text(label).FontSize(7.5f).FontColor(Colors.Grey.Darken1).Medium();
            c.Item().Text(value).FontSize(16).Bold().FontColor(color);
            if (!string.IsNullOrEmpty(sub))
                c.Item().Text(sub).FontSize(7).FontColor(Colors.Grey.Darken1);
        });
    }

    private void ComposeDailyTable(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Text("Daily Cost Breakdown (last 30 days)").FontSize(11).Bold().FontColor(Colors.Grey.Darken2);
            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2);
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                });

                table.Header(header =>
                {
                    HeaderCell(header, "Date");
                    HeaderCell(header, "Cost (USD)", alignRight: true);
                    HeaderCell(header, "Calls", alignCenter: true);
                    HeaderCell(header, "Minutes", alignRight: true);
                });

                for (var i = 0; i < _data.Daily.Count; i++)
                {
                    var d = _data.Daily[i];
                    var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                    BodyCell(table, bg, TimeZoneHelper.ToPstString(d.Date, "yyyy-MM-dd"));
                    BodyCell(table, bg, FormatCost(d.Cost), alignRight: true, bold: true);
                    BodyCell(table, bg, d.CallCount.ToString("N0"), alignCenter: true);
                    BodyCell(table, bg, d.Minutes.ToString("N0"), alignRight: true);
                }
            });
        });
    }

    private void ComposeRecentCallsTable(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Text("Recent Calls (latest 20)").FontSize(11).Bold().FontColor(Colors.Grey.Darken2);
            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2);
                    c.RelativeColumn(0.7f);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                    c.RelativeColumn(0.7f);
                    c.RelativeColumn(0.5f);
                    c.RelativeColumn(0.8f);
                });

                table.Header(header =>
                {
                    HeaderCell(header, "Time (PST)");
                    HeaderCell(header, "Dir.");
                    HeaderCell(header, "From");
                    HeaderCell(header, "To");
                    HeaderCell(header, "Status", alignCenter: true);
                    HeaderCell(header, "Dur.", alignRight: true);
                    HeaderCell(header, "Cost", alignRight: true);
                });

                var limit = Math.Min(20, _data.RecentCalls.Count);
                for (var i = 0; i < limit; i++)
                {
                    var c = _data.RecentCalls[i];
                    var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                    BodyCell(table, bg, TimeZoneHelper.ToPstString(c.StartTime, "MM-dd HH:mm"));
                    BodyCell(table, bg, c.Direction == "inbound" ? "IN" : "OUT", alignCenter: true);
                    BodyCell(table, bg, c.From);
                    BodyCell(table, bg, c.To);
                    BodyCell(table, bg, c.Status, alignCenter: true);
                    BodyCell(table, bg, FormatDuration(c.DurationSeconds), alignRight: true);
                    BodyCell(table, bg, FormatCost(c.Cost), alignRight: true, bold: true);
                }
            });
        });
    }

    private static void HeaderCell(QuestPDF.Infrastructure.ITableCellDescriptor cell, string text, bool alignCenter = false, bool alignRight = false)
    {
        cell.Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6)
            .Text(text).FontSize(7.5f).SemiBold().FontColor(Colors.Grey.Darken2);
        if (alignCenter) cell.AlignCenter();
        if (alignRight) cell.AlignRight();
    }

    private static void BodyCell(QuestPDF.Fluent.TableDescriptor table, string bg, string text, bool alignCenter = false, bool alignRight = false, bool bold = false)
    {
        var cell = table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6)
            .Text(text).FontSize(8).FontColor(Colors.Grey.Darken1);
        if (bold) cell.SemiBold();
        if (alignCenter) cell.AlignCenter();
        if (alignRight) cell.AlignRight();
    }

    private static string FormatCost(decimal n)
    {
        if (n == 0m) return "$0.0000000";
        var abs = Math.Abs(n);
        if (abs < 1m) return "$" + n.ToString("F7", System.Globalization.CultureInfo.InvariantCulture);
        if (abs < 100m) return "$" + n.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        return "$" + n.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatDuration(int s)
    {
        var m = s / 60;
        var sec = s % 60;
        return $"{m}:{sec:D2}";
    }

    private void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().PaddingTop(8).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
            col.Item().Row(row =>
            {
                row.RelativeItem().Text("RevolutionMedia Reports — Twilio Costs").FontSize(7).FontColor(Colors.Grey.Lighten1);
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
