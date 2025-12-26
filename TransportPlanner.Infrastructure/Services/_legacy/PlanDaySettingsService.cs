using Microsoft.EntityFrameworkCore;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Application.Interfaces;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;

namespace TransportPlanner.Infrastructure.Services;

public class PlanDaySettingsService : IPlanDaySettingsService
{
    private readonly TransportPlannerDbContext _dbContext;

    public PlanDaySettingsService(TransportPlannerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PlanDaySettingsDto> GetAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var normalizedDate = date.Date;

        // Ensure PlanDay exists
        var planDayExists = await _dbContext.PlanDays
            .AnyAsync(pd => pd.Date == normalizedDate, cancellationToken);

        if (!planDayExists)
        {
            // Create PlanDay if it doesn't exist
            _dbContext.PlanDays.Add(new PlanDay
            {
                Date = normalizedDate,
                IsLocked = false,
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var settings = await _dbContext.PlanDaySettings
            .FirstOrDefaultAsync(s => s.Date == normalizedDate, cancellationToken);

        if (settings == null)
        {
            return new PlanDaySettingsDto
            {
                Date = normalizedDate,
                ExtraWorkMinutes = 0,
            };
        }

        return new PlanDaySettingsDto
        {
            Date = settings.Date,
            ExtraWorkMinutes = settings.ExtraWorkMinutes,
        };
    }

    public async Task<PlanDaySettingsDto> SetAsync(DateTime date, int extraWorkMinutes, CancellationToken cancellationToken = default)
    {
        // Validate range
        if (extraWorkMinutes < 0 || extraWorkMinutes > 300)
        {
            throw new ArgumentException("ExtraWorkMinutes must be between 0 and 300", nameof(extraWorkMinutes));
        }

        var normalizedDate = date.Date;

        // Ensure PlanDay exists
        var planDayExists = await _dbContext.PlanDays
            .AnyAsync(pd => pd.Date == normalizedDate, cancellationToken);

        if (!planDayExists)
        {
            _dbContext.PlanDays.Add(new PlanDay
            {
                Date = normalizedDate,
                IsLocked = false,
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // Upsert settings
        var settings = await _dbContext.PlanDaySettings
            .FirstOrDefaultAsync(s => s.Date == normalizedDate, cancellationToken);

        if (settings == null)
        {
            settings = new PlanDaySettings
            {
                Date = normalizedDate,
                ExtraWorkMinutes = extraWorkMinutes,
            };
            _dbContext.PlanDaySettings.Add(settings);
        }
        else
        {
            settings.ExtraWorkMinutes = extraWorkMinutes;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new PlanDaySettingsDto
        {
            Date = settings.Date,
            ExtraWorkMinutes = settings.ExtraWorkMinutes,
        };
    }
}

