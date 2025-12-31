namespace TransportPlanner.Application.DTOs;

public class ServiceLocationOpeningHoursDto
{
    public int Id { get; set; }
    public int DayOfWeek { get; set; }
    public string? OpenTime { get; set; } // HH:mm
    public string? CloseTime { get; set; } // HH:mm
    public string? OpenTime2 { get; set; } // HH:mm
    public string? CloseTime2 { get; set; } // HH:mm
    public bool IsClosed { get; set; }
}

public class SaveServiceLocationOpeningHoursRequest
{
    public List<ServiceLocationOpeningHoursDto> Items { get; set; } = new();
}
