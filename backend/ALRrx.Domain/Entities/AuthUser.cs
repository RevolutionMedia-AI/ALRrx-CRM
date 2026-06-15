using ALRrx.Domain.Enums;

namespace ALRrx.Domain.Entities;

public sealed record AuthUser
{
    public int Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public int RoleId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public UserStatus Status { get; init; } = UserStatus.Pending;
    public PlatformAccess PlatformAccess { get; init; } = PlatformAccess.None;
    public bool IsActive { get; init; } = true;
    public int? ApprovedBy { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public string? RejectionReason { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public int FailedLoginAttempts { get; init; }
    public DateTime? LockedUntil { get; init; }
    public int? CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public List<string> Permissions { get; init; } = [];
}
