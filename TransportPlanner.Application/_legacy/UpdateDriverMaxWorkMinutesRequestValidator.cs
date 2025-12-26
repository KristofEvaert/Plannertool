using FluentValidation;
using TransportPlanner.Application.DTOs;

namespace TransportPlanner.Application.Validators;

public class UpdateDriverMaxWorkMinutesRequestValidator : AbstractValidator<UpdateDriverMaxWorkMinutesRequest>
{
    public UpdateDriverMaxWorkMinutesRequestValidator()
    {
        RuleFor(x => x.MaxWorkMinutesPerDay)
            .InclusiveBetween(60, 900)
            .WithMessage("MaxWorkMinutesPerDay must be between 60 and 900.");
    }
}

