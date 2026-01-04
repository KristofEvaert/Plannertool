using Microsoft.EntityFrameworkCore;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Services;
using Xunit;

namespace TransportPlanner.Tests;

public class TravelTimeModelServiceTests
{
    [Fact]
    public async Task EstimateMinutes_PicksHighestPriorityRegion()
    {
        var options = new DbContextOptionsBuilder<TransportPlannerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new TransportPlannerDbContext(options);

        db.TravelTimeRegions.AddRange(
            new TravelTimeRegion
            {
                Id = 1,
                Name = "LowPriority",
                CountryCode = "XX",
                BboxMinLat = 0,
                BboxMinLon = 0,
                BboxMaxLat = 10,
                BboxMaxLon = 10,
                Priority = 10
            },
            new TravelTimeRegion
            {
                Id = 2,
                Name = "HighPriority",
                CountryCode = "XX",
                BboxMinLat = 0,
                BboxMinLon = 0,
                BboxMaxLat = 10,
                BboxMaxLon = 10,
                Priority = 20
            },
            new TravelTimeRegion
            {
                Id = 99,
                Name = "DEFAULT",
                CountryCode = "XX",
                BboxMinLat = -90,
                BboxMinLon = -180,
                BboxMaxLat = 90,
                BboxMaxLon = 180,
                Priority = 0
            });

        db.RegionSpeedProfiles.AddRange(
            new RegionSpeedProfile
            {
                RegionId = 1,
                DayType = DayType.Weekday,
                BucketStartHour = 9,
                BucketEndHour = 10,
                AvgMinutesPerKm = 1.0m
            },
            new RegionSpeedProfile
            {
                RegionId = 2,
                DayType = DayType.Weekday,
                BucketStartHour = 9,
                BucketEndHour = 10,
                AvgMinutesPerKm = 2.0m
            },
            new RegionSpeedProfile
            {
                RegionId = 99,
                DayType = DayType.Weekday,
                BucketStartHour = 9,
                BucketEndHour = 10,
                AvgMinutesPerKm = 3.0m
            });

        await db.SaveChangesAsync();

        var service = new TravelTimeModelService(
            db,
            Microsoft.Extensions.Options.Options.Create(new TransportPlanner.Infrastructure.Options.TravelTimeModelQualitySettings()));
        var minutes = await service.EstimateMinutesAsync(
            new DateTime(2025, 1, 6),
            departureMinute: 9 * 60,
            distanceKm: 10,
            startLat: 5,
            startLng: 5,
            endLat: 6,
            endLng: 6);

        Assert.Equal(20.0, minutes, 2);
    }
}
