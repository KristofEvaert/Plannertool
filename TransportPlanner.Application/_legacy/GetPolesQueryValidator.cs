using FluentValidation;

namespace TransportPlanner.Application.Validators;

public class GetPolesQueryValidator : AbstractValidator<(DateTime? from, DateTime? to, string? status, int page, int pageSize)>
{
    private static readonly string[] ValidStatuses = { "New", "Planned", "InProgress", "Done", "Cancelled" };

    public GetPolesQueryValidator()
    {
        RuleFor(x => x.page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be at least 1");

        RuleFor(x => x.pageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page size must be at least 1")
            .LessThanOrEqualTo(200)
            .WithMessage("Page size cannot exceed 200");

        When(x => x.from.HasValue && x.to.HasValue, () =>
        {
            RuleFor(x => x.to!.Value)
                .GreaterThanOrEqualTo(x => x.from!.Value)
                .WithMessage("To date must be greater than or equal to from date");
        });

        When(x => !string.IsNullOrWhiteSpace(x.status), () =>
        {
            RuleFor(x => x.status!)
                .Must(status => ValidStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
                .WithMessage($"Status must be one of: {string.Join(", ", ValidStatuses)}");
        });
    }
}

