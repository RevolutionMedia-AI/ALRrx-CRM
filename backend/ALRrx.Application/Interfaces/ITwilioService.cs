using ALRrx.Application.DTOs;

namespace ALRrx.Application.Interfaces;

public interface ITwilioService
{
    Task<TwilioSummaryDto> GetSummaryAsync(string period, DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default);
    Task<List<TwilioCallDto>> GetRecentCallsAsync(string period = "today", int limit = 50, CancellationToken ct = default);
    Task<List<TwilioDailyCostDto>> GetDailyCostsAsync(string period = "today", CancellationToken ct = default);
}
