using ALRrx.Application.DTOs;

namespace ALRrx.Application.Interfaces;

public interface IGoogleSheetsImportService
{
    Task<List<SaleRecord>> GetSalesAsync(CancellationToken cancellationToken = default);
}
