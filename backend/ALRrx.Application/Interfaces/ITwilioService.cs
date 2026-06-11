using ALRrx.Application.DTOs;

namespace ALRrx.Application.Interfaces;

public interface ITwilioService
{
    Task<TwilioSummaryDto> GetSummaryAsync(string period, DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default);
    Task<List<TwilioCallDto>> GetRecentCallsAsync(int limit = 50, CancellationToken ct = default);
    Task<List<TwilioDailyCostDto>> GetDailyCostsAsync(int days = 30, CancellationToken ct = default);
}
