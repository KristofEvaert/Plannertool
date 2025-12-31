namespace TransportPlanner.Domain.Entities;

public class SystemCostSettings
{
    public int Id { get; set; }
    public decimal FuelCostPerKm { get; set; }
    public decimal PersonnelCostPerHour { get; set; }
    public string CurrencyCode { get; set; } = "EUR";
    public DateTime UpdatedAtUtc { get; set; }
}
