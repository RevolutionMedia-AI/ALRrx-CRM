using FluentValidation;
using Slice.Application.DTOs;

namespace Slice.Application.Validators;

/// <summary>
/// Validates a <see cref="LoginRequest"/> before it reaches the authentication logic.
/// Rules: Email must be a valid address; Password must be non-empty and at least 6 characters.
/// </summary>
public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6);
    }
}
