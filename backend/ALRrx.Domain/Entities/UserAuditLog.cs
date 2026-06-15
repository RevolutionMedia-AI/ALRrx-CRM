namespace ALRrx.Domain.Entities;

public sealed class UserAuditLog
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public int? PerformedBy { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
