using FluentValidation;

namespace TransportPlanner.Application.Validators;

public class ImportPolesValidator : AbstractValidator<int>
{
    public ImportPolesValidator()
    {
        RuleFor(x => x)
            .GreaterThanOrEqualTo(1).WithMessage("Days must be at least 1")
            .LessThanOrEqualTo(31).WithMessage("Days cannot exceed 31");
    }
}

