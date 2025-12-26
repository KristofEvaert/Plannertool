namespace TransportPlanner.Application.DTOs;

public class AssignRolesRequest
{
    public Guid UserId { get; set; }
    public List<string> Roles { get; set; } = new();
    public int? OwnerIdForDriver { get; set; }
    public int? OwnerIdForStaff { get; set; }
    public string? DisplayName { get; set; }
    public string? DriverStartAddress { get; set; }
    public double? DriverStartLatitude { get; set; }
    public double? DriverStartLongitude { get; set; }
}
