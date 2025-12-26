namespace TransportPlanner.Application.DTOs;

public class DriverDto
{
    public Guid ToolId { get; set; }
    public int ErpId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? StartAddress { get; set; }
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }
    public int DefaultServiceMinutes { get; set; }
    public int MaxWorkMinutesPerDay { get; set; }
    public int OwnerId { get; set; }
    public string? OwnerName { get; set; } // Convenience field from backend
    public bool IsActive { get; set; }
}
