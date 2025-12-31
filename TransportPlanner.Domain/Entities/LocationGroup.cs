namespace TransportPlanner.Domain.Entities;

public class LocationGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? OwnerId { get; set; }

    public ICollection<LocationGroupMember> Members { get; set; } = new List<LocationGroupMember>();
    public ICollection<WeightTemplate> WeightTemplates { get; set; } = new List<WeightTemplate>();
}
