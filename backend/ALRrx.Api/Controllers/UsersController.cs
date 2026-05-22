using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public sealed class UsersController : ControllerBase
{
    private readonly IUserRepository _users;
    private readonly IAuthService _auth;

    public UsersController(IUserRepository users, IAuthService auth)
    {
        _users = users;
        _auth = auth;
    }

    [HttpGet]
    public async Task<ActionResult<List<UserInfoDto>>> GetAll(CancellationToken ct = default)
    {
        var users = await _users.GetAllAsync(ct);
        var dtos = users.Select(u => new UserInfoDto
        {
            Id = u.Id,
            Email = u.Email,
            FullName = u.FullName,
            Role = u.Role.ToString(),
            IsActive = u.IsActive,
            CreatedAt = u.CreatedAt
        }).ToList();
        return Ok(dtos);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserInfoDto>> GetById(int id, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return NotFound();

        return Ok(new UserInfoDto
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role.ToString(),
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt
        });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return NotFound();

        var newHash = request.Password is not null ? _auth.HashPassword(request.Password) : user.PasswordHash;

        var updated = user with
        {
            FullName = request.FullName ?? user.FullName,
            PasswordHash = newHash,
            Role = request.Role is not null ? Enum.Parse<Domain.Enums.UserRole>(request.Role) : user.Role,
            IsActive = request.IsActive ?? user.IsActive
        };

        await _users.UpdateAsync(updated, ct);

        return NoContent();
    }
}
