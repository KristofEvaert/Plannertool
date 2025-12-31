namespace TransportPlanner.Application.DTOs;

public class UpdateServiceTypeRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
    public int? OwnerId { get; set; }
}

