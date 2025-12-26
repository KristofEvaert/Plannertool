namespace TransportPlanner.Application.DTOs;

public class UserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public List<string> Roles { get; set; } = new();
    public Guid? DriverToolId { get; set; }
    public int? DriverOwnerId { get; set; }
    public int? OwnerId { get; set; }
    public string? DriverStartAddress { get; set; }
    public double? DriverStartLatitude { get; set; }
    public double? DriverStartLongitude { get; set; }
}
