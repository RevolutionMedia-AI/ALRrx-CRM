namespace ALRrx.Application.DTOs;

public sealed record LoginRequest
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

public sealed record GoogleLoginRequest
{
    public string AccessToken { get; init; } = string.Empty;
}

public sealed record LoginResponse
{
    public string Token { get; init; } = string.Empty;
    public UserInfoDto User { get; init; } = null!;
}

public sealed record RegisterRequest
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public int RoleId { get; init; } = 0;
}

public sealed record UserInfoDto
{
    public int Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public int RoleId { get; init; }
    public string Role { get; init; } = string.Empty;
    public string Status { get; init; } = "Pending";
    public bool IsActive { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public List<string> Permissions { get; init; } = [];
}

public sealed record UpdateUserRequest
{
    public string? FullName { get; init; }
    public string? Password { get; init; }
    public int? RoleId { get; init; }
    public bool? IsActive { get; init; }
}

public sealed record EditRowRequest
{
    public Dictionary<string, object?> Updates { get; init; } = [];
}

public sealed record RoleDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsSystem { get; init; }
    public List<string> Permissions { get; init; } = [];
}

public sealed record PermissionDto
{
    public int Id { get; init; }
    public string KeyName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Module { get; init; } = string.Empty;
}

public sealed record PagedResult<T>
{
    public List<T> Items { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}
