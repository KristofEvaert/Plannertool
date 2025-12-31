namespace TransportPlanner.Application.DTOs;

public class LocationGroupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? OwnerId { get; set; }
    public List<int> ServiceLocationIds { get; set; } = new();
}

public class SaveLocationGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? OwnerId { get; set; }
    public List<int> ServiceLocationIds { get; set; } = new();
}
