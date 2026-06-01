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

    private static string GetKpiColor(string label) => label.ToLower() switch
    {
        var l when l.Contains("google sheets sales") => "#10B981",
        var l when l.Contains("google sheets count") => "#3B82F6",
        var l when l.Contains("last gs sale") => "#F59E0B",
        var l when l.Contains("sales") => "#10B981",
        var l when l.Contains("no contact") => "#EF4444",
        var l when l.Contains("contact rate") || l.Contains("rate") => "#10B981",
        var l when l.Contains("contact") => "#10B981",
        var l when l.Contains("leads dialed") => "#1E293B",
        var l when l.Contains("leads contacted") => "#10B981",
        var l when l.Contains("total call") => "#1E293B",
        var l when l.Contains("handle time") || l.Contains("aht") => "#2563EB",
        var l when l.Contains("occupancy") => "#2563EB",
        _ => "#64748B"
    };

    private static float GetKpiFontSize(string label) =>
        label.ToLower().Contains("sales") || label.ToLower().Contains("last gs") ? 22 : 18;

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
            if (_data.GoogleSheets.Sales.Count > 0)
                col.Item().Element(ComposeGoogleSheetsSection);
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

        container.Column(col =>
        {
            col.Item().Text("Key Performance Indicators").FontSize(12).Bold().FontColor(Colors.Grey.Darken2);
            col.Item().PaddingTop(6).Grid(grid =>
            {
                grid.Columns(6);
                grid.HorizontalSpacing(8);
                grid.VerticalSpacing(8);

                foreach (var kpi in kpis)
                {
                    var color = GetKpiColor(kpi.Label);
                    var fontSize = GetKpiFontSize(kpi.Label);
                    grid.Item().Background("#F8F9FA").Border(1).BorderColor(color + "40").Padding(8).Column(c =>
                    {
                        c.Item().Text(kpi.Label).FontSize(7.5f).FontColor(Colors.Grey.Darken1).Medium();
                        c.Item().Text(kpi.Value).FontSize(fontSize).Bold().FontColor(color);
                        if (kpi.Trend != null)
                        {
                            var isPositive = !kpi.Trend.StartsWith('-');
                            c.Item().Text($"{(isPositive ? "▲" : "▼")} {kpi.Trend}").FontSize(8)
                                .FontColor(isPositive ? "#10B981" : "#EF4444");
                        }
                    });
                }
            });
        });
    }
#pragma warning restore CS0618

    private void ComposeContactSummary(IContainer container)
    {
        var c = _data.ContactData!;
        container.Background("#F8F9FA").Border(1).BorderColor("#10B981" + "30").Padding(10).Column(col =>
        {
            col.Item().Text("Contact vs No Contact").FontSize(10).Bold().FontColor(Colors.Grey.Darken2);
#pragma warning disable CS0618
            col.Item().PaddingTop(6).Grid(grid =>
            {
                grid.Columns(3);
                grid.HorizontalSpacing(24);

                grid.Item().Column(inner =>
                {
                    inner.Item().Text(c.Contacts).FontSize(16).Bold().FontColor("#10B981");
                    inner.Item().Text("Contacts").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                });
                grid.Item().Column(inner =>
                {
                    inner.Item().Text(c.NoContacts).FontSize(16).Bold().FontColor("#EF4444");
                    inner.Item().Text("No Contacts").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                });
                grid.Item().Column(inner =>
                {
                    inner.Item().Text(c.ContactRate).FontSize(16).Bold().FontColor("#10B981");
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
                    table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).AlignCenter().Text(a.SalesMade).FontSize(8).SemiBold().FontColor("#10B981");
                    table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).AlignCenter().Text(a.Contacts).FontSize(8).FontColor(Colors.Grey.Darken1);
                    table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).AlignCenter().Text(a.Conversion).FontSize(8).FontColor(Colors.Grey.Darken1);
                    table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).AlignCenter().Text(a.Aht).FontSize(8).FontColor(Colors.Grey.Darken1);
                }
            });
        });
    }

    private void ComposeGoogleSheetsSection(IContainer container)
    {
        var gs = _data.GoogleSheets;
        container.Background("#F0FDF4").Border(1).BorderColor("#10B981" + "40").Padding(10).Column(col =>
        {
            col.Item().Text("Google Sheets Sales (Forms)").FontSize(11).Bold().FontColor("#10B981");

            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text($"${gs.TotalSales:F0}").FontSize(20).Bold().FontColor("#10B981");
                    c.Item().Text("Total Sales").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                });
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(gs.TotalCount.ToString()).FontSize(20).Bold().FontColor("#3B82F6");
                    c.Item().Text("Total Count").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                });
                if (gs.LastSale != null)
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text($"${gs.LastSale.Amount:F0}").FontSize(20).Bold().FontColor("#F59E0B");
                        c.Item().Text($"Last Sale: {gs.LastSale.SellerName}").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                        c.Item().Text(gs.LastSale.Timestamp.ToString("yyyy-MM-dd HH:mm")).FontSize(7).FontColor(Colors.Grey.Darken1);
                    });
                }
            });

            if (gs.Sales.Count > 0)
            {
                col.Item().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(1.5f);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                        c.RelativeColumn(1);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6).Text("Date").FontSize(7.5f).SemiBold().FontColor(Colors.Grey.Darken2);
                        header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6).Text("Seller").FontSize(7.5f).SemiBold().FontColor(Colors.Grey.Darken2);
                        header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6).Text("Email").FontSize(7.5f).SemiBold().FontColor(Colors.Grey.Darken2);
                        header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6).Text("Package").FontSize(7.5f).SemiBold().FontColor(Colors.Grey.Darken2);
                        header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6).AlignRight().Text("Amount").FontSize(7.5f).SemiBold().FontColor(Colors.Grey.Darken2);
                    });

                    var topSales = gs.Sales.Take(20).ToList();
                    for (var i = 0; i < topSales.Count; i++)
                    {
                        var sale = topSales[i];
                        var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                        table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).Text(sale.Timestamp.ToString("yyyy-MM-dd HH:mm")).FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                        table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).Text(sale.SellerName).FontSize(7.5f).SemiBold().FontColor(Colors.Grey.Darken1);
                        table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).Text(sale.CustomerEmail).FontSize(7).FontColor(Colors.Grey.Darken1);
                        table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).Text(sale.Package).FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                        table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).AlignRight().Text($"${sale.Amount:F0}").FontSize(8).SemiBold().FontColor("#10B981");
                    }
                });
            }
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