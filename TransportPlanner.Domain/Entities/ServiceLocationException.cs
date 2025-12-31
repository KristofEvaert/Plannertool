namespace TransportPlanner.Domain.Entities;

public class ServiceLocationException
{
    public int Id { get; set; }
    public int ServiceLocationId { get; set; }
    public DateTime Date { get; set; } // Date-only
    public TimeSpan? OpenTime { get; set; }
    public TimeSpan? CloseTime { get; set; }
    public bool IsClosed { get; set; }
    public string? Note { get; set; }

    public ServiceLocation ServiceLocation { get; set; } = null!;
}
