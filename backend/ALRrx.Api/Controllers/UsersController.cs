using System.Security.Claims;
using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using ALRrx.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public sealed class UsersController : ControllerBase
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IAuthService _auth;
    private readonly IAuditLogRepository _audit;

    public UsersController(IUserRepository users, IRoleRepository roles, IAuthService auth, IAuditLogRepository audit)
    {
        _users = users;
        _roles = roles;
        _auth = auth;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<List<UserInfoDto>>> GetAll(CancellationToken ct = default)
    {
        var users = await _users.GetAllAsync(ct);
        return Ok(users.Select(MapUser).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserInfoDto>> GetById(int id, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return NotFound();
        return Ok(MapUser(user));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return NotFound();

        var newHash = request.Password is not null ? _auth.HashPassword(request.Password) : user.PasswordHash;
        var newRoleId = request.RoleId ?? user.RoleId;

        await _users.SetRoleAsync(id, newRoleId, ct);
        if (request.IsActive.HasValue)
        {
            var status = request.IsActive.Value ? UserStatus.Active : UserStatus.Suspended;
            var adminId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            await _users.SetStatusAsync(id, status, adminId, null, ct);
        }

        await _audit.LogAsync(new Domain.Entities.UserAuditLog
        {
            UserId = id,
            Action = "RoleChanged",
            PerformedBy = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value),
            OldValue = user.RoleName,
            NewValue = (await _roles.GetByIdAsync(newRoleId, ct))?.Name ?? user.RoleName,
        }, ct);

        return NoContent();
    }

    private static UserInfoDto MapUser(Domain.Entities.AuthUser u) => new()
    {
        Id = u.Id,
        Email = u.Email,
        FullName = u.FullName,
        RoleId = u.RoleId,
        Role = u.RoleName,
        Status = u.Status.ToString(),
        IsActive = u.IsActive,
        LastLoginAt = u.LastLoginAt,
        CreatedAt = u.CreatedAt,
        Permissions = u.Permissions,
    };
}
