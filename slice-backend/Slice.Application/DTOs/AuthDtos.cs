namespace Slice.Application.DTOs;

public record LoginRequest(string Email, string Password);

public record RegisterRequest(string Email, string Password, string FullName, string Role = "Viewer");

public record GoogleLoginRequest(string AccessToken);

public record LoginResponse(string Token, string Email, string FullName, string Role, DateTime ExpiresAt);

public record UserInfoDto(Guid Id, string Email, string FullName, string Role, DateTime CreatedAt);
