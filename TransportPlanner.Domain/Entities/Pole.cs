namespace TransportPlanner.Domain.Entities;

public class Pole
{
    public int Id { get; set; }
    public string Serial { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? FixedDate { get; set; }
    public PoleStatus Status { get; set; }
}

