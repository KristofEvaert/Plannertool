using Microsoft.EntityFrameworkCore;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Application.Exceptions;
using TransportPlanner.Application.Interfaces;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;

namespace TransportPlanner.Infrastructure.Services;

public class RouteExecutionService : IRouteExecutionService
{
    private readonly TransportPlannerDbContext _dbContext;

    public RouteExecutionService(TransportPlannerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<RouteActionResultDto> StartRouteAsync(int routeId, CancellationToken cancellationToken = default)
    {
        var route = await _dbContext.Routes
            .FirstOrDefaultAsync(r => r.Id == routeId, cancellationToken);

        if (route == null)
        {
            throw new NotFoundException("Route", routeId);
        }

        // Check if day is locked
        var planDay = await _dbContext.PlanDays
            .FirstOrDefaultAsync(pd => pd.Date == route.Date.Date, cancellationToken);

        if (planDay?.IsLocked == true)
        {
            throw new ConflictException($"Cannot start route: day {route.Date:yyyy-MM-dd} is locked");
        }

        // Check route constraints
        if (route.IsLocked)
        {
            throw new ConflictException($"Cannot start route {routeId}: route is locked");
        }

        if (route.Status == RouteStatus.Completed)
        {
            throw new ConflictException($"Cannot start route {routeId}: route is already completed");
        }

        // Start route
        route.Status = RouteStatus.Started;
        route.StartedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new RouteActionResultDto
        {
            RouteId = route.Id,
            Status = route.Status.ToString(),
            StartedAt = route.StartedAt,
            CompletedAt = route.CompletedAt
        };
    }

    public async Task<StopActionResultDto> ArriveStopAsync(int routeId, int stopId, CancellationToken cancellationToken = default)
    {
        var route = await _dbContext.Routes
            .FirstOrDefaultAsync(r => r.Id == routeId, cancellationToken);

        if (route == null)
        {
            throw new NotFoundException("Route", routeId);
        }

        if (route.Status != RouteStatus.Started)
        {
            throw new ConflictException($"Route {routeId} must be started before arriving at stops");
        }

        var stop = await _dbContext.RouteStops
            .FirstOrDefaultAsync(rs => rs.Id == stopId && rs.RouteId == routeId, cancellationToken);

        if (stop == null)
        {
            throw new NotFoundException("RouteStop", stopId);
        }

        // Idempotent: if already arrived, keep existing timestamp
        if (stop.Status == RouteStopStatus.Pending)
        {
            stop.Status = RouteStopStatus.Arrived;
            stop.ArrivedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new StopActionResultDto
        {
            RouteId = routeId,
            StopId = stopId,
            Status = stop.Status.ToString(),
            ArrivedAt = stop.ArrivedAt,
            CompletedAt = stop.CompletedAt,
            Note = stop.Note
        };
    }

    public async Task<StopActionResultDto> CompleteStopAsync(int routeId, int stopId, CancellationToken cancellationToken = default)
    {
        var route = await _dbContext.Routes
            .FirstOrDefaultAsync(r => r.Id == routeId, cancellationToken);

        if (route == null)
        {
            throw new NotFoundException("Route", routeId);
        }

        if (route.Status != RouteStatus.Started)
        {
            throw new ConflictException($"Route {routeId} must be started before completing stops");
        }

        var stop = await _dbContext.RouteStops
            .FirstOrDefaultAsync(rs => rs.Id == stopId && rs.RouteId == routeId, cancellationToken);

        if (stop == null)
        {
            throw new NotFoundException("RouteStop", stopId);
        }

        // Complete stop
        stop.Status = RouteStopStatus.Completed;
        stop.CompletedAt = DateTime.UtcNow;

        // Update pole status
        var pole = await _dbContext.Poles.FindAsync(new object[] { stop.PoleId }, cancellationToken);
        if (pole != null)
        {
            pole.Status = PoleStatus.Done;
        }

        // Check if all stops are completed or skipped
        var allStops = await _dbContext.RouteStops
            .Where(rs => rs.RouteId == routeId)
            .ToListAsync(cancellationToken);
        
        var allStopsDone = allStops.All(rs => 
            rs.Status == RouteStopStatus.Completed || rs.Status == RouteStopStatus.Skipped);

        if (allStopsDone)
        {
            route.Status = RouteStatus.Completed;
            route.CompletedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new StopActionResultDto
        {
            RouteId = routeId,
            StopId = stopId,
            Status = stop.Status.ToString(),
            ArrivedAt = stop.ArrivedAt,
            CompletedAt = stop.CompletedAt,
            Note = stop.Note
        };
    }

    public async Task<StopActionResultDto> AddStopNoteAsync(int routeId, int stopId, string note, CancellationToken cancellationToken = default)
    {
        var stop = await _dbContext.RouteStops
            .FirstOrDefaultAsync(rs => rs.Id == stopId && rs.RouteId == routeId, cancellationToken);

        if (stop == null)
        {
            throw new NotFoundException("RouteStop", stopId);
        }

        // Overwrite note (v1)
        stop.Note = note;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new StopActionResultDto
        {
            RouteId = routeId,
            StopId = stopId,
            Status = stop.Status.ToString(),
            ArrivedAt = stop.ArrivedAt,
            CompletedAt = stop.CompletedAt,
            Note = stop.Note
        };
    }
}

