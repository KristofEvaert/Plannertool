namespace TransportPlanner.Application.DTOs;

public class CreateServiceTypeRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? OwnerId { get; set; }
}

