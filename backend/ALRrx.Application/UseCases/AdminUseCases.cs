using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using ALRrx.Domain.Entities;
using ALRrx.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ALRrx.Application.UseCases;

internal static class AdminAuditHelpers
{
    public static async Task LogEmailFailureAsync(IAuditLogRepository audit, int userId, int performedBy, EmailResult result, string? ip, CancellationToken ct = default)
    {
        if (result.Sent) return;
        await audit.LogAsync(new UserAuditLog
        {
            UserId = userId,
            Action = nameof(AuditAction.EmailFailed),
            PerformedBy = performedBy,
            OldValue = null,
            NewValue = "EmailFailed",
            Reason = result.Error ?? "Unknown email error",
            IpAddress = ip,
        }, ct);
    }
}

public sealed class GetAdminUsersUseCase
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IAuditLogRepository _audit;
    private readonly ILogger<GetAdminUsersUseCase> _logger;

    public GetAdminUsersUseCase(IUserRepository users, IRoleRepository roles, IAuditLogRepository audit, ILogger<GetAdminUsersUseCase> logger)
    {
        _users = users;
        _roles = roles;
        _audit = audit;
        _logger = logger;
    }

    public async Task<PagedResult<AdminUserDto>> ExecuteAsync(AdminUserListQuery query, CancellationToken ct = default)
    {
        var all = string.IsNullOrEmpty(query.Status)
            ? await _users.GetAllAsync(ct)
            : Enum.TryParse<UserStatus>(query.Status, true, out var st)
                ? await _users.GetByStatusAsync(st, ct)
                : await _users.GetAllAsync(ct);

        IEnumerable<AuthUser> filtered = all;
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            filtered = filtered.Where(u => u.Email.Contains(s, StringComparison.OrdinalIgnoreCase)
                                       || u.FullName.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.ToList();
        var total = list.Count;

        var skip = Math.Max(0, (query.Page - 1) * query.PageSize);
        var page = list.Skip(skip).Take(query.PageSize).ToList();

        var dtos = new List<AdminUserDto>();
        foreach (var u in page)
        {
            dtos.Add(new AdminUserDto
            {
                Id = u.Id,
                Email = u.Email,
                FullName = u.FullName,
                RoleId = u.RoleId,
                RoleName = u.RoleName,
                Status = u.Status.ToString(),
                PlatformAccess = u.PlatformAccess.ToString(),
                IsActive = u.IsActive,
                ApprovedBy = u.ApprovedBy,
                ApprovedAt = u.ApprovedAt,
                RejectionReason = u.RejectionReason,
                LastLoginAt = u.LastLoginAt,
                FailedLoginAttempts = u.FailedLoginAttempts,
                LockedUntil = u.LockedUntil,
                CreatedAt = u.CreatedAt,
                Permissions = u.Permissions,
            });
        }

        return new PagedResult<AdminUserDto>
        {
            Items = dtos,
            Total = total,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }
}

public sealed class GetAdminUserDetailUseCase
{
    private readonly IUserRepository _users;
    private readonly IAuditLogRepository _audit;

    public GetAdminUserDetailUseCase(IUserRepository users, IAuditLogRepository audit)
    {
        _users = users;
        _audit = audit;
    }

    public async Task<(AdminUserDto? user, List<AuditLogEntryDto> audit)> ExecuteAsync(int userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return (null, []);

        var dto = new AdminUserDto
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            RoleId = user.RoleId,
            RoleName = user.RoleName,
            Status = user.Status.ToString(),
            IsActive = user.IsActive,
            ApprovedBy = user.ApprovedBy,
            ApprovedAt = user.ApprovedAt,
            RejectionReason = user.RejectionReason,
            LastLoginAt = user.LastLoginAt,
            FailedLoginAttempts = user.FailedLoginAttempts,
            LockedUntil = user.LockedUntil,
            CreatedAt = user.CreatedAt,
            Permissions = user.Permissions,
        };

        var entries = await _audit.GetForUserAsync(userId, 50, ct);
        var dtos = entries.Select(e => new AuditLogEntryDto
        {
            Id = e.Id,
            UserId = e.UserId,
            UserEmail = user.Email,
            Action = e.Action,
            PerformedBy = e.PerformedBy,
            OldValue = e.OldValue,
            NewValue = e.NewValue,
            Reason = e.Reason,
            IpAddress = e.IpAddress,
            CreatedAt = e.CreatedAt,
        }).ToList();

        return (dto, dtos);
    }
}

public sealed class ApproveUserUseCase
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IAuditLogRepository _audit;
    private readonly IEmailService _email;
    private readonly IRevokedUserStore _revoked;
    private readonly ILogger<ApproveUserUseCase> _logger;

    public ApproveUserUseCase(IUserRepository users, IRoleRepository roles, IAuditLogRepository audit, IEmailService email, IRevokedUserStore revoked, ILogger<ApproveUserUseCase> logger)
    {
        _users = users;
        _roles = roles;
        _audit = audit;
        _email = email;
        _revoked = revoked;
        _logger = logger;
    }

    public async Task<AdminActionResultDto> ExecuteAsync(int userId, int roleId, int performedBy, string? ip, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new InvalidOperationException("User not found");
        var role = await _roles.GetByIdAsync(roleId, ct) ?? throw new InvalidOperationException("Role not found");

        var oldStatus = user.Status.ToString();
        await _users.SetStatusAsync(userId, UserStatus.Active, performedBy, null, ct);
        await _users.SetRoleAsync(userId, roleId, ct);

        await _audit.LogAsync(new UserAuditLog
        {
            UserId = userId,
            Action = nameof(AuditAction.Approved),
            PerformedBy = performedBy,
            OldValue = oldStatus,
            NewValue = UserStatus.Active.ToString(),
            IpAddress = ip,
        }, ct);

        await _audit.LogAsync(new UserAuditLog
        {
            UserId = userId,
            Action = nameof(AuditAction.RoleChanged),
            PerformedBy = performedBy,
            OldValue = user.RoleName,
            NewValue = role.Name,
            IpAddress = ip,
        }, ct);

        var emailResult = await _email.SendAccountApprovedAsync(user.Email, user.FullName, role.Name, ct);
        await AdminAuditHelpers.LogEmailFailureAsync(_audit, userId, performedBy, emailResult, ip, ct);

        var updated = await _users.GetByIdAsync(userId, ct) ?? throw new InvalidOperationException("User disappeared");
        return new AdminActionResultDto
        {
            User = new AdminUserDto
            {
                Id = updated.Id,
                Email = updated.Email,
                FullName = updated.FullName,
                RoleId = updated.RoleId,
                RoleName = updated.RoleName,
                Status = updated.Status.ToString(),
                PlatformAccess = updated.PlatformAccess.ToString(),
                IsActive = updated.IsActive,
                ApprovedBy = updated.ApprovedBy,
                ApprovedAt = updated.ApprovedAt,
                RejectionReason = updated.RejectionReason,
                LastLoginAt = updated.LastLoginAt,
                CreatedAt = updated.CreatedAt,
                Permissions = updated.Permissions,
            },
            EmailSent = emailResult.Sent,
            EmailError = emailResult.Error,
        };
    }
}

public sealed class RejectUserUseCase
{
    private readonly IUserRepository _users;
    private readonly IAuditLogRepository _audit;
    private readonly IEmailService _email;
    private readonly IRevokedUserStore _revoked;

    public RejectUserUseCase(IUserRepository users, IAuditLogRepository audit, IEmailService email, IRevokedUserStore revoked)
    {
        _users = users;
        _audit = audit;
        _email = email;
        _revoked = revoked;
    }

    public async Task<AdminActionResultDto> ExecuteAsync(int userId, string reason, int performedBy, string? ip, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new InvalidOperationException("User not found");
        if (userId == performedBy)
            throw new InvalidOperationException("You cannot reject yourself");
        if (user.RoleName == "Admin")
            throw new InvalidOperationException("Cannot reject an admin � demote them first");
        var oldStatus = user.Status.ToString();
        await _users.SetStatusAsync(userId, UserStatus.Rejected, performedBy, reason, ct);
        _revoked.Revoke(userId, performedBy, $"Rejected: {reason}");

        await _audit.LogAsync(new UserAuditLog
        {
            UserId = userId,
            Action = nameof(AuditAction.Rejected),
            PerformedBy = performedBy,
            OldValue = oldStatus,
            NewValue = UserStatus.Rejected.ToString(),
            Reason = reason,
            IpAddress = ip,
        }, ct);

        await _audit.LogAsync(new UserAuditLog
        {
            UserId = userId,
            Action = nameof(AuditAction.TokenRevoked),
            PerformedBy = performedBy,
            Reason = reason,
            IpAddress = ip,
        }, ct);

        var emailResult = await _email.SendAccountRejectedAsync(user.Email, user.FullName, reason, ct);
        await AdminAuditHelpers.LogEmailFailureAsync(_audit, userId, performedBy, emailResult, ip);

        var updated = await _users.GetByIdAsync(userId, ct) ?? throw new InvalidOperationException("User disappeared");
        return new AdminActionResultDto
        {
            User = new AdminUserDto
            {
                Id = updated.Id,
                Email = updated.Email,
                FullName = updated.FullName,
                RoleId = updated.RoleId,
                RoleName = updated.RoleName,
                Status = updated.Status.ToString(),
                PlatformAccess = updated.PlatformAccess.ToString(),
                IsActive = updated.IsActive,
                ApprovedBy = updated.ApprovedBy,
                ApprovedAt = updated.ApprovedAt,
                RejectionReason = updated.RejectionReason,
                LastLoginAt = updated.LastLoginAt,
                CreatedAt = updated.CreatedAt,
                Permissions = updated.Permissions,
            },
            EmailSent = emailResult.Sent,
            EmailError = emailResult.Error,
        };
    }
}

public sealed class SuspendUserUseCase
{
    private readonly IUserRepository _users;
    private readonly IAuditLogRepository _audit;
    private readonly IEmailService _email;
    private readonly IRevokedUserStore _revoked;

    public SuspendUserUseCase(IUserRepository users, IAuditLogRepository audit, IEmailService email, IRevokedUserStore revoked)
    {
        _users = users;
        _audit = audit;
        _email = email;
        _revoked = revoked;
    }

    public async Task<AdminActionResultDto> ExecuteAsync(int userId, string reason, int performedBy, string? ip, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new InvalidOperationException("User not found");
        if (userId == performedBy)
            throw new InvalidOperationException("You cannot suspend yourself");
        if (user.RoleName == "Admin")
            throw new InvalidOperationException("Cannot suspend an admin � demote them first");
        var oldStatus = user.Status.ToString();
        await _users.SetStatusAsync(userId, UserStatus.Suspended, performedBy, reason, ct);
        _revoked.Revoke(userId, performedBy, $"Suspended: {reason}");

        await _audit.LogAsync(new UserAuditLog
        {
            UserId = userId,
            Action = nameof(AuditAction.Suspended),
            PerformedBy = performedBy,
            OldValue = oldStatus,
            NewValue = UserStatus.Suspended.ToString(),
            Reason = reason,
            IpAddress = ip,
        }, ct);

        await _audit.LogAsync(new UserAuditLog
        {
            UserId = userId,
            Action = nameof(AuditAction.TokenRevoked),
            PerformedBy = performedBy,
            Reason = reason,
            IpAddress = ip,
        }, ct);

        var emailResult = await _email.SendAccountSuspendedAsync(user.Email, user.FullName, reason, ct);
        await AdminAuditHelpers.LogEmailFailureAsync(_audit, userId, performedBy, emailResult, ip);

        var updated = await _users.GetByIdAsync(userId, ct) ?? throw new InvalidOperationException("User disappeared");
        return new AdminActionResultDto
        {
            User = new AdminUserDto
            {
                Id = updated.Id,
                Email = updated.Email,
                FullName = updated.FullName,
                RoleId = updated.RoleId,
                RoleName = updated.RoleName,
                Status = updated.Status.ToString(),
                PlatformAccess = updated.PlatformAccess.ToString(),
                IsActive = updated.IsActive,
                ApprovedBy = updated.ApprovedBy,
                ApprovedAt = updated.ApprovedAt,
                RejectionReason = updated.RejectionReason,
                LastLoginAt = updated.LastLoginAt,
                CreatedAt = updated.CreatedAt,
                Permissions = updated.Permissions,
            },
            EmailSent = emailResult.Sent,
            EmailError = emailResult.Error,
        };
    }
}

public sealed class ReactivateUserUseCase
{
    private readonly IUserRepository _users;
    private readonly IAuditLogRepository _audit;
    private readonly IRevokedUserStore _revoked;

    public ReactivateUserUseCase(IUserRepository users, IAuditLogRepository audit, IRevokedUserStore revoked)
    {
        _users = users;
        _audit = audit;
        _revoked = revoked;
    }

    public async Task<AdminActionResultDto> ExecuteAsync(int userId, int performedBy, string? ip, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new InvalidOperationException("User not found");
        var oldStatus = user.Status.ToString();
        await _users.SetStatusAsync(userId, UserStatus.Active, performedBy, null, ct);
        _revoked.Clear(userId);

        await _audit.LogAsync(new UserAuditLog
        {
            UserId = userId,
            Action = nameof(AuditAction.Reactivated),
            PerformedBy = performedBy,
            OldValue = oldStatus,
            NewValue = UserStatus.Active.ToString(),
            IpAddress = ip,
        }, ct);

        var updated = await _users.GetByIdAsync(userId, ct) ?? throw new InvalidOperationException("User disappeared");
        return new AdminActionResultDto
        {
            User = new AdminUserDto
            {
                Id = updated.Id,
                Email = updated.Email,
                FullName = updated.FullName,
                RoleId = updated.RoleId,
                RoleName = updated.RoleName,
                Status = updated.Status.ToString(),
                PlatformAccess = updated.PlatformAccess.ToString(),
                IsActive = updated.IsActive,
                ApprovedBy = updated.ApprovedBy,
                ApprovedAt = updated.ApprovedAt,
                RejectionReason = updated.RejectionReason,
                LastLoginAt = updated.LastLoginAt,
                CreatedAt = updated.CreatedAt,
                Permissions = updated.Permissions,
            },
            EmailSent = true,
            EmailError = null,
        };
    }
}

public sealed class ChangeUserRoleUseCase
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IAuditLogRepository _audit;
    private readonly IEmailService _email;

    public ChangeUserRoleUseCase(
        IUserRepository users,
        IRoleRepository roles,
        IAuditLogRepository audit,
        IEmailService email)
    {
        _users = users;
        _roles = roles;
        _audit = audit;
        _email = email;
    }

    public async Task<AdminUserDto> ExecuteAsync(int userId, int roleId, int performedBy, string? ip, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new InvalidOperationException("User not found");
        if (userId == performedBy && user.RoleName == "Admin" && roleId != user.RoleId)
            throw new InvalidOperationException("You cannot change your own role away from Admin");
        var role = await _roles.GetByIdAsync(roleId, ct) ?? throw new InvalidOperationException("Role not found");

        var oldRoleName = user.RoleName;
        await _users.SetRoleAsync(userId, roleId, ct);

        await _audit.LogAsync(new UserAuditLog
        {
            UserId = userId,
            Action = nameof(AuditAction.RoleChanged),
            PerformedBy = performedBy,
            OldValue = oldRoleName,
            NewValue = role.Name,
            IpAddress = ip,
        }, ct);

        // Notify the user when access is actually granted — i.e. they
        // move from the no-permission Pending role into something with
        // real permissions. We don't email on any other role change
        // (e.g. Employee → Supervisor) because the user is already
        // inside the platform and the admin panel is the source of
        // truth for their new state. Only the initial grant is news
        // they need to act on.
        if (string.Equals(oldRoleName, "Pending", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role.Name, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            var updatedForEmail = await _users.GetByIdAsync(userId, ct) ?? user;
            var platformName = MapPlatformAccessLabel(updatedForEmail.PlatformAccess);
            var emailResult = await _email.SendPlatformAccessGrantedAsync(
                updatedForEmail.Email,
                updatedForEmail.FullName,
                role.Name,
                platformName,
                ct);
            await AdminAuditHelpers.LogEmailFailureAsync(_audit, userId, performedBy, emailResult, ip, ct);
        }

        var updated = await _users.GetByIdAsync(userId, ct) ?? throw new InvalidOperationException("User disappeared");
        return new AdminUserDto
        {
            Id = updated.Id,
            Email = updated.Email,
            FullName = updated.FullName,
            RoleId = updated.RoleId,
            RoleName = updated.RoleName,
            Status = updated.Status.ToString(),
            PlatformAccess = updated.PlatformAccess.ToString(),
            IsActive = updated.IsActive,
            ApprovedBy = updated.ApprovedBy,
            ApprovedAt = updated.ApprovedAt,
            RejectionReason = updated.RejectionReason,
            LastLoginAt = updated.LastLoginAt,
            CreatedAt = updated.CreatedAt,
            Permissions = updated.Permissions,
        };
    }

    private static string MapPlatformAccessLabel(PlatformAccess access) => access switch
    {
        PlatformAccess.Altrx => "ALTRX",
        PlatformAccess.Slice => "SLICE",
        PlatformAccess.Both  => "ALTRX + SLICE",
        _                    => "the platform",
    };
}

public sealed class SetUserPlatformAccessUseCase
{
    private readonly IUserRepository _users;
    private readonly IAuditLogRepository _audit;
    private readonly IRevokedUserStore _revoked;

    public SetUserPlatformAccessUseCase(IUserRepository users, IAuditLogRepository audit, IRevokedUserStore revoked)
    {
        _users = users;
        _audit = audit;
        _revoked = revoked;
    }

    public async Task<AdminActionResultDto> ExecuteAsync(int userId, string platformAccess, int performedBy, string? ip, CancellationToken ct = default)
    {
        if (!Enum.TryParse<PlatformAccess>(platformAccess, true, out var access))
            throw new InvalidOperationException($"Invalid platform access: '{platformAccess}'. Use None, Altrx, Slice, or Both.");

        var user = await _users.GetByIdAsync(userId, ct) ?? throw new InvalidOperationException("User not found");
        if (userId == performedBy && access == PlatformAccess.None)
            throw new InvalidOperationException("You cannot remove your own platform access");

        var oldAccess = user.PlatformAccess;
        if (oldAccess == access) return ToResult(user);

        await _users.SetPlatformAccessAsync(userId, access, performedBy, ct);

        // Revoke the user's existing token so they re-login with the new access.
        // BUG-008 immediate revocation � necessary because the platform picker
        // is gated only by the JWT, and the new access needs to be picked up
        // on next login.
        _revoked.Revoke(userId, performedBy, $"PlatformAccess changed from {oldAccess} to {access}");

        await _audit.LogAsync(new UserAuditLog
        {
            UserId = userId,
            Action = nameof(AuditAction.RoleChanged),
            PerformedBy = performedBy,
            OldValue = oldAccess.ToString(),
            NewValue = access.ToString(),
            Reason = "PlatformAccess changed",
            IpAddress = ip,
        }, ct);

        await _audit.LogAsync(new UserAuditLog
        {
            UserId = userId,
            Action = nameof(AuditAction.TokenRevoked),
            PerformedBy = performedBy,
            Reason = $"PlatformAccess changed: {oldAccess} -> {access}",
            IpAddress = ip,
        }, ct);

        var updated = await _users.GetByIdAsync(userId, ct) ?? throw new InvalidOperationException("User disappeared");
        return ToResult(updated);
    }

    private static AdminActionResultDto ToResult(AuthUser u) => new()
    {
        User = new AdminUserDto
        {
            Id = u.Id,
            Email = u.Email,
            FullName = u.FullName,
            RoleId = u.RoleId,
            RoleName = u.RoleName,
            Status = u.Status.ToString(),
            PlatformAccess = u.PlatformAccess.ToString(),
            IsActive = u.IsActive,
            ApprovedBy = u.ApprovedBy,
            ApprovedAt = u.ApprovedAt,
            RejectionReason = u.RejectionReason,
            LastLoginAt = u.LastLoginAt,
            CreatedAt = u.CreatedAt,
            Permissions = u.Permissions,
        },
        EmailSent = true,
        EmailError = null,
    };
}
