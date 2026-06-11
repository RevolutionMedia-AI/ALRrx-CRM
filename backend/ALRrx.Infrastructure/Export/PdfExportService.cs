using ALRrx.Application.Helpers;
using ALRrx.Application.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ALRrx.Infrastructure.Export;

public sealed class PdfExportService : IReportExportService
{
    public string Format => "pdf";
    public string ContentType => "application/pdf";

    static PdfExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public Task<byte[]> ExportAsync(string reportName, string[] columns, Dictionary<string, object?>[] rows, CancellationToken ct = default)
    {
        var document = new ReportDocument(reportName, columns, rows);
        var pdfBytes = document.GeneratePdf();
        return Task.FromResult(pdfBytes);
    }

    private sealed class ReportDocument : IDocument
    {
        private readonly string _title;
        private readonly string[] _columns;
        private readonly Dictionary<string, object?>[] _rows;

        public ReportDocument(string title, string[] columns, Dictionary<string, object?>[] rows)
        {
            _title = title;
            _columns = columns;
            _rows = rows;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);

                page.Header().Row(row =>
                {
                    row.RelativeItem().Text(_title).FontSize(16).Bold().FontColor(Colors.Blue.Darken2);
                    row.RelativeItem().AlignRight().Text($"Generated: {TimeZoneHelper.NowPstString()} {TimeZoneHelper.Label}").FontSize(8).FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in _columns)
                            columns.RelativeColumn();
                    });

                    table.Header(header =>
                    {
                        foreach (var col in _columns)
                            header.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6).Text(col).FontSize(8).SemiBold().FontColor(Colors.Grey.Darken2);
                    });

                    for (var i = 0; i < _rows.Length; i++)
                    {
                        var row = _rows[i];
                        var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                        foreach (var col in _columns)
                        {
                            var value = row.GetValueOrDefault(col)?.ToString() ?? "";
                            table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(6).Text(value).FontSize(7.5f);
                        }
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Page ").FontSize(7).FontColor(Colors.Grey.Lighten1);
                    text.CurrentPageNumber().FontSize(7).FontColor(Colors.Grey.Lighten1);
                    text.Span(" / ").FontSize(7).FontColor(Colors.Grey.Lighten1);
                    text.TotalPages().FontSize(7).FontColor(Colors.Grey.Lighten1);
                });
            });
        }
    }
}