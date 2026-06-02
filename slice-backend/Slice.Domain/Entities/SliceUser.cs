namespace Slice.Domain.Entities;

/// <summary>
/// Represents an authenticated Slice platform user.
/// Users may be seeded from configuration, registered by an Admin,
/// or auto-provisioned on first Google OAuth login.
/// </summary>
public sealed class SliceUser
{
    /// <summary>Unique identifier, assigned at creation.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Primary email address used for authentication and communications.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// BCrypt-hashed password. Empty string for users who authenticate exclusively via Google OAuth.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Display name shown in the UI and email headers.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Authorization role: <c>Admin</c>, <c>Supervisor</c>, or <c>Viewer</c>.</summary>
    public string Role { get; set; } = "Viewer";

    /// <summary>Whether the account is enabled. Deactivated users cannot authenticate.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>UTC timestamp when the user was first created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
