namespace TransportPlanner.Domain.Entities;

public class WeightTemplateLocationLink
{
    public int WeightTemplateId { get; set; }
    public int ServiceLocationId { get; set; }

    public WeightTemplate WeightTemplate { get; set; } = null!;
    public ServiceLocation ServiceLocation { get; set; } = null!;
}
