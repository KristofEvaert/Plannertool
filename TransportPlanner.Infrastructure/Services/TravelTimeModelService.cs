using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Options;

namespace TransportPlanner.Infrastructure.Services;

public class TravelTimeModelService : ITravelTimeModelService
{
    private static readonly decimal[] DistanceBands = { 0, 5, 15, 30, 60, 120, 10000 };

    private readonly TransportPlannerDbContext _dbContext;
    private readonly TravelTimeModelQualitySettings _settings;

    public TravelTimeModelService(
        TransportPlannerDbContext dbContext,
        IOptions<TravelTimeModelQualitySettings> settings)
    {
        _dbContext = dbContext;
        _settings = settings?.Value ?? new TravelTimeModelQualitySettings();
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

        var learnedQuery = _dbContext.LearnedTravelStats
            .AsNoTracking()
            .Where(x => x.RegionId == region.Id
                && x.DayType == dayType
                && x.BucketStartHour == bucketStart
                && x.BucketEndHour == bucketEnd
                && x.DistanceBandKmMin == bandMin
                && x.DistanceBandKmMax == bandMax);

        if (_settings.UseLearnedOnlyIfApproved)
        {
            learnedQuery = learnedQuery.Where(x => x.Status == LearnedTravelStatStatus.Approved);
        }

        if (_settings.LearnedSampleThreshold > 0)
        {
            learnedQuery = learnedQuery.Where(x => x.TotalSampleCount >= _settings.LearnedSampleThreshold);
        }

        if (_settings.StaleAfterDays > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-_settings.StaleAfterDays);
            learnedQuery = learnedQuery.Where(x => x.LastSampleAtUtc.HasValue && x.LastSampleAtUtc.Value >= cutoff);
        }

        var learned = await learnedQuery.FirstOrDefaultAsync(cancellationToken);

        if (learned?.AvgMinutesPerKm != null)
        {
            return (double)(learned.AvgMinutesPerKm.Value * (decimal)distanceKm);
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
        int driverId,
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
                TotalSampleCount = 0,
                SuspiciousSampleCount = 0,
                AvgMinutesPerKm = null,
                AvgStopServiceMinutes = stopServiceMinutes.HasValue ? (decimal?)stopServiceMinutes.Value : null,
                Status = LearnedTravelStatStatus.Draft
            };
            _dbContext.LearnedTravelStats.Add(stat);
        }

        var nowUtc = DateTime.UtcNow;
        var minutesPerKm = distanceKm > 0 ? travelMinutes / distanceKm : 0;
        var sampleCount = stat.SampleCount;
        var newSampleCount = sampleCount + 1;

        var existingAvg = stat.AvgMinutesPerKm.HasValue ? (double)stat.AvgMinutesPerKm.Value : 0;
        stat.AvgMinutesPerKm = (decimal)((existingAvg * sampleCount + minutesPerKm) / newSampleCount);
        stat.SampleCount = newSampleCount;
        stat.TotalSampleCount = newSampleCount;
        stat.LastSampleAtUtc = nowUtc;

        if (!stat.MinMinutesPerKm.HasValue || minutesPerKm < (double)stat.MinMinutesPerKm.Value)
        {
            stat.MinMinutesPerKm = (decimal)minutesPerKm;
        }

        if (!stat.MaxMinutesPerKm.HasValue || minutesPerKm > (double)stat.MaxMinutesPerKm.Value)
        {
            stat.MaxMinutesPerKm = (decimal)minutesPerKm;
        }

        if (IsSuspiciousSample(travelMinutes, minutesPerKm, bandMin, bandMax))
        {
            stat.SuspiciousSampleCount += 1;
        }

        if (stopServiceMinutes.HasValue)
        {
            var existingService = stat.AvgStopServiceMinutes.HasValue
                ? (double)stat.AvgStopServiceMinutes.Value
                : stopServiceMinutes.Value;
            var averaged = (existingService * sampleCount + stopServiceMinutes.Value) / newSampleCount;
            stat.AvgStopServiceMinutes = (decimal)averaged;
        }

        if (driverId > 0)
        {
            LearnedTravelStatContributor? contributor = null;
            if (stat.Id != 0)
            {
                contributor = await _dbContext.LearnedTravelStatContributors
                    .FirstOrDefaultAsync(x => x.LearnedTravelStatsId == stat.Id && x.DriverId == driverId, cancellationToken);
            }

            if (contributor == null)
            {
                contributor = new LearnedTravelStatContributor
                {
                    LearnedTravelStats = stat,
                    DriverId = driverId,
                    SampleCount = 0,
                    LastContributionUtc = nowUtc
                };
                _dbContext.LearnedTravelStatContributors.Add(contributor);
            }

            contributor.SampleCount += 1;
            contributor.LastContributionUtc = nowUtc;
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

    private static bool IsSuspiciousSample(double travelMinutes, double minutesPerKm, decimal bandMin, decimal bandMax)
    {
        if (bandMin == 0 && bandMax == 5 && travelMinutes > 90)
        {
            return true;
        }

        if (bandMin == 30 && bandMax == 60 && travelMinutes < 10)
        {
            return true;
        }

        return minutesPerKm < 0.3 || minutesPerKm > 6.0;
    }
}
