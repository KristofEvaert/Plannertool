using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;

namespace TransportPlanner.Infrastructure.Seeding;

public class DatabaseSeeder
{
    private readonly TransportPlannerDbContext _dbContext;
    private readonly ILogger<DatabaseSeeder> _logger;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;

    public DatabaseSeeder(
        TransportPlannerDbContext dbContext,
        ILogger<DatabaseSeeder> logger,
        RoleManager<IdentityRole<Guid>> roleManager,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _logger = logger;
        _roleManager = roleManager;
        _userManager = userManager;
        _configuration = configuration;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting database seeding...");

        // Check if database is accessible
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                _logger.LogError("Cannot connect to database. Seeding aborted.");
                return;
            }
            _logger.LogInformation("Database connection verified.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking database connection: {Message}", ex.Message);
            throw;
        }

        // Seed roles and initial super admin
        await SeedRolesAsync(cancellationToken);
        await SeedSuperAdminAsync(cancellationToken);

        _logger.LogInformation("Skipping service type, owner, driver, and service location seeding.");

        // Seed travel time model data (regions + speed profiles) if empty
        await SeedTravelTimeModelAsync(cancellationToken);
    }

    private async Task SeedRolesAsync(CancellationToken cancellationToken)
    {
        var roles = new[] { AppRoles.SuperAdmin, AppRoles.Admin, AppRoles.Planner, AppRoles.Driver };

        foreach (var role in roles)
        {
            if (!await _roleManager.RoleExistsAsync(role))
            {
                var result = await _roleManager.CreateAsync(new IdentityRole<Guid>(role));
                if (!result.Succeeded)
                {
                    _logger.LogError("Failed to create role {Role}. Errors: {Errors}", role, string.Join(",", result.Errors.Select(e => e.Description)));
                }
            }
        }
    }

    private async Task SeedSuperAdminAsync(CancellationToken cancellationToken)
    {
        var settings = _configuration.GetSection("InitialSuperAdmin");
        var email = settings["Email"];
        var password = settings["Password"];
        var displayName = settings["DisplayName"] ?? "Super Admin";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("InitialSuperAdmin credentials not configured. Skipping super admin seed.");
            return;
        }

        var existing = await _userManager.FindByEmailAsync(email);
        if (existing != null)
        {
            if (!await _userManager.IsInRoleAsync(existing, AppRoles.SuperAdmin))
            {
                await _userManager.AddToRoleAsync(existing, AppRoles.SuperAdmin);
            }
            return;
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            DisplayName = displayName,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            _logger.LogError("Failed to create initial super admin {Email}. Errors: {Errors}", email, string.Join(",", result.Errors.Select(e => e.Description)));
            return;
        }

        await _userManager.AddToRoleAsync(user, AppRoles.SuperAdmin);
    }

    private async Task SeedTravelTimeModelAsync(CancellationToken cancellationToken)
    {
        var hasRegions = await _dbContext.TravelTimeRegions.AnyAsync(cancellationToken);
        var hasProfiles = await _dbContext.RegionSpeedProfiles.AnyAsync(cancellationToken);

        if (hasRegions && hasProfiles)
        {
            _logger.LogInformation("Travel time model data already exists. Skipping seed.");
            return;
        }

        _logger.LogInformation("Seeding travel time model data...");

        if (!hasRegions)
        {
            var regions = TravelTimeSeedParser.ParseRegions(TravelTimeSeedData.RegionsCsv);
            if (regions.Count > 0)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                await _dbContext.Database.ExecuteSqlRawAsync(
                    "SET IDENTITY_INSERT [dbo].[TravelTimeRegions] ON",
                    cancellationToken);
                try
                {
                    _dbContext.TravelTimeRegions.AddRange(regions);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await _dbContext.Database.ExecuteSqlRawAsync(
                        "SET IDENTITY_INSERT [dbo].[TravelTimeRegions] OFF",
                        cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    await _dbContext.Database.ExecuteSqlRawAsync(
                        "SET IDENTITY_INSERT [dbo].[TravelTimeRegions] OFF",
                        cancellationToken);
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }
        }

        if (!hasProfiles)
        {
            var profiles = TravelTimeSeedParser.ParseSpeedProfiles(TravelTimeSeedData.SpeedProfilesCsv);
            if (profiles.Count > 0)
            {
                _dbContext.RegionSpeedProfiles.AddRange(profiles);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        _logger.LogInformation("Travel time model seed completed.");
    }
}
