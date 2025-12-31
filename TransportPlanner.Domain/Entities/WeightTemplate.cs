namespace TransportPlanner.Domain.Entities;

public class WeightTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public WeightTemplateScopeType ScopeType { get; set; } = WeightTemplateScopeType.Global;
    public int? OwnerId { get; set; }
    public int? ServiceTypeId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public decimal WeightDistance { get; set; } = 1;
    public decimal WeightTravelTime { get; set; } = 1;
    public decimal WeightOvertime { get; set; } = 1;
    public decimal WeightCost { get; set; } = 1;
    public decimal WeightDate { get; set; } = 1;

    public ICollection<WeightTemplateLocationLink> LocationLinks { get; set; } = new List<WeightTemplateLocationLink>();
}
