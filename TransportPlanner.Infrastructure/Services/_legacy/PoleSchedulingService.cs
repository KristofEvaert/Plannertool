using Microsoft.EntityFrameworkCore;
using TransportPlanner.Application.Interfaces;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;

namespace TransportPlanner.Infrastructure.Services;

public class PoleSchedulingService : IPoleSchedulingService
{
    private readonly TransportPlannerDbContext _dbContext;

    public PoleSchedulingService(TransportPlannerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SetFixedDateAsync(int poleId, DateTime fixedDate, CancellationToken cancellationToken = default)
    {
        var pole = await _dbContext.Poles
            .FirstOrDefaultAsync(p => p.Id == poleId, cancellationToken);

        if (pole == null)
        {
            throw new InvalidOperationException($"Pole with id {poleId} not found");
        }

        // Check if Status is Done or Cancelled
        if (pole.Status == PoleStatus.Done || pole.Status == PoleStatus.Cancelled)
        {
            throw new InvalidOperationException($"Cannot modify FixedDate for pole {poleId} with status {pole.Status}");
        }

        // Validate fixedDate is date-only and within today..today+365
        var dateOnly = fixedDate.Date;
        var today = DateTime.Today;
        var maxDate = today.AddDays(365);

        if (dateOnly < today || dateOnly > maxDate)
        {
            throw new ArgumentException($"FixedDate must be between {today:yyyy-MM-dd} and {maxDate:yyyy-MM-dd}", nameof(fixedDate));
        }

        // Set FixedDate (idempotent - setting same date twice is OK)
        pole.FixedDate = dateOnly;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearFixedDateAsync(int poleId, CancellationToken cancellationToken = default)
    {
        var pole = await _dbContext.Poles
            .FirstOrDefaultAsync(p => p.Id == poleId, cancellationToken);

        if (pole == null)
        {
            throw new InvalidOperationException($"Pole with id {poleId} not found");
        }

        // Check if Status is Done or Cancelled
        if (pole.Status == PoleStatus.Done || pole.Status == PoleStatus.Cancelled)
        {
            throw new InvalidOperationException($"Cannot modify FixedDate for pole {poleId} with status {pole.Status}");
        }

        // Clear FixedDate
        pole.FixedDate = null;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

