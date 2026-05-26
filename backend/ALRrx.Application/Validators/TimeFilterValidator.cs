using ALRrx.Application.DTOs;
using ALRrx.Domain.Enums;
using FluentValidation;

namespace ALRrx.Application.Validators;

public sealed class TimeFilterValidator : AbstractValidator<TimeFilterDto>
{
    public TimeFilterValidator()
    {
        RuleFor(x => x.Period)
            .NotEmpty()
            .Must(p => Enum.TryParse<TimePeriod>(p, ignoreCase: true, out _))
            .WithMessage("Invalid time period. Valid values: LastHour, Today, ThisWeek, ThisMonth, Custom");

        When(x => x.Period == nameof(TimePeriod.Custom), () =>
        {
            RuleFor(x => x.CustomStart)
                .NotNull().WithMessage("CustomStart is required when period is Custom");

            RuleFor(x => x.CustomEnd)
                .NotNull().WithMessage("CustomEnd is required when period is Custom");

            RuleFor(x => x)
                .Must(x => x.CustomStart < x.CustomEnd)
                .WithMessage("CustomStart must be before CustomEnd");
        });
    }
}
