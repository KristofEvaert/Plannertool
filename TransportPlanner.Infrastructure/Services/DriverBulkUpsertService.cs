using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;

namespace TransportPlanner.Infrastructure.Services;

public class DriverBulkUpsertService
{
    private readonly TransportPlannerDbContext _dbContext;
    private readonly ILogger<DriverBulkUpsertService> _logger;
    private readonly IGeocodingService _geocodingService;

    public DriverBulkUpsertService(
        TransportPlannerDbContext dbContext,
        ILogger<DriverBulkUpsertService> logger,
        IGeocodingService geocodingService)
    {
        _dbContext = dbContext;
        _logger = logger;
        _geocodingService = geocodingService;
    }

    public async Task<BulkUpsertResultDto> UpsertAsync(
        BulkUpsertDriversRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = new BulkUpsertResultDto();
        var now = DateTime.UtcNow;

        // Step 1: Validate and upsert drivers
        var driverMap = new Dictionary<string, Driver>(); // Key: toolId or erpId as string
        var driverIdMap = new Dictionary<string, int>(); // Map toolId/erpId to Driver.Id

        for (int i = 0; i < request.Drivers.Count; i++)
        {
            var driverDto = request.Drivers[i];
            var rowRef = $"Drivers row {i + 1}";

            // Validation
            if (string.IsNullOrWhiteSpace(driverDto.Name))
            {
                result.Errors.Add(new BulkErrorDto
                {
                    Scope = "Driver",
                    RowRef = rowRef,
                    Message = "Name is required"
                });
                continue;
            }

            if (driverDto.ErpId <= 0)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    Scope = "Driver",
                    RowRef = rowRef,
                    Message = "ErpId must be greater than 0"
                });
                continue;
            }

            if (driverDto.StartLatitude == 0)
            {
                driverDto.StartLatitude = null;
            }
            if (driverDto.StartLongitude == 0)
            {
                driverDto.StartLongitude = null;
            }

            var startAddress = driverDto.StartAddress?.Trim();
            var hasAddress = !string.IsNullOrWhiteSpace(startAddress);
            var hasLatitude = driverDto.StartLatitude.HasValue;
            var hasLongitude = driverDto.StartLongitude.HasValue;
            if (hasLatitude != hasLongitude)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    Scope = "Driver",
                    RowRef = rowRef,
                    Message = "Provide both StartLatitude and StartLongitude, or leave both empty"
                });
                continue;
            }

            if (!hasAddress && !hasLatitude)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    Scope = "Driver",
                    RowRef = rowRef,
                    Message = "StartAddress or StartLatitude/StartLongitude is required"
                });
                continue;
            }

            if (!hasLatitude && hasAddress)
            {
                var geocode = await _geocodingService.GeocodeAddressAsync(startAddress!, cancellationToken);
                if (geocode == null)
                {
                    result.Errors.Add(new BulkErrorDto
                    {
                        Scope = "Driver",
                        RowRef = rowRef,
                        Message = "Unable to resolve StartLatitude/StartLongitude from StartAddress"
                    });
                    continue;
                }
                driverDto.StartLatitude = geocode.Latitude;
                driverDto.StartLongitude = geocode.Longitude;
                hasLatitude = true;
                hasLongitude = true;
            }
            else if (!hasAddress && hasLatitude)
            {
                var reverseAddress = await _geocodingService.ReverseGeocodeAsync(driverDto.StartLatitude!.Value, driverDto.StartLongitude!.Value, cancellationToken);
                if (string.IsNullOrWhiteSpace(reverseAddress))
                {
                    result.Errors.Add(new BulkErrorDto
                    {
                        Scope = "Driver",
                        RowRef = rowRef,
                        Message = "Unable to resolve StartAddress from StartLatitude/StartLongitude"
                    });
                    continue;
                }
                startAddress = reverseAddress;
                hasAddress = true;
            }

            if (!hasLatitude || !hasLongitude)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    Scope = "Driver",
                    RowRef = rowRef,
                    Message = "StartLatitude and StartLongitude are required after geocoding"
                });
                continue;
            }

            var latitude = driverDto.StartLatitude!.Value;
            var longitude = driverDto.StartLongitude!.Value;
            if (latitude < -90 || latitude > 90)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    Scope = "Driver",
                    RowRef = rowRef,
                    Message = "StartLatitude must be between -90 and 90"
                });
                continue;
            }

            if (longitude < -180 || longitude > 180)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    Scope = "Driver",
                    RowRef = rowRef,
                    Message = "StartLongitude must be between -180 and 180"
                });
                continue;
            }

            // Validate and resolve OwnerId
            int resolvedOwnerId = 0;
            if (driverDto.OwnerId.HasValue)
            {
                var owner = await _dbContext.ServiceLocationOwners
                    .FirstOrDefaultAsync(so => so.Id == driverDto.OwnerId.Value && so.IsActive, cancellationToken);
                if (owner == null)
                {
                    result.Errors.Add(new BulkErrorDto
                    {
                        Scope = "Driver",
                        RowRef = rowRef,
                        Message = $"OwnerId {driverDto.OwnerId.Value} does not exist or is not active"
                    });
                    continue;
                }
                resolvedOwnerId = owner.Id;
            }
            else if (!string.IsNullOrWhiteSpace(driverDto.OwnerCode))
            {
                var owner = await _dbContext.ServiceLocationOwners
                    .FirstOrDefaultAsync(so => so.Code == driverDto.OwnerCode && so.IsActive, cancellationToken);
                if (owner == null)
                {
                    result.Errors.Add(new BulkErrorDto
                    {
                        Scope = "Driver",
                        RowRef = rowRef,
                        Message = $"OwnerCode '{driverDto.OwnerCode}' does not exist or is not active"
                    });
                    continue;
                }
                resolvedOwnerId = owner.Id;
            }
            else
            {
                result.Errors.Add(new BulkErrorDto
                {
                    Scope = "Driver",
                    RowRef = rowRef,
                    Message = "OwnerId or OwnerCode is required"
                });
                continue;
            }

            if (driverDto.IsActive == false)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    Scope = "Driver",
                    RowRef = rowRef,
                    Message = "Soft delete not allowed via bulk operations"
                });
                // Continue processing but ignore IsActive
            }

            // Find or create driver
            Driver? driver = null;
            string lookupKey = string.Empty;

            if (driverDto.ToolId.HasValue)
            {
                lookupKey = driverDto.ToolId.Value.ToString();
                driver = await _dbContext.Drivers
                    .FirstOrDefaultAsync(d => d.ToolId == driverDto.ToolId.Value, cancellationToken);
            }
            else
            {
                lookupKey = $"ERP_{driverDto.ErpId}";
                driver = await _dbContext.Drivers
                    .FirstOrDefaultAsync(d => d.ErpId == driverDto.ErpId, cancellationToken);
            }

            if (driver == null)
            {
                // Create new driver
                driver = new Driver
                {
                    ToolId = driverDto.ToolId ?? Guid.NewGuid(),
                    ErpId = driverDto.ErpId,
                    Name = driverDto.Name,
                    StartAddress = startAddress,
                    StartLatitude = latitude,
                    StartLongitude = longitude,
                    DefaultServiceMinutes = driverDto.DefaultServiceMinutes ?? 20,
                    MaxWorkMinutesPerDay = driverDto.MaxWorkMinutesPerDay ?? 480,
                    OwnerId = resolvedOwnerId,
                    IsActive = true, // Always active for bulk operations
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                _dbContext.Drivers.Add(driver);
                result.DriversCreated++;
            }
            else
            {
                // Update existing driver
                // Check ErpId uniqueness if changed
                if (driver.ErpId != driverDto.ErpId)
                {
                    var erpIdExists = await _dbContext.Drivers
                        .AnyAsync(d => d.ErpId == driverDto.ErpId && d.Id != driver.Id, cancellationToken);
                    if (erpIdExists)
                    {
                        result.Errors.Add(new BulkErrorDto
                        {
                            Scope = "Driver",
                            RowRef = rowRef,
                            Message = $"ErpId {driverDto.ErpId} already exists for another driver"
                        });
                        continue;
                    }
                }

                driver.ErpId = driverDto.ErpId;
                driver.Name = driverDto.Name;
                driver.StartAddress = startAddress;
                driver.StartLatitude = latitude;
                driver.StartLongitude = longitude;
                driver.DefaultServiceMinutes = driverDto.DefaultServiceMinutes ?? driver.DefaultServiceMinutes;
                driver.MaxWorkMinutesPerDay = driverDto.MaxWorkMinutesPerDay ?? driver.MaxWorkMinutesPerDay;
                driver.OwnerId = resolvedOwnerId; // Update owner (drivers can be reassigned)
                // IsActive is NOT updated via bulk (always stays true or current value)
                driver.UpdatedAtUtc = now;
                result.DriversUpdated++;
            }

            driverMap[lookupKey] = driver;
            // Also map by both identifiers for availability lookup
            if (driver.ToolId != Guid.Empty)
            {
                driverMap[driver.ToolId.ToString()] = driver;
            }
            driverMap[$"ERP_{driver.ErpId}"] = driver;
        }

        // Save drivers first to get IDs
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Build driver ID map
        foreach (var kvp in driverMap)
        {
            if (!driverIdMap.ContainsKey(kvp.Key))
            {
                driverIdMap[kvp.Key] = kvp.Value.Id;
            }
        }

        // Step 2: Upsert availabilities
        for (int i = 0; i < request.Availabilities.Count; i++)
        {
            var avDto = request.Availabilities[i];
            var rowRef = $"Availability row {i + 1}";

            // Find driver
            Driver? driver = null;
            string? lookupKey = null;

            if (avDto.DriverToolId.HasValue)
            {
                lookupKey = avDto.DriverToolId.Value.ToString();
                driver = driverMap.GetValueOrDefault(lookupKey);
            }
            else if (avDto.DriverErpId.HasValue)
            {
                lookupKey = $"ERP_{avDto.DriverErpId.Value}";
                driver = driverMap.GetValueOrDefault(lookupKey);
            }

            if (driver == null)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    Scope = "Availability",
                    RowRef = rowRef,
                    Message = "Driver not found. Provide either DriverToolId or DriverErpId"
                });
                continue;
            }

            // Validation
            if (avDto.StartMinuteOfDay < 0 || avDto.StartMinuteOfDay > 1439)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    Scope = "Availability",
                    RowRef = rowRef,
                    Message = "StartMinuteOfDay must be between 0 and 1439"
                });
                continue;
            }

            if (avDto.EndMinuteOfDay < 1 || avDto.EndMinuteOfDay > 1440)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    Scope = "Availability",
                    RowRef = rowRef,
                    Message = "EndMinuteOfDay must be between 1 and 1440"
                });
                continue;
            }

            if (avDto.EndMinuteOfDay <= avDto.StartMinuteOfDay)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    Scope = "Availability",
                    RowRef = rowRef,
                    Message = "EndMinuteOfDay must be greater than StartMinuteOfDay"
                });
                continue;
            }

            var dateOnly = avDto.Date.ToDateTime(TimeOnly.MinValue).Date;

            // Find or create availability
            var availability = await _dbContext.DriverAvailabilities
                .FirstOrDefaultAsync(
                    da => da.DriverId == driver.Id && da.Date == dateOnly,
                    cancellationToken);

            if (availability == null)
            {
                availability = new DriverAvailability
                {
                    DriverId = driver.Id,
                    Date = dateOnly,
                    StartMinuteOfDay = avDto.StartMinuteOfDay,
                    EndMinuteOfDay = avDto.EndMinuteOfDay,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                _dbContext.DriverAvailabilities.Add(availability);
            }
            else
            {
                availability.StartMinuteOfDay = avDto.StartMinuteOfDay;
                availability.EndMinuteOfDay = avDto.EndMinuteOfDay;
                availability.UpdatedAtUtc = now;
            }

            result.AvailabilitiesUpserted++;
        }

        // Save all changes
        await _dbContext.SaveChangesAsync(cancellationToken);

        return result;
    }
}
