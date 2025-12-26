namespace TransportPlanner.Application.DTOs;

public class BulkErrorDto
{
    public string Scope { get; set; } = string.Empty; // "Driver" or "Availability"
    public string RowRef { get; set; } = string.Empty; // e.g. "Drivers row 5" / "Availability row 12"
    public string Message { get; set; } = string.Empty;
}

