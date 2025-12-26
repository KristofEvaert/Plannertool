using Microsoft.EntityFrameworkCore;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Application.Exceptions;
using TransportPlanner.Application.Interfaces;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;

namespace TransportPlanner.Infrastructure.Services;

public class PoleImportService : IPoleImportService
{
    private readonly TransportPlannerDbContext _dbContext;
    private readonly DummyErpPoleSource _erpSource;

    public PoleImportService(TransportPlannerDbContext dbContext)
    {
        _dbContext = dbContext;
        _erpSource = new DummyErpPoleSource();
    }

    public async Task<ImportPolesResultDto> ImportDueWithinAsync(int days, CancellationToken cancellationToken = default)
    {
        var today = DateTime.Today;
        var toDate = today.AddDays(days);
        var fromDueDate = today;
        var toDueDate = toDate;

        // Generate/fetch poles from ERP
        var erpPoles = _erpSource.GeneratePolesForDateRange(fromDueDate, toDueDate);

        var imported = 0;
        var updated = 0;

        foreach (var erpPole in erpPoles)
        {
            var existingPole = await _dbContext.Poles
                .FirstOrDefaultAsync(p => p.Serial == erpPole.Serial, cancellationToken);

            if (existingPole == null)
            {
                // New pole - insert with Status=New
                var newPole = new Pole
                {
                    Serial = erpPole.Serial,
                    Latitude = erpPole.Latitude,
                    Longitude = erpPole.Longitude,
                    DueDate = erpPole.DueDate,
                    FixedDate = erpPole.FixedDate,
                    Status = PoleStatus.New
                };
                _dbContext.Poles.Add(newPole);
                imported++;
            }
            else
            {
                // Existing pole - update only if not Done/Cancelled
                if (existingPole.Status != PoleStatus.Done && existingPole.Status != PoleStatus.Cancelled)
                {
                    existingPole.Latitude = erpPole.Latitude;
                    existingPole.Longitude = erpPole.Longitude;
                    existingPole.DueDate = erpPole.DueDate;
                    existingPole.FixedDate = erpPole.FixedDate;
                    // Do not overwrite Status - keep existing status
                    updated++;
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ImportPolesResultDto
        {
            Imported = imported,
            Updated = updated,
            Total = imported + updated,
            FromDueDate = fromDueDate,
            ToDueDate = toDueDate
        };
    }
}

