using FluentValidation;
using TransportPlanner.Application.DTOs;

namespace TransportPlanner.Application.Validators;

public class AddStopNoteRequestValidator : AbstractValidator<AddStopNoteRequest>
{
    public AddStopNoteRequestValidator()
    {
        RuleFor(x => x.Note)
            .NotEmpty().WithMessage("Note cannot be empty")
            .MaximumLength(1000).WithMessage("Note cannot exceed 1000 characters");
    }
}

