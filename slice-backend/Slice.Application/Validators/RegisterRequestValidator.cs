using FluentValidation;
using Slice.Application.DTOs;

namespace Slice.Application.Validators;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    private static readonly string[] AllowedRoles = ["Admin", "Supervisor", "Viewer"];

    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.");
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).Must(r => AllowedRoles.Contains(r))
            .WithMessage("Role must be Admin, Supervisor, or Viewer.");
    }
}
