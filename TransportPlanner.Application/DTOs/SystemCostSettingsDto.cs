namespace TransportPlanner.Application.DTOs;

public class SystemCostSettingsDto
{
    public int? OwnerId { get; set; }
    public decimal FuelCostPerKm { get; set; }
    public decimal PersonnelCostPerHour { get; set; }
    public string CurrencyCode { get; set; } = "EUR";
}
