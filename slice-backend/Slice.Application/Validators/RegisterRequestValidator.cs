using FluentValidation;
using Slice.Application.DTOs;

namespace Slice.Application.Validators;

/// <summary>
/// Validates a <see cref="RegisterRequest"/> before user creation.
/// Rules:
/// <list type="bullet">
///   <item>Email — valid format, non-empty.</item>
///   <item>Password — min 8 chars, at least one uppercase letter and one digit.</item>
///   <item>FullName — non-empty, max 100 characters.</item>
///   <item>Role — must be one of: <c>Admin</c>, <c>Supervisor</c>, <c>Viewer</c>.</item>
/// </list>
/// </summary>
public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    private static readonly string[] AllowedRoles = ["Admin", "Supervisor", "Viewer"];

    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.");

        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);

        RuleFor(x => x.Role)
            .Must(r => AllowedRoles.Contains(r))
            .WithMessage("Role must be Admin, Supervisor, or Viewer.");
    }
}
