namespace Slice.Application.DTOs;

/// <summary>Cuerpo de la petición para el login con email y contraseña.</summary>
public record LoginRequest(string Email, string Password);

/// <summary>Cuerpo de la petición para registrar un nuevo usuario (solo Admin).</summary>
/// <param name="Role">Debe ser <c>Admin</c>, <c>Supervisor</c> o <c>Viewer</c>. Default: Viewer.</param>
public record RegisterRequest(string Email, string Password, string FullName, string Role = "Viewer");

/// <summary>Cuerpo de la petición para el login con Google OAuth.</summary>
/// <param name="AccessToken">Access token obtenido del SDK de Google en el frontend.</param>
public record GoogleLoginRequest(string AccessToken);

/// <summary>Respuesta devuelta tras un login exitoso.</summary>
/// <param name="Token">JWT firmado listo para enviar como <c>Authorization: Bearer &lt;token&gt;</c>.</param>
/// <param name="ExpiresAt">Fecha/hora UTC en que expira el token (8 horas desde la emisión).</param>
public record LoginResponse(string Token, string Email, string FullName, string Role, DateTime ExpiresAt);

/// <summary>Información pública del usuario autenticado, sin datos sensibles.</summary>
public record UserInfoDto(Guid Id, string Email, string FullName, string Role, DateTime CreatedAt);
