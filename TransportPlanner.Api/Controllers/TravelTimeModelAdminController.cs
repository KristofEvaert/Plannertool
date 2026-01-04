using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Security.Claims;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;
using TransportPlanner.Infrastructure.Options;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/admin/travelTimeModel")]
[Authorize(Roles = AppRoles.SuperAdmin)]
public class TravelTimeModelAdminController : ControllerBase
{
    private const decimal FallbackMinutesPerKm = 60m / 50m;
    private readonly TransportPlannerDbContext _dbContext;
    private readonly TravelTimeModelQualitySettings _settings;

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue("uid"), out var id) ? id : null;

    public TravelTimeModelAdminController(
        TransportPlannerDbContext dbContext,
        Microsoft.Extensions.Options.IOptions<TravelTimeModelQualitySettings> settings)
    {
        _dbContext = dbContext;
        _settings = settings?.Value ?? new TravelTimeModelQualitySettings();
    }

    [HttpGet("learned")]
    [ProducesResponseType(typeof(List<TravelTimeModelLearnedStatDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<TravelTimeModelLearnedStatDto>>> GetLearned(CancellationToken cancellationToken)
    {
        var stats = await _dbContext.LearnedTravelStats
            .AsNoTracking()
            .Include(s => s.Region)
            .ToListAsync(cancellationToken);

        var profiles = await _dbContext.RegionSpeedProfiles
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var profileLookup = profiles.ToDictionary(p => (p.RegionId, p.DayType, p.BucketStartHour, p.BucketEndHour));

        var contributors = await _dbContext.LearnedTravelStatContributors
            .AsNoTracking()
            .Include(c => c.Driver)
            .ToListAsync(cancellationToken);
        var contributorsByStat = contributors
            .GroupBy(c => c.LearnedTravelStatsId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(x => x.SampleCount)
                    .ThenByDescending(x => x.LastContributionUtc)
                    .Take(5)
                    .Select(x => new TravelTimeModelContributorDto
                    {
                        DriverId = x.DriverId,
                        DriverName = x.Driver?.Name ?? string.Empty,
                        SampleCount = x.SampleCount,
                        LastContributionUtc = x.LastContributionUtc
                    })
                    .ToList());

        var nowUtc = DateTime.UtcNow;
        var useStaleCheck = _settings.StaleAfterDays > 0;
        var staleCutoff = useStaleCheck ? nowUtc.AddDays(-_settings.StaleAfterDays) : DateTime.MinValue;

        var result = stats.Select(stat =>
        {
            var baseline = ResolveBaseline(profileLookup, stat.RegionId, stat.DayType, stat.BucketStartHour, stat.BucketEndHour);
            var deviation = stat.AvgMinutesPerKm.HasValue && baseline > 0
                ? (stat.AvgMinutesPerKm.Value - baseline) / baseline * 100m
                : (decimal?)null;
            var isOutOfRange = stat.AvgMinutesPerKm.HasValue
                && (stat.AvgMinutesPerKm.Value < _settings.PlausibleMinutesPerKmMin
                    || stat.AvgMinutesPerKm.Value > _settings.PlausibleMinutesPerKmMax);
            var isStale = useStaleCheck && (!stat.LastSampleAtUtc.HasValue || stat.LastSampleAtUtc.Value < staleCutoff);
            var isLowSample = _settings.LearnedSampleThreshold > 0
                && stat.TotalSampleCount < _settings.LearnedSampleThreshold;
            var isHighDeviation = _settings.DeviationVsBaselineWarnPercent > 0
                && deviation.HasValue
                && Math.Abs(deviation.Value) >= _settings.DeviationVsBaselineWarnPercent;
            var suspiciousRatio = stat.TotalSampleCount > 0
                ? (decimal)stat.SuspiciousSampleCount / stat.TotalSampleCount
                : 0m;

            contributorsByStat.TryGetValue(stat.Id, out var contributorList);

            return new TravelTimeModelLearnedStatDto
            {
                Id = stat.Id,
                RegionId = stat.RegionId,
                RegionName = stat.Region?.Name ?? string.Empty,
                DayType = stat.DayType.ToString(),
                BucketStartHour = stat.BucketStartHour,
                BucketEndHour = stat.BucketEndHour,
                DistanceBandKmMin = stat.DistanceBandKmMin,
                DistanceBandKmMax = stat.DistanceBandKmMax,
                Status = stat.Status.ToString(),
                TotalSampleCount = stat.TotalSampleCount,
                AvgMinutesPerKm = stat.AvgMinutesPerKm,
                MinMinutesPerKm = stat.MinMinutesPerKm,
                MaxMinutesPerKm = stat.MaxMinutesPerKm,
                LastSampleAtUtc = stat.LastSampleAtUtc,
                BaselineMinutesPerKm = baseline,
                ExpectedRangeMin = _settings.PlausibleMinutesPerKmMin,
                ExpectedRangeMax = _settings.PlausibleMinutesPerKmMax,
                DeviationPercent = deviation,
                IsOutOfRange = isOutOfRange,
                IsStale = isStale,
                IsLowSample = isLowSample,
                IsHighDeviation = isHighDeviation,
                SuspiciousRatio = suspiciousRatio,
                Contributors = contributorList ?? new List<TravelTimeModelContributorDto>()
            };
        }).ToList();

        return Ok(result);
    }

    [HttpPatch("learned/{id:int}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        [FromRoute] int id,
        [FromBody] TravelTimeModelStatusUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Status)
            || !Enum.TryParse<LearnedTravelStatStatus>(request.Status, ignoreCase: true, out var status))
        {
            return BadRequest(new { message = "Invalid status value." });
        }

        var stat = await _dbContext.LearnedTravelStats
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (stat == null)
        {
            return NotFound();
        }

        stat.Status = status;
        if (status == LearnedTravelStatStatus.Approved)
        {
            stat.ApprovedByUserId = CurrentUserId;
            stat.ApprovedAtUtc = DateTime.UtcNow;
        }
        else
        {
            stat.ApprovedByUserId = null;
            stat.ApprovedAtUtc = null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("learned/{id:int}/reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reset(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        var stat = await _dbContext.LearnedTravelStats
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (stat == null)
        {
            return NotFound();
        }

        stat.AvgMinutesPerKm = null;
        stat.AvgStopServiceMinutes = null;
        stat.MinMinutesPerKm = null;
        stat.MaxMinutesPerKm = null;
        stat.SampleCount = 0;
        stat.TotalSampleCount = 0;
        stat.SuspiciousSampleCount = 0;
        stat.LastSampleAtUtc = null;
        stat.Status = LearnedTravelStatStatus.Draft;
        stat.ApprovedByUserId = null;
        stat.ApprovedAtUtc = null;

        var contributors = _dbContext.LearnedTravelStatContributors
            .Where(x => x.LearnedTravelStatsId == id);
        _dbContext.LearnedTravelStatContributors.RemoveRange(contributors);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static decimal ResolveBaseline(
        Dictionary<(int RegionId, DayType DayType, int BucketStart, int BucketEnd), RegionSpeedProfile> lookup,
        int regionId,
        DayType dayType,
        int bucketStart,
        int bucketEnd)
    {
        if (lookup.TryGetValue((regionId, dayType, bucketStart, bucketEnd), out var profile))
        {
            return profile.AvgMinutesPerKm;
        }

        if (regionId != 99 && lookup.TryGetValue((99, dayType, bucketStart, bucketEnd), out var fallback))
        {
            return fallback.AvgMinutesPerKm;
        }

        return FallbackMinutesPerKm;
    }
}
