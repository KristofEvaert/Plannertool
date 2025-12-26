using Microsoft.EntityFrameworkCore;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Application.Exceptions;
using TransportPlanner.Application.Interfaces;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;

namespace TransportPlanner.Infrastructure.Services;

public class DriverAdminService : IDriverAdminService
{
    private readonly TransportPlannerDbContext _dbContext;

    public DriverAdminService(TransportPlannerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DriverDto> UpdateMaxWorkMinutesAsync(int driverId, int minutes, CancellationToken cancellationToken = default)
    {
        // Validate range
        if (minutes < 60 || minutes > 900)
        {
            throw new ArgumentException("MaxWorkMinutesPerDay must be between 60 and 900", nameof(minutes));
        }

        var driver = await _dbContext.Drivers
            .FirstOrDefaultAsync(d => d.Id == driverId, cancellationToken);

        if (driver == null)
        {
            throw new NotFoundException($"Driver with ID {driverId} not found");
        }

        driver.MaxWorkMinutesPerDay = minutes;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DriverDto
        {
            Id = driver.Id,
            Name = driver.Name,
            StartLatitude = driver.StartLatitude ?? 0,
            StartLongitude = driver.StartLongitude ?? 0,
            MaxWorkMinutesPerDay = driver.MaxWorkMinutesPerDay,
        };
    }
}

