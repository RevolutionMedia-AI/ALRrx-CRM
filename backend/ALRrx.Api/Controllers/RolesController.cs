using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public sealed class RolesController : ControllerBase
{
    private readonly IRoleRepository _roles;

    public RolesController(IRoleRepository roles)
    {
        _roles = roles;
    }

    [HttpGet]
    public async Task<ActionResult<List<RoleDto>>> GetAll(CancellationToken ct = default)
    {
        var roles = await _roles.GetAllWithPermissionsAsync(ct);
        return Ok(roles.Select(r => new RoleDto
        {
            Id = r.Id,
            Name = r.Name,
            Description = r.Description,
            IsSystem = r.IsSystem,
            Permissions = r.Permissions,
        }).ToList());
    }
}
