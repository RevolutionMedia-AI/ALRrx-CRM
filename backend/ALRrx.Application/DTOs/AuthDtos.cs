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
    public string Role { get; init; } = "Employee";
}

public sealed record UserInfoDto
{
    public int Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record UpdateUserRequest
{
    public string? FullName { get; init; }
    public string? Password { get; init; }
    public string? Role { get; init; }
    public bool? IsActive { get; init; }
}

public sealed record EditRowRequest
{
    public Dictionary<string, object?> Updates { get; init; } = [];
}

public sealed record DeleteRowRequest
{
    public string Column { get; init; } = string.Empty;
    public object? Value { get; init; } = null;
}
