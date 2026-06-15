namespace ALRrx.Application.DTOs;

public sealed record ApproveUserRequest(int RoleId);

public sealed record RejectUserRequest(string Reason);

public sealed record SuspendUserRequest(string Reason);

public sealed record ChangeUserRoleRequest(int RoleId);

public sealed record AdminUserListQuery(
    string? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20);

public sealed record AdminUserDto
{
    public int Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public int RoleId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public string Status { get; init; } = "Pending";
    public string PlatformAccess { get; init; } = "None";
    public bool IsActive { get; init; }
    public int? ApprovedBy { get; init; }
    public string? ApprovedByName { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public string? RejectionReason { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public int FailedLoginAttempts { get; init; }
    public DateTime? LockedUntil { get; init; }
    public DateTime CreatedAt { get; init; }
    public List<string> Permissions { get; init; } = [];
}

public sealed record SetUserPlatformAccessRequest(string PlatformAccess);

public sealed record AuditLogEntryDto
{
    public long Id { get; init; }
    public int UserId { get; init; }
    public string UserEmail { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public int? PerformedBy { get; init; }
    public string? PerformedByEmail { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string? Reason { get; init; }
    public string? IpAddress { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record PasswordResetResult
{
    public string TemporaryPassword { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public bool EmailSent { get; init; }
    public string? EmailError { get; init; }
}

public sealed record EmailResult(bool Sent, string? Error);

public sealed record AdminActionResultDto
{
    public AdminUserDto User { get; init; } = null!;
    public bool EmailSent { get; init; }
    public string? EmailError { get; init; }
}
