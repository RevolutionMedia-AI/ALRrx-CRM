using System.Text;
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

                page.Header().Text(_title).FontSize(18).Bold().FontColor(Colors.Blue.Darken2);

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in _columns)
                            columns.RelativeColumn();
                    });

                    table.Header(header =>
                    {
                        foreach (var col in _columns)
                        {
                            header.Cell().Element(CellStyle).Text(col).Bold().FontSize(10);
                        }
                    });

                    foreach (var row in _rows)
                    {
                        foreach (var col in _columns)
                        {
                            var value = row.GetValueOrDefault(col)?.ToString() ?? "";
                            table.Cell().Element(CellStyle).Text(value).FontSize(9);
                        }
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Generated: ").FontSize(9);
                    text.Span(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")).FontSize(9);
                });
            });
        }

        private static IContainer CellStyle(IContainer container)
        {
            return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).PaddingHorizontal(6);
        }
    }
}