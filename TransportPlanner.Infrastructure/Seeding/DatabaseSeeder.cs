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

        // Seed service types FIRST (they are required for service locations)
        // Check if the required service types exist, if not, seed them
        var chargingPostExists = await _dbContext.ServiceTypes.AnyAsync(st => st.Code == "CHARGING_POST", cancellationToken);
        var pharmaExists = await _dbContext.ServiceTypes.AnyAsync(st => st.Code == "PHARMA", cancellationToken);
        var generalExists = await _dbContext.ServiceTypes.AnyAsync(st => st.Code == "GENERAL", cancellationToken);

        if (!chargingPostExists || !pharmaExists || !generalExists)
        {
            _logger.LogInformation("Seeding service types...");
            await SeedServiceTypesAsync(cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Service types seeded successfully.");
        }
        else
        {
            _logger.LogInformation("Service types already exist. Skipping service type seed.");
        }

        // Seed service location owners (required for service locations)
        var antwerpExists = await _dbContext.ServiceLocationOwners.AnyAsync(so => so.Code == "TRESCAL_ANTWERP", cancellationToken);
        var zoetermeerExists = await _dbContext.ServiceLocationOwners.AnyAsync(so => so.Code == "TRESCAL_ZOETERMEER", cancellationToken);

        if (!antwerpExists || !zoetermeerExists)
        {
            _logger.LogInformation("Seeding service location owners...");
            await SeedServiceLocationOwnersAsync(cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Service location owners seeded successfully.");
        }
        else
        {
            _logger.LogInformation("Service location owners already exist. Skipping owner seed.");
        }

        // Check if drivers already exist - only seed drivers if database is empty
        var existingDrivers = await _dbContext.Drivers.AnyAsync(cancellationToken);
        if (!existingDrivers)
        {
            _logger.LogInformation("Seeding drivers...");
            await SeedDriversAsync(cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken); // Save drivers to get IDs
            _logger.LogInformation("Drivers seeded successfully.");
        }
        else
        {
            _logger.LogInformation("Drivers already exist. Skipping driver seed.");
        }

        // Update existing drivers that don't have a valid OwnerId
        await UpdateExistingDriversWithOwnerAsync(cancellationToken);

        // Update existing service locations that don't have a valid ServiceTypeId or OwnerId
        await UpdateExistingServiceLocationsWithServiceTypeAsync(cancellationToken);
        await UpdateExistingServiceLocationsWithOwnerAsync(cancellationToken);

        // Reseed service locations (clear and recreate) - useful for development/testing
        // Uncomment the line below to reseed service locations on every startup
        // await ReseedServiceLocationsAsync(cancellationToken);

        // Always seed service locations if they don't exist
        var existingServiceLocations = await _dbContext.ServiceLocations.AnyAsync(cancellationToken);
        if (!existingServiceLocations)
        {
            _logger.LogInformation("Seeding service locations...");
            await SeedServiceLocationsAsync(cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Database seeding completed successfully.");
        }
        else
        {
            _logger.LogInformation("Service locations already exist. Skipping seed.");
        }
    }

    private async Task ClearSeedDataAsync(CancellationToken cancellationToken)
    {
        // Clear DriverAvailabilities table (one-time cleanup)
        var driverAvailabilities = await _dbContext.DriverAvailabilities.ToListAsync(cancellationToken);
        if (driverAvailabilities.Any())
        {
            _logger.LogInformation($"Clearing {driverAvailabilities.Count} driver availabilities...");
            _dbContext.DriverAvailabilities.RemoveRange(driverAvailabilities);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var serviceLocations = await _dbContext.ServiceLocations.ToListAsync(cancellationToken);
        if (serviceLocations.Any())
        {
            _dbContext.ServiceLocations.RemoveRange(serviceLocations);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var drivers = await _dbContext.Drivers.ToListAsync(cancellationToken);
        if (drivers.Any())
        {
            _dbContext.Drivers.RemoveRange(drivers);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Existing seed data cleared.");
    }

    private async Task SeedServiceTypesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Seeding service types...");

        var now = DateTime.UtcNow;
        var serviceTypesToAdd = new List<ServiceType>();

        // Check and add CHARGING_POST if it doesn't exist
        if (!await _dbContext.ServiceTypes.AnyAsync(st => st.Code == "CHARGING_POST", cancellationToken))
        {
            serviceTypesToAdd.Add(new ServiceType
            {
                Code = "CHARGING_POST",
                Name = "Charging Posts",
                Description = "Electric vehicle charging posts",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        // Check and add PHARMA if it doesn't exist
        if (!await _dbContext.ServiceTypes.AnyAsync(st => st.Code == "PHARMA", cancellationToken))
        {
            serviceTypesToAdd.Add(new ServiceType
            {
                Code = "PHARMA",
                Name = "Pharmacist Interventions",
                Description = "Pharmacist service interventions",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        // Check and add GENERAL if it doesn't exist
        if (!await _dbContext.ServiceTypes.AnyAsync(st => st.Code == "GENERAL", cancellationToken))
        {
            serviceTypesToAdd.Add(new ServiceType
            {
                Code = "GENERAL",
                Name = "General Service",
                Description = "General service locations",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        if (serviceTypesToAdd.Any())
        {
            await _dbContext.ServiceTypes.AddRangeAsync(serviceTypesToAdd, cancellationToken);
            _logger.LogInformation($"Seeded {serviceTypesToAdd.Count} service types.");
        }
        else
        {
            _logger.LogInformation("All required service types already exist.");
        }
    }

    private async Task SeedServiceLocationOwnersAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Seeding service location owners...");

        var now = DateTime.UtcNow;
        var ownersToAdd = new List<ServiceLocationOwner>();

        // Check and add TRESCAL_ANTWERP if it doesn't exist
        if (!await _dbContext.ServiceLocationOwners.AnyAsync(so => so.Code == "TRESCAL_ANTWERP", cancellationToken))
        {
            ownersToAdd.Add(new ServiceLocationOwner
            {
                Code = "TRESCAL_ANTWERP",
                Name = "Trescal Antwerp",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        // Check and add TRESCAL_ZOETERMEER if it doesn't exist
        if (!await _dbContext.ServiceLocationOwners.AnyAsync(so => so.Code == "TRESCAL_ZOETERMEER", cancellationToken))
        {
            ownersToAdd.Add(new ServiceLocationOwner
            {
                Code = "TRESCAL_ZOETERMEER",
                Name = "Trescal Zoetermeer",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        if (ownersToAdd.Any())
        {
            await _dbContext.ServiceLocationOwners.AddRangeAsync(ownersToAdd, cancellationToken);
            _logger.LogInformation($"Seeded {ownersToAdd.Count} service location owners.");
        }
        else
        {
            _logger.LogInformation("All required service location owners already exist.");
        }
    }

    /// <summary>
    /// Reseeds service types by clearing existing ones and creating new ones.
    /// Useful for development/testing.
    /// NOTE: No FK constraint, so we can delete ServiceTypes without deleting ServiceLocations first.
    /// </summary>
    public async Task ReseedServiceTypesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reseeding service types (clearing existing data)...");

        // No FK constraint, so we can delete ServiceTypes directly
        var typesCount = await _dbContext.ServiceTypes.CountAsync(cancellationToken);
        if (typesCount > 0)
        {
            _logger.LogInformation($"Removing {typesCount} service types...");
            await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM ServiceTypes", cancellationToken);
            _logger.LogInformation($"Cleared {typesCount} existing service types.");
        }

        // Seed new service types
        await SeedServiceTypesAsync(cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Service types reseeded successfully.");
    }

    private async Task SeedDriversAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Seeding drivers...");

        // Get owners - must exist at this point
        var antwerpOwner = await _dbContext.ServiceLocationOwners
            .AsNoTracking()
            .FirstOrDefaultAsync(so => so.Code == "TRESCAL_ANTWERP", cancellationToken);
        var zoetermeerOwner = await _dbContext.ServiceLocationOwners
            .AsNoTracking()
            .FirstOrDefaultAsync(so => so.Code == "TRESCAL_ZOETERMEER", cancellationToken);

        if (antwerpOwner == null || zoetermeerOwner == null)
        {
            _logger.LogError("Required owners not found. Cannot seed drivers.");
            throw new InvalidOperationException("TRESCAL_ANTWERP and TRESCAL_ZOETERMEER owners must exist before seeding drivers. Run SeedServiceLocationOwnersAsync first.");
        }

        var now = DateTime.UtcNow;
        
        // Only seed 2 drivers: Anna Smet and David Goossens
        // Anna -> Antwerp, David -> Zoetermeer
        var drivers = new List<Driver>
        {
            new Driver
            {
                ToolId = Guid.NewGuid(),
                ErpId = 1001,
                Name = "Anna Smet",
                StartAddress = "Brussels, Belgium",
                StartLatitude = 50.8503, // Brussels-ish
                StartLongitude = 4.3517,
                DefaultServiceMinutes = 20,
                MaxWorkMinutesPerDay = 480,
                OwnerId = antwerpOwner.Id, // Anna -> Antwerp
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new Driver
            {
                ToolId = Guid.NewGuid(),
                ErpId = 1002,
                Name = "David Goossens",
                StartAddress = "Antwerp, Belgium",
                StartLatitude = 51.2194, // Antwerp-ish
                StartLongitude = 4.4025,
                DefaultServiceMinutes = 20,
                MaxWorkMinutesPerDay = 480,
                OwnerId = zoetermeerOwner.Id, // David -> Zoetermeer
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            }
        };

        await _dbContext.Drivers.AddRangeAsync(drivers, cancellationToken);
    }

    private async Task SeedServiceLocationsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Seeding service locations...");

        var now = DateTime.UtcNow;
        var today = DateTime.Today;
        var random = new Random();

        // Belgian city coordinates
        var locations = new List<(double lat, double lon, string city, string address)>
        {
            (51.2194, 4.4025, "Antwerp", "Antwerp Central Station"),
            (50.8503, 4.3517, "Brussels", "Brussels Central Station"),
            (51.0543, 3.7174, "Ghent", "Ghent Sint-Pieters Station"),
            (50.8798, 4.7005, "Leuven", "Leuven Station"),
            (51.0257, 4.4776, "Mechelen", "Mechelen Station"),
            (50.9307, 5.3325, "Hasselt", "Hasselt Station"),
            (50.9650, 5.5000, "Genk", "Genk Station"),
            (51.3226, 4.9486, "Turnhout", "Turnhout Station"),
            (50.6292, 3.0573, "Mons", "Mons Station"),
            (50.4542, 3.9527, "Charleroi", "Charleroi-Sud Station"),
        };

        // Get default service type ONCE before the loop - must exist at this point
        // Query directly from database to ensure we have the actual ID
        var defaultServiceType = await _dbContext.ServiceTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(st => st.Code == "CHARGING_POST", cancellationToken);
        
        if (defaultServiceType == null)
        {
            _logger.LogError("CHARGING_POST service type not found. Cannot seed service locations.");
            throw new InvalidOperationException("CHARGING_POST service type must exist before seeding service locations. Run SeedServiceTypesAsync first.");
        }
        
        _logger.LogInformation($"Using ServiceType CHARGING_POST with Id: {defaultServiceType.Id} for seeding service locations.");

        // Get default owner ONCE before the loop - must exist at this point
        var defaultOwner = await _dbContext.ServiceLocationOwners
            .AsNoTracking()
            .FirstOrDefaultAsync(so => so.Code == "TRESCAL_ANTWERP", cancellationToken);
        
        if (defaultOwner == null)
        {
            _logger.LogError("TRESCAL_ANTWERP owner not found. Cannot seed service locations.");
            throw new InvalidOperationException("TRESCAL_ANTWERP owner must exist before seeding service locations. Run SeedServiceLocationOwnersAsync first.");
        }
        
        _logger.LogInformation($"Using Owner TRESCAL_ANTWERP with Id: {defaultOwner.Id} for seeding service locations.");
        
        var serviceLocations = new List<ServiceLocation>();

        for (int i = 1; i <= 10; i++)
        {
            var location = locations[i - 1];
            var locationIndex = (i - 1) % locations.Count;
            var loc = locations[locationIndex];

            // Mix of due dates: some today, some in future
            DateTime dueDate;
            DateTime? priorityDate = null;

            if (i <= 3)
            {
                // First 3: due today or yesterday (urgent)
                dueDate = today.AddDays(random.Next(-1, 2));
                if (i == 1)
                {
                    // First one has priority date
                    priorityDate = today.AddDays(-1);
                }
            }
            else if (i <= 6)
            {
                // Next 3: due in 2-5 days
                dueDate = today.AddDays(random.Next(2, 6));
                if (i == 4)
                {
                    // One has priority date
                    priorityDate = today.AddDays(1);
                }
            }
            else
            {
                // Last 4: due in 6-14 days
                dueDate = today.AddDays(random.Next(6, 15));
            }

            var serviceLocation = new ServiceLocation
            {
                ToolId = Guid.NewGuid(),
                ErpId = 2000 + i,
                Name = $"Service Station {loc.city}",
                Address = loc.address,
                Latitude = loc.lat + (random.NextDouble() * 0.1 - 0.05), // Small random offset
                Longitude = loc.lon + (random.NextDouble() * 0.1 - 0.05),
                DueDate = dueDate.Date,
                PriorityDate = priorityDate?.Date,
                ServiceMinutes = 20 + random.Next(0, 21), // 20-40 minutes
                ServiceTypeId = defaultServiceType.Id, // Set directly, no FK constraint
                OwnerId = defaultOwner.Id, // Set directly, no FK constraint
                Status = ServiceLocationStatus.Open,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            serviceLocations.Add(serviceLocation);
        }

        await _dbContext.ServiceLocations.AddRangeAsync(serviceLocations, cancellationToken);
        _logger.LogInformation($"Seeded {serviceLocations.Count} service locations.");
    }

    private async Task UpdateExistingServiceLocationsWithServiceTypeAsync(CancellationToken cancellationToken)
    {
        // Get the default service type (CHARGING_POST)
        var defaultServiceType = await _dbContext.ServiceTypes
            .FirstOrDefaultAsync(st => st.Code == "CHARGING_POST", cancellationToken);

        if (defaultServiceType == null)
        {
            _logger.LogWarning("CHARGING_POST service type not found. Cannot update existing service locations.");
            return;
        }

        // Get all valid service type IDs (both active and inactive, to check if FK exists)
        var validServiceTypeIds = await _dbContext.ServiceTypes
            .Select(st => st.Id)
            .ToListAsync(cancellationToken);

        if (!validServiceTypeIds.Any())
        {
            _logger.LogWarning("No service types found. Cannot update existing service locations.");
            return;
        }

        // Find all service locations that don't have a valid ServiceTypeId
        // This includes ServiceTypeId = 0 or any ID that doesn't exist in ServiceTypes
        var allServiceLocations = await _dbContext.ServiceLocations.ToListAsync(cancellationToken);
        var serviceLocationsToUpdate = allServiceLocations
            .Where(sl => sl.ServiceTypeId == 0 || !validServiceTypeIds.Contains(sl.ServiceTypeId))
            .ToList();

        if (serviceLocationsToUpdate.Any())
        {
            _logger.LogInformation($"Found {serviceLocationsToUpdate.Count} service locations with invalid ServiceTypeId. Updating to default ServiceTypeId {defaultServiceType.Id}...");
            var now = DateTime.UtcNow;
            
            foreach (var location in serviceLocationsToUpdate)
            {
                _logger.LogInformation($"Updating ServiceLocation ID {location.Id} (ErpId: {location.ErpId}, Name: {location.Name}) from ServiceTypeId {location.ServiceTypeId} to {defaultServiceType.Id}");
                location.ServiceTypeId = defaultServiceType.Id;
                location.UpdatedAtUtc = now;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation($"Successfully updated {serviceLocationsToUpdate.Count} service locations with ServiceTypeId {defaultServiceType.Id}.");
        }
        else
        {
            _logger.LogInformation("All service locations have valid ServiceTypeId. No updates needed.");
        }
    }

    private async Task UpdateExistingServiceLocationsWithOwnerAsync(CancellationToken cancellationToken)
    {
        // Get the default owner (TRESCAL_ANTWERP)
        var defaultOwner = await _dbContext.ServiceLocationOwners
            .FirstOrDefaultAsync(so => so.Code == "TRESCAL_ANTWERP", cancellationToken);

        if (defaultOwner == null)
        {
            _logger.LogWarning("TRESCAL_ANTWERP owner not found. Cannot update existing service locations.");
            return;
        }

        // Get all valid owner IDs (both active and inactive, to check if exists)
        var validOwnerIds = await _dbContext.ServiceLocationOwners
            .Select(so => so.Id)
            .ToListAsync(cancellationToken);

        if (!validOwnerIds.Any())
        {
            _logger.LogWarning("No service location owners found. Cannot update existing service locations.");
            return;
        }

        // Find all service locations that don't have a valid OwnerId
        // This includes OwnerId = 0 or any ID that doesn't exist in ServiceLocationOwners
        var allServiceLocations = await _dbContext.ServiceLocations.ToListAsync(cancellationToken);
        var serviceLocationsToUpdate = allServiceLocations
            .Where(sl => sl.OwnerId == 0 || !validOwnerIds.Contains(sl.OwnerId))
            .ToList();

        if (serviceLocationsToUpdate.Any())
        {
            _logger.LogInformation($"Found {serviceLocationsToUpdate.Count} service locations with invalid OwnerId. Updating to default OwnerId {defaultOwner.Id}...");
            var now = DateTime.UtcNow;
            
            foreach (var location in serviceLocationsToUpdate)
            {
                _logger.LogInformation($"Updating ServiceLocation ID {location.Id} (ErpId: {location.ErpId}, Name: {location.Name}) from OwnerId {location.OwnerId} to {defaultOwner.Id}");
                location.OwnerId = defaultOwner.Id;
                location.UpdatedAtUtc = now;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation($"Successfully updated {serviceLocationsToUpdate.Count} service locations with OwnerId {defaultOwner.Id}.");
        }
        else
        {
            _logger.LogInformation("All service locations have valid OwnerId. No updates needed.");
        }
    }

    private async Task UpdateExistingDriversWithOwnerAsync(CancellationToken cancellationToken)
    {
        // Get the default owner (TRESCAL_ANTWERP)
        var defaultOwner = await _dbContext.ServiceLocationOwners
            .FirstOrDefaultAsync(so => so.Code == "TRESCAL_ANTWERP", cancellationToken);

        if (defaultOwner == null)
        {
            _logger.LogWarning("TRESCAL_ANTWERP owner not found. Cannot update existing drivers.");
            return;
        }

        // Get all valid owner IDs (both active and inactive, to check if exists)
        var validOwnerIds = await _dbContext.ServiceLocationOwners
            .Select(so => so.Id)
            .ToListAsync(cancellationToken);

        if (!validOwnerIds.Any())
        {
            _logger.LogWarning("No service location owners found. Cannot update existing drivers.");
            return;
        }

        // Find all drivers that don't have a valid OwnerId
        // This includes OwnerId = 0 or any ID that doesn't exist in ServiceLocationOwners
        var allDrivers = await _dbContext.Drivers.ToListAsync(cancellationToken);
        var driversToUpdate = allDrivers
            .Where(d => d.OwnerId == 0 || !validOwnerIds.Contains(d.OwnerId))
            .ToList();

        if (driversToUpdate.Any())
        {
            _logger.LogInformation($"Found {driversToUpdate.Count} drivers with invalid OwnerId. Updating to default OwnerId {defaultOwner.Id}...");
            var now = DateTime.UtcNow;
            
            foreach (var driver in driversToUpdate)
            {
                _logger.LogInformation($"Updating Driver ID {driver.Id} (ErpId: {driver.ErpId}, Name: {driver.Name}) from OwnerId {driver.OwnerId} to {defaultOwner.Id}");
                driver.OwnerId = defaultOwner.Id;
                driver.UpdatedAtUtc = now;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation($"Successfully updated {driversToUpdate.Count} drivers with OwnerId {defaultOwner.Id}.");
        }
        else
        {
            _logger.LogInformation("All drivers have valid OwnerId. No updates needed.");
        }
    }

    /// <summary>
    /// Clears all service locations and reseeds them with mock data.
    /// Useful for development/testing to ensure all records have proper ServiceTypeId.
    /// </summary>
    public async Task ReseedServiceLocationsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reseeding service locations (clearing existing data)...");

        // CRITICAL: Ensure service types exist first and are saved
        var chargingPostExists = await _dbContext.ServiceTypes.AnyAsync(st => st.Code == "CHARGING_POST", cancellationToken);
        var pharmaExists = await _dbContext.ServiceTypes.AnyAsync(st => st.Code == "PHARMA", cancellationToken);
        var generalExists = await _dbContext.ServiceTypes.AnyAsync(st => st.Code == "GENERAL", cancellationToken);

        if (!chargingPostExists || !pharmaExists || !generalExists)
        {
            _logger.LogInformation("Seeding service types before reseeding service locations...");
            await SeedServiceTypesAsync(cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Service types seeded and saved.");
        }

        // CRITICAL: Ensure owners exist first and are saved
        var antwerpExists = await _dbContext.ServiceLocationOwners.AnyAsync(so => so.Code == "TRESCAL_ANTWERP", cancellationToken);
        var zoetermeerExists = await _dbContext.ServiceLocationOwners.AnyAsync(so => so.Code == "TRESCAL_ZOETERMEER", cancellationToken);

        if (!antwerpExists || !zoetermeerExists)
        {
            _logger.LogInformation("Seeding service location owners before reseeding service locations...");
            await SeedServiceLocationOwnersAsync(cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Service location owners seeded and saved.");
        }

        // Verify service types exist and get their IDs
        var chargingPost = await _dbContext.ServiceTypes.FirstOrDefaultAsync(st => st.Code == "CHARGING_POST", cancellationToken);
        if (chargingPost == null)
        {
            _logger.LogError("CHARGING_POST service type not found after seeding. Cannot reseed service locations.");
            throw new InvalidOperationException("CHARGING_POST service type must exist before reseeding service locations.");
        }
        _logger.LogInformation($"Using ServiceType CHARGING_POST with Id: {chargingPost.Id}");

        // Verify owners exist and get their IDs
        var antwerpOwner = await _dbContext.ServiceLocationOwners.FirstOrDefaultAsync(so => so.Code == "TRESCAL_ANTWERP", cancellationToken);
        if (antwerpOwner == null)
        {
            _logger.LogError("TRESCAL_ANTWERP owner not found after seeding. Cannot reseed service locations.");
            throw new InvalidOperationException("TRESCAL_ANTWERP owner must exist before reseeding service locations.");
        }
        _logger.LogInformation($"Using Owner TRESCAL_ANTWERP with Id: {antwerpOwner.Id}");

        // Clear existing service locations
        var existingLocations = await _dbContext.ServiceLocations.ToListAsync(cancellationToken);
        if (existingLocations.Any())
        {
            _logger.LogInformation($"Removing {existingLocations.Count} existing service locations...");
            _dbContext.ServiceLocations.RemoveRange(existingLocations);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation($"Cleared {existingLocations.Count} existing service locations.");
        }

        // Seed new service locations (this will use the verified ServiceTypeId)
        _logger.LogInformation("Seeding new service locations...");
        await SeedServiceLocationsAsync(cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Service locations reseeded successfully.");
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
}
