using FluentValidation;
using TransportPlanner.Application.DTOs;

namespace TransportPlanner.Application.Validators;

public class SetExtraWorkMinutesRequestValidator : AbstractValidator<SetExtraWorkMinutesRequest>
{
    public SetExtraWorkMinutesRequestValidator()
    {
        RuleFor(x => x.ExtraWorkMinutes)
            .InclusiveBetween(0, 300)
            .WithMessage("ExtraWorkMinutes must be between 0 and 300.");
    }
}

