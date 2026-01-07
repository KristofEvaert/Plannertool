using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;
using TransportPlanner.Infrastructure.Services;
using RouteEntity = TransportPlanner.Domain.Entities.Route;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/routeStops")]
[Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.Driver}")]
public class RouteStopsController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;
    private readonly ITravelTimeModelService _travelTimeModel;
    private readonly ILogger<RouteStopsController> _logger;

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue("uid"), out var id) ? id : null;

    private bool IsDriver => User.IsInRole(AppRoles.Driver);
    private bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    private int? CurrentOwnerId => int.TryParse(User.FindFirstValue("ownerId"), out var id) ? id : null;
    private bool CanAccessOwner(int ownerId) => IsSuperAdmin || (CurrentOwnerId.HasValue && CurrentOwnerId.Value == ownerId);

    public RouteStopsController(
        TransportPlannerDbContext dbContext,
        ITravelTimeModelService travelTimeModel,
        ILogger<RouteStopsController> logger)
    {
        _dbContext = dbContext;
        _travelTimeModel = travelTimeModel;
        _logger = logger;
    }

    [HttpPost("{routeStopId:int}/arrive")]
    [ProducesResponseType(typeof(RouteStopDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RouteStopDto>> Arrive(
        [FromRoute] int routeStopId,
        [FromBody] RouteStopArriveRequest request,
        CancellationToken cancellationToken = default)
    {
        var stop = await LoadStopAsync(routeStopId, cancellationToken);
        if (stop == null)
        {
            return NotFound();
        }

        if (!await CanAccessStopAsync(stop, cancellationToken))
        {
            return Forbid();
        }

        var nowUtc = DateTime.UtcNow;
        var arrivedAt = request.ArrivedAtUtc ?? nowUtc;

        stop.ArrivedAt = arrivedAt;
        stop.ActualArrivalUtc = arrivedAt;
        if (stop.Status != RouteStopStatus.Completed)
        {
            stop.Status = RouteStopStatus.Arrived;
        }

        if (stop.ProofStatus == RouteStopProofStatus.None)
        {
            stop.ProofStatus = RouteStopProofStatus.InProgress;
        }

        MarkRouteStarted(stop.Route, nowUtc);

        stop.LastUpdatedByUserId = CurrentUserId;
        stop.LastUpdatedUtc = nowUtc;

        _dbContext.RouteStopEvents.Add(new RouteStopEvent
        {
            RouteStopId = stop.Id,
            EventType = RouteStopEventType.Arrive,
            EventUtc = nowUtc,
            PayloadJson = JsonSerializer.Serialize(new { arrivedAtUtc = arrivedAt })
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToStopDto(stop));
    }

    [HttpPost("{routeStopId:int}/depart")]
    [ProducesResponseType(typeof(RouteStopDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RouteStopDto>> Depart(
        [FromRoute] int routeStopId,
        [FromBody] RouteStopDepartRequest request,
        CancellationToken cancellationToken = default)
    {
        var stop = await LoadStopAsync(routeStopId, cancellationToken);
        if (stop == null)
        {
            return NotFound();
        }

        if (!await CanAccessStopAsync(stop, cancellationToken))
        {
            return Forbid();
        }

        var nowUtc = DateTime.UtcNow;
        var departedAt = request.DepartedAtUtc ?? nowUtc;

        stop.CompletedAt = departedAt;
        stop.ActualDepartureUtc = departedAt;
        stop.Status = RouteStopStatus.Completed;
        stop.ProofStatus = RouteStopProofStatus.Completed;

        if (!stop.ArrivedAt.HasValue)
        {
            stop.ArrivedAt = departedAt;
            stop.ActualArrivalUtc ??= departedAt;
        }

        if (stop.ArrivedAt.HasValue && !stop.ActualServiceMinutes.HasValue)
        {
            var minutes = (int)Math.Max(0, Math.Round((departedAt - stop.ArrivedAt.Value).TotalMinutes));
            stop.ActualServiceMinutes = minutes;
        }

        if (stop.ServiceLocation != null)
        {
            stop.ServiceLocation.Status = ServiceLocationStatus.Done;
            stop.ServiceLocation.UpdatedAtUtc = nowUtc;
        }

        MarkRouteStarted(stop.Route, nowUtc);
        UpdateRouteCompletion(stop.Route, nowUtc);

        stop.LastUpdatedByUserId = CurrentUserId;
        stop.LastUpdatedUtc = nowUtc;

        _dbContext.RouteStopEvents.Add(new RouteStopEvent
        {
            RouteStopId = stop.Id,
            EventType = RouteStopEventType.Depart,
            EventUtc = nowUtc,
            PayloadJson = JsonSerializer.Serialize(new { departedAtUtc = departedAt })
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await TryUpdateLearnedStatsAsync(stop, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update learned travel stats for RouteStopId={RouteStopId}", stop.Id);
        }

        return Ok(ToStopDto(stop));
    }

    private async Task<RouteStop?> LoadStopAsync(int routeStopId, CancellationToken cancellationToken)
    {
        return await _dbContext.RouteStops
            .Include(s => s.Route)
                .ThenInclude(r => r.Driver)
            .Include(s => s.Route)
                .ThenInclude(r => r.Stops)
            .Include(s => s.ServiceLocation)
            .FirstOrDefaultAsync(s => s.Id == routeStopId, cancellationToken);
    }

    private async Task<bool> CanAccessStopAsync(RouteStop stop, CancellationToken cancellationToken)
    {
        if (!IsDriver && !CanAccessOwner(stop.Route.OwnerId))
        {
            return false;
        }

        if (IsDriver)
        {
            var currentDriver = await _dbContext.Drivers
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.UserId == CurrentUserId, cancellationToken);
            if (currentDriver == null || stop.Route.DriverId != currentDriver.Id)
            {
                return false;
            }

            if (stop.Route.Date.Date != DateTime.UtcNow.Date)
            {
                return false;
            }
        }

        return true;
    }

    private static void MarkRouteStarted(RouteEntity route, DateTime nowUtc)
    {
        if (route.Status != RouteStatus.Started && route.Status != RouteStatus.Completed)
        {
            route.Status = RouteStatus.Started;
            route.StartedAt ??= nowUtc;
        }
    }

    private static void UpdateRouteCompletion(RouteEntity route, DateTime nowUtc)
    {
        if (route.Status == RouteStatus.Completed)
        {
            return;
        }

        var allDone = route.Stops.All(s =>
            s.Status == RouteStopStatus.Completed || s.Status == RouteStopStatus.NotVisited);
        if (allDone)
        {
            route.Status = RouteStatus.Completed;
            route.CompletedAt ??= nowUtc;
        }
    }

    private async Task TryUpdateLearnedStatsAsync(RouteStop stop, CancellationToken cancellationToken)
    {
        var arrivalUtc = stop.ActualArrivalUtc ?? stop.ArrivedAt;
        if (!arrivalUtc.HasValue)
        {
            return;
        }

        var previousStop = await _dbContext.RouteStops
            .AsNoTracking()
            .Where(s => s.RouteId == stop.RouteId && s.Sequence < stop.Sequence)
            .OrderByDescending(s => s.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

        if (previousStop == null)
        {
            return;
        }

        var departureUtc = previousStop.ActualDepartureUtc ?? previousStop.CompletedAt;
        if (!departureUtc.HasValue)
        {
            return;
        }

        var travelMinutes = (arrivalUtc.Value - departureUtc.Value).TotalMinutes;
        if (travelMinutes <= 0)
        {
            return;
        }

        var distanceKm = stop.TravelKmFromPrev > 0
            ? (double)stop.TravelKmFromPrev
            : HaversineKm(previousStop.Latitude, previousStop.Longitude, stop.Latitude, stop.Longitude);

        var departureMinute = (int)Math.Round((departureUtc.Value - stop.Route.Date.Date).TotalMinutes);
        departureMinute = Math.Clamp(departureMinute, 0, 24 * 60 - 1);

        await _travelTimeModel.UpdateLearnedStatsAsync(
            stop.Route.Date.Date,
            departureMinute,
            distanceKm,
            travelMinutes,
            stop.ActualServiceMinutes,
            stop.Route.DriverId,
            previousStop.Latitude,
            previousStop.Longitude,
            stop.Latitude,
            stop.Longitude,
            cancellationToken);
    }

    private static RouteStopDto ToStopDto(RouteStop stop)
    {
        return new RouteStopDto
        {
            Id = stop.Id,
            Sequence = stop.Sequence,
            ServiceLocationId = stop.ServiceLocationId,
            ServiceLocationToolId = stop.ServiceLocation?.ToolId,
            Name = stop.ServiceLocation?.Name ?? $"Stop {stop.Sequence}",
            Latitude = stop.Latitude,
            Longitude = stop.Longitude,
            ServiceMinutes = stop.ServiceMinutes,
            ActualServiceMinutes = stop.ActualServiceMinutes,
            ActualArrivalUtc = stop.ActualArrivalUtc,
            ActualDepartureUtc = stop.ActualDepartureUtc,
            TravelKmFromPrev = stop.TravelKmFromPrev,
            TravelMinutesFromPrev = stop.TravelMinutesFromPrev,
            Status = stop.Status.ToString(),
            ArrivedAtUtc = stop.ArrivedAt,
            CompletedAtUtc = stop.CompletedAt,
            Note = stop.Note,
            DriverNote = stop.DriverNote,
            IssueCode = stop.IssueCode,
            FollowUpRequired = stop.FollowUpRequired,
            ProofStatus = stop.ProofStatus.ToString(),
            LastUpdatedByUserId = stop.LastUpdatedByUserId,
            LastUpdatedUtc = stop.LastUpdatedUtc,
            DriverInstruction = stop.ServiceLocation?.DriverInstruction
        };
    }

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371;
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRad(double value) => (Math.PI / 180) * value;
}
