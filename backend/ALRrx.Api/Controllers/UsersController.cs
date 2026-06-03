using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using ALRrx.Application.UseCases;
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
    private readonly GenerateVicidialFormTokenUseCase _generateVicidialToken;

    public UsersController(
        IUserRepository users,
        IAuthService auth,
        GenerateVicidialFormTokenUseCase generateVicidialToken)
    {
        _users = users;
        _auth = auth;
        _generateVicidialToken = generateVicidialToken;
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

    [HttpPost("vicidial-form-tokens")]
    public async Task<ActionResult<VicidialFormTokenResponse>> GenerateVicidialFormToken(
        [FromBody] VicidialFormTokenRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _generateVicidialToken.ExecuteAsync(request, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
