namespace TransportPlanner.Application.DTOs;

public class ServiceLocationExceptionDto
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string? OpenTime { get; set; } // HH:mm
    public string? CloseTime { get; set; } // HH:mm
    public bool IsClosed { get; set; }
    public string? Note { get; set; }
}

public class SaveServiceLocationExceptionsRequest
{
    public List<ServiceLocationExceptionDto> Items { get; set; } = new();
}
