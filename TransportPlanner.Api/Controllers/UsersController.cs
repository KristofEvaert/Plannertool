using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;
using TransportPlanner.Infrastructure.Services;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize] // default: any authenticated
public class UsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly TransportPlannerDbContext _dbContext;
    private readonly IGeocodingService _geocodingService;

    public UsersController(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        TransportPlannerDbContext dbContext,
        IGeocodingService geocodingService)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _dbContext = dbContext;
        _geocodingService = geocodingService;
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> GetMe(CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var roles = await _userManager.GetRolesAsync(user);
        var driver = await _dbContext.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.UserId == user.Id, cancellationToken);

        var dto = new UserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            Roles = roles.ToList(),
            DriverToolId = driver?.ToolId,
            DriverOwnerId = driver?.OwnerId,
            OwnerId = user.OwnerId,
            DriverStartAddress = driver?.StartAddress,
            DriverStartLatitude = driver?.StartLatitude == 0 ? null : driver?.StartLatitude,
            DriverStartLongitude = driver?.StartLongitude == 0 ? null : driver?.StartLongitude
        };

        return Ok(dto);
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<UserDto>), StatusCodes.Status200OK)]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<List<UserDto>>> GetUsers(CancellationToken cancellationToken)
    {
        var users = await _userManager.Users.ToListAsync(cancellationToken);
        var result = new List<UserDto>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var driver = await _dbContext.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.UserId == user.Id, cancellationToken);
            result.Add(new UserDto
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                DisplayName = user.DisplayName,
                Roles = roles.ToList(),
                DriverToolId = driver?.ToolId,
                DriverOwnerId = driver?.OwnerId,
                OwnerId = user.OwnerId,
                DriverStartAddress = driver?.StartAddress,
                DriverStartLatitude = driver?.StartLatitude == 0 ? null : driver?.StartLatitude,
                DriverStartLongitude = driver?.StartLongitude == 0 ? null : driver?.StartLongitude
            });
        }

        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] CreateUserRequest request)
    {
        if (await _userManager.FindByEmailAsync(request.Email) != null)
        {
            return Conflict(new { message = "User already exists" });
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            UserName = request.Email,
            DisplayName = BuildDisplayName(request.Email, request.DisplayName),
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new { message = string.Join("; ", result.Errors.Select(e => e.Description)) });
        }

        var dto = new UserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            Roles = new()
        };

        return CreatedAtAction(nameof(GetUsers), new { id = user.Id }, dto);
    }

    [HttpPost("assign-roles")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<UserDto>> AssignRoles([FromBody] AssignRolesRequest request, CancellationToken cancellationToken = default)
    {
        var targetUser = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (targetUser == null)
        {
            return NotFound(new { message = "User not found" });
        }

        var currentUser = await _userManager.GetUserAsync(User);
        var currentRoles = currentUser != null ? await _userManager.GetRolesAsync(currentUser) : Array.Empty<string>();
        var isCurrentSuperAdmin = currentRoles.Contains(AppRoles.SuperAdmin);
        var isCurrentAdmin = currentRoles.Contains(AppRoles.Admin);

        var ownerIdForStaff = request.OwnerIdForStaff;
        var requiresOwnerForStaff = request.Roles.Any(r => r == AppRoles.Admin || r == AppRoles.Planner);
        if (requiresOwnerForStaff && (!ownerIdForStaff.HasValue || ownerIdForStaff.Value <= 0))
        {
            return BadRequest(new { message = "OwnerIdForStaff is required when assigning Admin or Planner role" });
        }
        if (ownerIdForStaff.HasValue)
        {
            var ownerExists = await _dbContext.ServiceLocationOwners.AnyAsync(o => o.Id == ownerIdForStaff.Value && o.IsActive, cancellationToken);
            if (!ownerExists)
            {
                return BadRequest(new { message = $"OwnerId {ownerIdForStaff.Value} is invalid or inactive." });
            }
        }

        // enforce role assignment constraints
        foreach (var role in request.Roles)
        {
            if (role == AppRoles.SuperAdmin && !isCurrentSuperAdmin)
            {
                return Forbid();
            }
            if (role == AppRoles.Admin && !isCurrentSuperAdmin)
            {
                return Forbid();
            }
            if (role == AppRoles.Driver && !request.OwnerIdForDriver.HasValue)
            {
                return BadRequest(new { message = "OwnerIdForDriver is required when assigning Driver role" });
            }
        }

        var allowedRoles = request.Roles.Where(r =>
            r == AppRoles.Driver ||
            r == AppRoles.Planner ||
            (r == AppRoles.Admin && isCurrentSuperAdmin) ||
            (r == AppRoles.SuperAdmin && isCurrentSuperAdmin)).ToList();

        Driver? existingDriver = null;
        if (allowedRoles.Contains(AppRoles.Driver))
        {
            existingDriver = await _dbContext.Drivers
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.UserId == targetUser.Id, cancellationToken);

            if (request.DriverStartLatitude == 0)
            {
                request.DriverStartLatitude = null;
            }
            if (request.DriverStartLongitude == 0)
            {
                request.DriverStartLongitude = null;
            }
            var inputHasAddress = !string.IsNullOrWhiteSpace(request.DriverStartAddress);
            var inputHasLatitude = request.DriverStartLatitude.HasValue;
            var inputHasLongitude = request.DriverStartLongitude.HasValue;
            if (inputHasLatitude != inputHasLongitude)
            {
                return BadRequest(new { message = "Provide both DriverStartLatitude and DriverStartLongitude, or leave both empty" });
            }

            var existingHasAddress = existingDriver != null && !string.IsNullOrWhiteSpace(existingDriver.StartAddress);
            var existingHasCoordinates = existingDriver != null
                && existingDriver.StartLatitude.HasValue
                && existingDriver.StartLongitude.HasValue
                && !(existingDriver.StartLatitude.Value == 0 && existingDriver.StartLongitude.Value == 0);
            if (!inputHasAddress && !inputHasLatitude && !existingHasAddress && !existingHasCoordinates)
            {
                return BadRequest(new { message = "DriverStartAddress or DriverStartLatitude/DriverStartLongitude is required when assigning Driver role" });
            }
        }

        // remove roles not allowed or not in requested set (except SuperAdmin protection)
        var existingRoles = await _userManager.GetRolesAsync(targetUser);
        var rolesToRemove = existingRoles.Where(r =>
            r != AppRoles.SuperAdmin && // do not remove superadmin silently
            !allowedRoles.Contains(r)).ToList();
        if (rolesToRemove.Any())
        {
            await _userManager.RemoveFromRolesAsync(targetUser, rolesToRemove);
        }

        // add roles
        foreach (var role in allowedRoles)
        {
            if (!await _roleManager.RoleExistsAsync(role))
            {
                await _roleManager.CreateAsync(new IdentityRole<Guid>(role));
            }

            if (!await _userManager.IsInRoleAsync(targetUser, role))
            {
                await _userManager.AddToRoleAsync(targetUser, role);
            }

            if (role == AppRoles.Driver)
            {
                var error = await EnsureDriverForUser(
                    targetUser,
                    request.OwnerIdForDriver!.Value,
                    request.DriverStartAddress,
                    request.DriverStartLatitude,
                    request.DriverStartLongitude,
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    return BadRequest(new { message = error });
                }
            }
        }

        // Update display name if provided, otherwise ensure a sensible default
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            targetUser.DisplayName = request.DisplayName.Trim();
        }
        else if (string.IsNullOrWhiteSpace(targetUser.DisplayName))
        {
            targetUser.DisplayName = BuildDisplayName(targetUser.Email ?? string.Empty);
        }

        // Set staff owner when applicable (super admins remain global)
        if (allowedRoles.Contains(AppRoles.SuperAdmin))
        {
            targetUser.OwnerId = null;
        }
        else if (allowedRoles.Any(r => r == AppRoles.Admin || r == AppRoles.Planner))
        {
            targetUser.OwnerId = ownerIdForStaff;
        }
        else if (targetUser.OwnerId.HasValue)
        {
            targetUser.OwnerId = null;
        }

        await _userManager.UpdateAsync(targetUser);

        var updatedRoles = await _userManager.GetRolesAsync(targetUser);
        var updatedDriver = await _dbContext.Drivers
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == targetUser.Id, cancellationToken);
        var dto = new UserDto
        {
            Id = targetUser.Id,
            Email = targetUser.Email ?? string.Empty,
            DisplayName = targetUser.DisplayName,
            Roles = updatedRoles.ToList(),
            OwnerId = targetUser.OwnerId,
            DriverToolId = updatedDriver?.ToolId,
            DriverOwnerId = updatedDriver?.OwnerId,
            DriverStartAddress = updatedDriver?.StartAddress,
            DriverStartLatitude = updatedDriver?.StartLatitude == 0 ? null : updatedDriver?.StartLatitude,
            DriverStartLongitude = updatedDriver?.StartLongitude == 0 ? null : updatedDriver?.StartLongitude
        };

        return Ok(dto);
    }

    private async Task<string?> EnsureDriverForUser(
        ApplicationUser user,
        int ownerId,
        string? startAddress,
        double? startLatitude,
        double? startLongitude,
        CancellationToken cancellationToken)
    {
        var ownerExists = await _dbContext.ServiceLocationOwners.AnyAsync(o => o.Id == ownerId && o.IsActive, cancellationToken);
        if (!ownerExists)
        {
            return $"OwnerId {ownerId} is invalid or inactive.";
        }

        var driver = await _dbContext.Drivers.FirstOrDefaultAsync(d => d.UserId == user.Id, cancellationToken);

        var address = string.IsNullOrWhiteSpace(startAddress) ? null : startAddress.Trim();
        var latitude = startLatitude;
        var longitude = startLongitude;
        if (latitude == 0)
        {
            latitude = null;
        }
        if (longitude == 0)
        {
            longitude = null;
        }

        if (driver != null)
        {
            var hasIncomingCoordinates = latitude.HasValue && longitude.HasValue;
            if (string.IsNullOrWhiteSpace(address) && !hasIncomingCoordinates)
            {
                address = string.IsNullOrWhiteSpace(driver.StartAddress) ? null : driver.StartAddress;
            }

            var hasExistingCoordinates = driver.StartLatitude.HasValue
                && driver.StartLongitude.HasValue
                && !(driver.StartLatitude.Value == 0 && driver.StartLongitude.Value == 0);
            // Alleen bestaande coords overnemen als er GEEN nieuw adres is meegegeven
            if (string.IsNullOrWhiteSpace(address) && !hasIncomingCoordinates && hasExistingCoordinates)
            {
                latitude = driver.StartLatitude;
                longitude = driver.StartLongitude;
            }

        }

        var hasAddress = !string.IsNullOrWhiteSpace(address);
        var hasLatitude = latitude.HasValue;
        var hasLongitude = longitude.HasValue;
        if (hasLatitude != hasLongitude)
        {
            return "Provide both DriverStartLatitude and DriverStartLongitude, or leave both empty.";
        }

        if (!hasAddress && !hasLatitude)
        {
            return "DriverStartAddress or DriverStartLatitude/DriverStartLongitude is required when assigning Driver role.";
        }

        if (!hasLatitude && hasAddress)
        {
            var geocode = await _geocodingService.GeocodeAddressAsync(address!, cancellationToken);
            if (geocode == null)
            {
                return "Unable to resolve DriverStartLatitude/DriverStartLongitude from DriverStartAddress.";
            }
            latitude = geocode.Latitude;
            longitude = geocode.Longitude;
            hasLatitude = true;
            hasLongitude = true;
        }
        else if (!hasAddress && hasLatitude)
        {
            var reverseAddress = await _geocodingService.ReverseGeocodeAsync(latitude!.Value, longitude!.Value, cancellationToken);
            if (string.IsNullOrWhiteSpace(reverseAddress))
            {
                return "Unable to resolve DriverStartAddress from DriverStartLatitude/DriverStartLongitude.";
            }
            address = reverseAddress;
            hasAddress = true;
        }

        if (!hasLatitude || !hasLongitude)
        {
            return "DriverStartLatitude and DriverStartLongitude are required after geocoding.";
        }

        if (latitude < -90 || latitude > 90)
        {
            return "DriverStartLatitude must be between -90 and 90.";
        }

        if (longitude < -180 || longitude > 180)
        {
            return "DriverStartLongitude must be between -180 and 180.";
        }

        if (driver != null)
        {
            driver.OwnerId = ownerId;
            driver.StartAddress = address;
            driver.StartLatitude = latitude.Value;
            driver.StartLongitude = longitude.Value;
            driver.UpdatedAtUtc = DateTime.UtcNow;
            _dbContext.Drivers.Update(driver);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        // Generate ERP id
        var nextErpId = await _dbContext.Drivers.MaxAsync(d => (int?)d.ErpId, cancellationToken) ?? 100000;
        nextErpId += 1;

        var now = DateTime.UtcNow;
        var newDriver = new Driver
        {
            ToolId = Guid.NewGuid(),
            UserId = user.Id,
            ErpId = nextErpId,
            Name = user.DisplayName ?? user.Email ?? "Driver",
            StartAddress = address,
            StartLatitude = latitude!.Value,
            StartLongitude = longitude!.Value,
            DefaultServiceMinutes = 20,
            MaxWorkMinutesPerDay = 480,
            OwnerId = ownerId,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _dbContext.Drivers.Add(newDriver);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    private static string BuildDisplayName(string email, string? requestedDisplayName = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedDisplayName))
        {
            return requestedDisplayName.Trim();
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : email;
    }
}
