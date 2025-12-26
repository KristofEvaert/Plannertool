using Microsoft.EntityFrameworkCore;
using TransportPlanner.Application.Interfaces;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;

namespace TransportPlanner.Infrastructure.Services;

public class PlanLockService : IPlanLockService
{
    private readonly TransportPlannerDbContext _dbContext;

    public PlanLockService(TransportPlannerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task LockDayAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var dateOnly = date.Date;

        var planDay = await _dbContext.PlanDays
            .FirstOrDefaultAsync(pd => pd.Date.Date == dateOnly, cancellationToken);

        if (planDay == null)
        {
            planDay = new PlanDay
            {
                Date = dateOnly,
                IsLocked = true
            };
            _dbContext.PlanDays.Add(planDay);
        }
        else
        {
            planDay.IsLocked = true;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UnlockDayAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var dateOnly = date.Date;

        var planDay = await _dbContext.PlanDays
            .FirstOrDefaultAsync(pd => pd.Date.Date == dateOnly, cancellationToken);

        if (planDay == null)
        {
            planDay = new PlanDay
            {
                Date = dateOnly,
                IsLocked = false
            };
            _dbContext.PlanDays.Add(planDay);
        }
        else
        {
            planDay.IsLocked = false;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

