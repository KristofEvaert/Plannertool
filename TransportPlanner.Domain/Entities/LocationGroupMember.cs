namespace TransportPlanner.Domain.Entities;

public class LocationGroupMember
{
    public int LocationGroupId { get; set; }
    public int ServiceLocationId { get; set; }

    public LocationGroup LocationGroup { get; set; } = null!;
    public ServiceLocation ServiceLocation { get; set; } = null!;
}
