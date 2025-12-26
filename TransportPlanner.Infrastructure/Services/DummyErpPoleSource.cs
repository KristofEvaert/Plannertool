using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Services;

/// <summary>
/// Dummy ERP pole source that generates deterministic poles for testing.
/// Uses stable random generation based on date range to produce consistent results.
/// </summary>
public class DummyErpPoleSource
{
    private readonly Random _random;

    public DummyErpPoleSource(int seed = 42)
    {
        _random = new Random(seed);
    }

    public List<PoleData> GeneratePolesForDateRange(DateTime fromDate, DateTime toDate)
    {
        var poles = new List<PoleData>();
        var currentDate = fromDate.Date;

        // Use date-based seed for deterministic generation
        var dateSeed = fromDate.GetHashCode() + toDate.GetHashCode();
        var dateRandom = new Random(dateSeed);

        while (currentDate <= toDate.Date)
        {
            // Generate approximately 50 poles per day (with some variation)
            var polesPerDay = dateRandom.Next(45, 56); // 45-55 poles per day, average ~50
            var daySeed = currentDate.GetHashCode();
            var dayRandom = new Random(daySeed);

            for (int i = 0; i < polesPerDay; i++)
            {
                var serial = $"ERP-{currentDate:yyyyMMdd}-{i + 1:D3}";
                
                // Belgian coordinates range
                var latitude = (decimal)(50.5 + dayRandom.NextDouble() * 1.5); // 50.5 to 52.0
                var longitude = (decimal)(3.0 + dayRandom.NextDouble() * 3.0); // 3.0 to 6.0
                
                // Due date can be within the range or slightly after
                var dueDate = currentDate.AddDays(dayRandom.Next(-7, 15));
                
                // 90% of poles have no fixed date
                var fixedDate = dayRandom.NextDouble() < 0.1 
                    ? (DateTime?)currentDate.AddDays(dayRandom.Next(0, 7))
                    : null;

                poles.Add(new PoleData
                {
                    Serial = serial,
                    Latitude = latitude,
                    Longitude = longitude,
                    DueDate = dueDate,
                    FixedDate = fixedDate,
                    ServiceMinutes = 20 // Default service time
                });
            }

            currentDate = currentDate.AddDays(1);
        }

        return poles;
    }
}

public class PoleData
{
    public string Serial { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? FixedDate { get; set; }
    public int ServiceMinutes { get; set; }
}

