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
        ["emerald"] = "#10B981",
        ["blue"] = "#3B82F6",
        ["amber"] = "#F59E0B",
        ["rose"] = "#E11D48",
        ["violet"] = "#8B5CF6",
        ["slate"] = "#64748B",
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

    private void ComposeKpiCards(IContainer container)
    {
        var kpis = _data.Kpis;
        const int cols = 6;

        container.Column(col =>
        {
            col.Item().Text("Key Performance Indicators").FontSize(12).Bold().FontColor(Colors.Grey.Darken2);
            col.Spacing(4);

            for (var rowStart = 0; rowStart < kpis.Count; rowStart += cols)
            {
                var slice = kpis.Skip(rowStart).Take(cols).ToList();
                col.Item().PaddingTop(4).Row(row =>
                {
                    foreach (var kpi in slice)
                    {
                        var bgColor = GetKpiColor(kpi.Label);
                        row.RelativeItem().Background(bgColor + "15").Border(1).BorderColor(bgColor + "40").Padding(8).Column(c =>
                        {
                            c.Item().Text(kpi.Label).FontSize(7.5f).FontColor(Colors.Grey.Darken1).Medium();
                            c.Item().Text(kpi.Value).FontSize(18).Bold().FontColor(bgColor);
                            if (kpi.Trend != null)
                            {
                                var isPositive = !kpi.Trend.StartsWith('-');
                                c.Item().Text($"{(isPositive ? "▲" : "▼")} {kpi.Trend}").FontSize(8).FontColor(isPositive ? SemanticColors["emerald"] : SemanticColors["rose"]);
                            }
                        });
                    }
                });
            }
        });
    }

    private void ComposeContactSummary(IContainer container)
    {
        var c = _data.ContactData!;
        container.Background(SemanticColors["blue"] + "12").Border(1).BorderColor(SemanticColors["blue"] + "30").Padding(10).Column(col =>
        {
            col.Item().Text("Contact vs No Contact").FontSize(10).Bold().FontColor(Colors.Grey.Darken2);
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Column(inner =>
                {
                    inner.Item().Text(c.Contacts).FontSize(16).Bold().FontColor(SemanticColors["blue"]);
                    inner.Item().Text("Contacts").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                });
                row.RelativeItem().Column(inner =>
                {
                    inner.Item().Text(c.NoContacts).FontSize(16).Bold().FontColor(SemanticColors["rose"]);
                    inner.Item().Text("No Contacts").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                });
                row.RelativeItem().Column(inner =>
                {
                    inner.Item().Text(c.ContactRate).FontSize(16).Bold().FontColor(SemanticColors["violet"]);
                    inner.Item().Text("Contact Rate").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                });
            });
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
        container.PaddingTop(8).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
        container.Row(row =>
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
    }

    private static string GetKpiColor(string label) => label.ToLower() switch
    {
        var l when l.Contains("sales") => SemanticColors["emerald"],
        var l when l.Contains("contact") && !l.Contains("no") && !l.Contains("rate") => SemanticColors["blue"],
        var l when l.Contains("no contact") => SemanticColors["rose"],
        var l when l.Contains("total call") => SemanticColors["amber"],
        var l when l.Contains("handle time") || l.Contains("aht") => SemanticColors["blue"],
        var l when l.Contains("occupancy") => SemanticColors["violet"],
        var l when l.Contains("contact rate") || l.Contains("rate") => SemanticColors["violet"],
        var l when l.Contains("leads dialed") => SemanticColors["blue"],
        var l when l.Contains("leads contacted") => SemanticColors["emerald"],
        _ => SemanticColors["slate"],
    };
}