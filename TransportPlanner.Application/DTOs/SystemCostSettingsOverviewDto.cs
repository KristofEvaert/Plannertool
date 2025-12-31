namespace TransportPlanner.Application.DTOs;

public class SystemCostSettingsOverviewDto
{
    public int OwnerId { get; set; }
    public string OwnerCode { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public bool OwnerIsActive { get; set; }
    public decimal FuelCostPerKm { get; set; }
    public decimal PersonnelCostPerHour { get; set; }
    public string CurrencyCode { get; set; } = "EUR";
    public DateTime? UpdatedAtUtc { get; set; }
}
