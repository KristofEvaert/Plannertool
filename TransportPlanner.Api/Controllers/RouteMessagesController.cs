using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TransportPlanner.Api.Hubs;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/route-messages")]
[Authorize]
public class RouteMessagesController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;
    private readonly IHubContext<RouteMessagesHub> _hubContext;

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue("uid"), out var id) ? id : null;

    private bool IsDriver => User.IsInRole(AppRoles.Driver);
    private bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    private int? CurrentOwnerId => int.TryParse(User.FindFirstValue("ownerId"), out var id) ? id : null;
    private bool CanAccessOwner(int ownerId) => IsSuperAdmin || (CurrentOwnerId.HasValue && CurrentOwnerId.Value == ownerId);

    public RouteMessagesController(
        TransportPlannerDbContext dbContext,
        IHubContext<RouteMessagesHub> hubContext)
    {
        _dbContext = dbContext;
        _hubContext = hubContext;
    }

    [HttpGet]
    [Authorize(Policy = "RequireStaff")]
    [ProducesResponseType(typeof(List<RouteMessageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<RouteMessageDto>>> GetMessages(
        [FromQuery] int? ownerId,
        [FromQuery] string? status,
        [FromQuery] int? routeId,
        CancellationToken cancellationToken = default)
    {
        if (!IsSuperAdmin)
        {
            ownerId = CurrentOwnerId;
        }

        if (!ownerId.HasValue || ownerId.Value <= 0)
        {
            return BadRequest(new { message = "OwnerId is required." });
        }

        if (!CanAccessOwner(ownerId.Value))
        {
            return Forbid();
        }

        var query = from message in _dbContext.RouteMessages.AsNoTracking()
            join route in _dbContext.Routes.AsNoTracking() on message.RouteId equals route.Id
            join driver in _dbContext.Drivers.AsNoTracking() on message.DriverId equals driver.Id
            where route.OwnerId == ownerId.Value
            select new { message, driver };

        if (routeId.HasValue)
        {
            query = query.Where(x => x.message.RouteId == routeId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<RouteMessageStatus>(status, ignoreCase: true, out var statusEnum))
        {
            query = query.Where(x => x.message.Status == statusEnum);
        }

        var items = await query
            .OrderByDescending(x => x.message.CreatedUtc)
            .Select(x => new RouteMessageDto
            {
                Id = x.message.Id,
                RouteId = x.message.RouteId,
                RouteStopId = x.message.RouteStopId,
                DriverId = x.message.DriverId,
                DriverName = x.driver.Name,
                PlannerId = x.message.PlannerId,
                MessageText = x.message.MessageText,
                CreatedUtc = x.message.CreatedUtc,
                Status = x.message.Status.ToString(),
                Category = x.message.Category.ToString()
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.Driver}")]
    [ProducesResponseType(typeof(RouteMessageDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RouteMessageDto>> CreateMessage(
        [FromBody] CreateRouteMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.MessageText))
        {
            return BadRequest(new { message = "MessageText is required." });
        }

        var route = await _dbContext.Routes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.RouteId, cancellationToken);

        if (route == null)
        {
            return BadRequest(new { message = "Route not found." });
        }

        if (!IsDriver && !CanAccessOwner(route.OwnerId))
        {
            return Forbid();
        }

        Driver? driver = null;
        if (IsDriver)
        {
            driver = await _dbContext.Drivers
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.UserId == CurrentUserId, cancellationToken);
            if (driver == null || route.DriverId != driver.Id)
            {
                return Forbid();
            }
        }
        else
        {
            driver = await _dbContext.Drivers
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == route.DriverId, cancellationToken);
        }

        if (driver == null)
        {
            return BadRequest(new { message = "Driver not found." });
        }

        if (!Enum.TryParse<RouteMessageCategory>(request.Category, ignoreCase: true, out var category))
        {
            category = RouteMessageCategory.Info;
        }

        var message = new RouteMessage
        {
            RouteId = request.RouteId,
            RouteStopId = request.RouteStopId,
            DriverId = driver.Id,
            PlannerId = IsDriver ? null : CurrentUserId,
            MessageText = request.MessageText.Trim(),
            CreatedUtc = DateTime.UtcNow,
            Status = RouteMessageStatus.New,
            Category = category
        };

        _dbContext.RouteMessages.Add(message);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = new RouteMessageDto
        {
            Id = message.Id,
            RouteId = message.RouteId,
            RouteStopId = message.RouteStopId,
            DriverId = message.DriverId,
            DriverName = driver.Name,
            PlannerId = message.PlannerId,
            MessageText = message.MessageText,
            CreatedUtc = message.CreatedUtc,
            Status = message.Status.ToString(),
            Category = message.Category.ToString()
        };

        await _hubContext.Clients.Group($"owner-{route.OwnerId}")
            .SendAsync("routeMessageCreated", dto, cancellationToken);
        await _hubContext.Clients.Group("superadmin")
            .SendAsync("routeMessageCreated", dto, cancellationToken);

        return Ok(dto);
    }

    [HttpPost("{messageId:int}/read")]
    [Authorize(Policy = "RequireStaff")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(
        [FromRoute] int messageId,
        CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.RouteMessages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (message == null)
        {
            return NotFound();
        }

        var route = await _dbContext.Routes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == message.RouteId, cancellationToken);

        if (route == null)
        {
            return NotFound(new { message = "Route not found." });
        }

        if (!CanAccessOwner(route.OwnerId))
        {
            return Forbid();
        }

        message.Status = RouteMessageStatus.Read;
        message.PlannerId = CurrentUserId;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{messageId:int}/resolve")]
    [Authorize(Policy = "RequireStaff")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkResolved(
        [FromRoute] int messageId,
        CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.RouteMessages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (message == null)
        {
            return NotFound();
        }

        var route = await _dbContext.Routes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == message.RouteId, cancellationToken);

        if (route == null)
        {
            return NotFound(new { message = "Route not found." });
        }

        if (!CanAccessOwner(route.OwnerId))
        {
            return Forbid();
        }

        message.Status = RouteMessageStatus.Resolved;
        message.PlannerId = CurrentUserId;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
