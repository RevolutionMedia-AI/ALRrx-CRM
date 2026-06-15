using System.Security.Claims;
using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using ALRrx.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
[EnableRateLimiting("admin")]
public sealed class AdminController : ControllerBase
{
    private readonly GetAdminUsersUseCase _listUsers;
    private readonly GetAdminUserDetailUseCase _userDetail;
    private readonly ApproveUserUseCase _approve;
    private readonly RejectUserUseCase _reject;
    private readonly SuspendUserUseCase _suspend;
    private readonly ReactivateUserUseCase _reactivate;
    private readonly ChangeUserRoleUseCase _changeRole;
    private readonly SetUserPlatformAccessUseCase _setPlatformAccess;
    private readonly IRoleRepository _roles;
    private readonly IAuditLogRepository _audit;

    public AdminController(
        GetAdminUsersUseCase listUsers,
        GetAdminUserDetailUseCase userDetail,
        ApproveUserUseCase approve,
        RejectUserUseCase reject,
        SuspendUserUseCase suspend,
        ReactivateUserUseCase reactivate,
        ChangeUserRoleUseCase changeRole,
        SetUserPlatformAccessUseCase setPlatformAccess,
        IRoleRepository roles,
        IAuditLogRepository audit)
    {
        _listUsers = listUsers;
        _userDetail = userDetail;
        _approve = approve;
        _reject = reject;
        _suspend = suspend;
        _reactivate = reactivate;
        _changeRole = changeRole;
        _setPlatformAccess = setPlatformAccess;
        _roles = roles;
        _audit = audit;
    }

    private string? ClientIp =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ??
        Request.Headers["X-Forwarded-For"].FirstOrDefault();

    private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet("users")]
    public async Task<ActionResult<PagedResult<AdminUserDto>>> ListUsers(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _listUsers.ExecuteAsync(new AdminUserListQuery(status, search, page, pageSize), ct);
        return Ok(result);
    }

    [HttpGet("users/{id:int}")]
    public async Task<ActionResult<AdminUserDetailResponse>> GetUser(int id, CancellationToken ct = default)
    {
        var (user, audit) = await _userDetail.ExecuteAsync(id, ct);
        if (user is null) return NotFound();
        return Ok(new AdminUserDetailResponse { User = user, Audit = audit });
    }

    [HttpPost("users/{id:int}/approve")]
    public async Task<ActionResult<AdminActionResultDto>> Approve(
        int id,
        [FromBody] ApproveUserRequest body,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _approve.ExecuteAsync(id, body.RoleId, CurrentUserId, ClientIp, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("users/{id:int}/reject")]
    public async Task<ActionResult<AdminActionResultDto>> Reject(
        int id,
        [FromBody] RejectUserRequest body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body.Reason))
            return BadRequest(new { error = "Reason is required" });
        try
        {
            var result = await _reject.ExecuteAsync(id, body.Reason, CurrentUserId, ClientIp, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("users/{id:int}/suspend")]
    public async Task<ActionResult<AdminActionResultDto>> Suspend(
        int id,
        [FromBody] SuspendUserRequest body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body.Reason))
            return BadRequest(new { error = "Reason is required" });
        try
        {
            var result = await _suspend.ExecuteAsync(id, body.Reason, CurrentUserId, ClientIp, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("users/{id:int}/reactivate")]
    public async Task<ActionResult<AdminActionResultDto>> Reactivate(int id, CancellationToken ct = default)
    {
        try
        {
            var result = await _reactivate.ExecuteAsync(id, CurrentUserId, ClientIp, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("users/{id:int}/role")]
    public async Task<ActionResult<AdminActionResultDto>> ChangeRole(
        int id,
        [FromBody] ChangeUserRoleRequest body,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _changeRole.ExecuteAsync(id, body.RoleId, CurrentUserId, ClientIp, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("users/{id:int}/platform-access")]
    public async Task<ActionResult<AdminActionResultDto>> SetPlatformAccess(
        int id,
        [FromBody] SetUserPlatformAccessRequest body,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _setPlatformAccess.ExecuteAsync(id, body.PlatformAccess, CurrentUserId, ClientIp, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("audit")]
    public async Task<ActionResult<List<AuditLogEntryDto>>> RecentAudit(
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var entries = await _audit.GetRecentAsync(Math.Clamp(limit, 1, 500), ct);
        var dtos = entries.Select(e => new AuditLogEntryDto
        {
            Id = e.Id,
            UserId = e.UserId,
            Action = e.Action,
            PerformedBy = e.PerformedBy,
            OldValue = e.OldValue,
            NewValue = e.NewValue,
            Reason = e.Reason,
            IpAddress = e.IpAddress,
            CreatedAt = e.CreatedAt,
        }).ToList();
        return Ok(dtos);
    }
}

public sealed class AdminUserDetailResponse
{
    public AdminUserDto User { get; init; } = null!;
    public List<AuditLogEntryDto> Audit { get; init; } = [];
}
