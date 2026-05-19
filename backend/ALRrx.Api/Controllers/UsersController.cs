using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public sealed class UsersController : ControllerBase
{
    private readonly IUserRepository _users;
    private readonly IAuthService _auth;
    private readonly MySqlConnection _connection;

    public UsersController(IUserRepository users, IAuthService auth, MySqlConnection connection)
    {
        _users = users;
        _auth = auth;
        _connection = connection;
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

        var updated = user with
        {
            FullName = request.FullName ?? user.FullName,
            Role = request.Role is not null ? Enum.Parse<Domain.Enums.UserRole>(request.Role) : user.Role,
            IsActive = request.IsActive ?? user.IsActive
        };

        await _users.UpdateAsync(updated, ct);

        if (request.Password is not null)
        {
            var newHash = _auth.HashPassword(request.Password);
            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync(ct);
            await using var cmd = new MySqlCommand(
                "UPDATE alrrx_users SET PasswordHash = @Hash WHERE Id = @Id", _connection);
            cmd.Parameters.AddWithValue("@Hash", newHash);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return NoContent();
    }
}
