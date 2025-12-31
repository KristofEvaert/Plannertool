namespace TransportPlanner.Domain.Entities;

public class ServiceLocationConstraint
{
    public int ServiceLocationId { get; set; }
    public int? MinVisitDurationMinutes { get; set; }
    public int? MaxVisitDurationMinutes { get; set; }

    public ServiceLocation ServiceLocation { get; set; } = null!;
}
