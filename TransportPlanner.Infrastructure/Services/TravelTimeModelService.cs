using Microsoft.EntityFrameworkCore;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;

namespace TransportPlanner.Infrastructure.Services;

public class TravelTimeModelService : ITravelTimeModelService
{
    private const int LearnedSampleThreshold = 30;
    private static readonly decimal[] DistanceBands = { 0, 5, 15, 30, 60, 120, 10000 };

    private readonly TransportPlannerDbContext _dbContext;

    public TravelTimeModelService(TransportPlannerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<double> EstimateMinutesAsync(
        DateTime date,
        int departureMinute,
        double distanceKm,
        double startLat,
        double startLng,
        double endLat,
        double endLng,
        CancellationToken cancellationToken = default)
    {
        var region = await ResolveRegionAsync(startLat, startLng, endLat, endLng, cancellationToken);
        var dayType = GetDayType(date);
        var (bucketStart, bucketEnd) = GetBucketRange(departureMinute);
        var (bandMin, bandMax) = GetDistanceBand(distanceKm);

        var learned = await _dbContext.LearnedTravelStats
            .AsNoTracking()
            .Where(x => x.RegionId == region.Id
                && x.DayType == dayType
                && x.BucketStartHour == bucketStart
                && x.BucketEndHour == bucketEnd
                && x.DistanceBandKmMin == bandMin
                && x.DistanceBandKmMax == bandMax
                && x.SampleCount >= LearnedSampleThreshold)
            .FirstOrDefaultAsync(cancellationToken);

        if (learned != null)
        {
            return (double)(learned.AvgMinutesPerKm * (decimal)distanceKm);
        }

        var profile = await _dbContext.RegionSpeedProfiles
            .AsNoTracking()
            .Where(x => x.RegionId == region.Id
                && x.DayType == dayType
                && x.BucketStartHour == bucketStart
                && x.BucketEndHour == bucketEnd)
            .FirstOrDefaultAsync(cancellationToken);

        if (profile == null && region.Id != 99)
        {
            profile = await _dbContext.RegionSpeedProfiles
                .AsNoTracking()
                .Where(x => x.RegionId == 99
                    && x.DayType == dayType
                    && x.BucketStartHour == bucketStart
                    && x.BucketEndHour == bucketEnd)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (profile == null)
        {
            return (distanceKm / 50.0) * 60.0;
        }

        return (double)(profile.AvgMinutesPerKm * (decimal)distanceKm);
    }

    public async Task UpdateLearnedStatsAsync(
        DateTime date,
        int departureMinute,
        double distanceKm,
        double travelMinutes,
        double? stopServiceMinutes,
        double startLat,
        double startLng,
        double endLat,
        double endLng,
        CancellationToken cancellationToken = default)
    {
        var region = await ResolveRegionAsync(startLat, startLng, endLat, endLng, cancellationToken);
        var dayType = GetDayType(date);
        var (bucketStart, bucketEnd) = GetBucketRange(departureMinute);
        var (bandMin, bandMax) = GetDistanceBand(distanceKm);

        var stat = await _dbContext.LearnedTravelStats
            .FirstOrDefaultAsync(x => x.RegionId == region.Id
                && x.DayType == dayType
                && x.BucketStartHour == bucketStart
                && x.BucketEndHour == bucketEnd
                && x.DistanceBandKmMin == bandMin
                && x.DistanceBandKmMax == bandMax, cancellationToken);

        if (stat == null)
        {
            stat = new LearnedTravelStats
            {
                RegionId = region.Id,
                DayType = dayType,
                BucketStartHour = bucketStart,
                BucketEndHour = bucketEnd,
                DistanceBandKmMin = bandMin,
                DistanceBandKmMax = bandMax,
                SampleCount = 0,
                AvgMinutesPerKm = 0,
                AvgStopServiceMinutes = stopServiceMinutes.HasValue ? (decimal?)stopServiceMinutes.Value : null
            };
            _dbContext.LearnedTravelStats.Add(stat);
        }

        var minutesPerKm = distanceKm > 0 ? travelMinutes / distanceKm : 0;
        var sampleCount = stat.SampleCount;
        var newSampleCount = sampleCount + 1;

        stat.AvgMinutesPerKm = (decimal)(((double)stat.AvgMinutesPerKm * sampleCount + minutesPerKm) / newSampleCount);
        stat.SampleCount = newSampleCount;

        if (stopServiceMinutes.HasValue)
        {
            var existingService = stat.AvgStopServiceMinutes.HasValue
                ? (double)stat.AvgStopServiceMinutes.Value
                : stopServiceMinutes.Value;
            var averaged = (existingService * sampleCount + stopServiceMinutes.Value) / newSampleCount;
            stat.AvgStopServiceMinutes = (decimal)averaged;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<TravelTimeRegion> ResolveRegionAsync(
        double startLat,
        double startLng,
        double endLat,
        double endLng,
        CancellationToken cancellationToken)
    {
        var midLat = (startLat + endLat) / 2.0;
        var midLng = (startLng + endLng) / 2.0;

        var regions = await _dbContext.TravelTimeRegions
            .AsNoTracking()
            .OrderByDescending(r => r.Priority)
            .ToListAsync(cancellationToken);

        var region = regions.FirstOrDefault(r =>
            midLat >= (double)r.BboxMinLat && midLat <= (double)r.BboxMaxLat
            && midLng >= (double)r.BboxMinLon && midLng <= (double)r.BboxMaxLon);

        return region ?? regions.First(r => r.Id == 99);
    }

    private static DayType GetDayType(DateTime date)
    {
        return date.DayOfWeek switch
        {
            DayOfWeek.Saturday => DayType.Saturday,
            DayOfWeek.Sunday => DayType.Sunday,
            _ => DayType.Weekday
        };
    }

    private static (int StartHour, int EndHour) GetBucketRange(int departureMinute)
    {
        var hour = Math.Clamp(departureMinute / 60, 0, 23);
        return (hour, hour + 1);
    }

    private static (decimal Min, decimal Max) GetDistanceBand(double distanceKm)
    {
        var distance = (decimal)Math.Max(0, distanceKm);
        for (var i = 0; i < DistanceBands.Length - 1; i++)
        {
            if (distance >= DistanceBands[i] && distance < DistanceBands[i + 1])
            {
                return (DistanceBands[i], DistanceBands[i + 1]);
            }
        }

        return (DistanceBands[^2], DistanceBands[^1]);
    }
}
