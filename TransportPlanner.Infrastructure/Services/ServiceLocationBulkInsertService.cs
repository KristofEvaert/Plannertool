using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;

namespace TransportPlanner.Infrastructure.Services;

public class ServiceLocationBulkInsertService
{
    private readonly TransportPlannerDbContext _dbContext;
    private readonly ILogger<ServiceLocationBulkInsertService> _logger;
    private readonly IGeocodingService _geocodingService;

    public ServiceLocationBulkInsertService(
        TransportPlannerDbContext dbContext,
        ILogger<ServiceLocationBulkInsertService> logger,
        IGeocodingService geocodingService)
    {
        _dbContext = dbContext;
        _logger = logger;
        _geocodingService = geocodingService;
    }

    public async Task<BulkInsertResultDto> InsertAsync(
        BulkInsertServiceLocationsRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = new BulkInsertResultDto();
        var now = DateTime.UtcNow;

        // Validate ServiceTypeId exists and is active
        var serviceType = await _dbContext.ServiceTypes
            .FirstOrDefaultAsync(st => st.Id == request.ServiceTypeId && st.IsActive, cancellationToken);
        if (serviceType == null)
        {
            result.Errors.Add(new BulkErrorDto
            {
                RowRef = "Request",
                Message = $"ServiceTypeId {request.ServiceTypeId} does not exist or is not active"
            });
            result.Skipped = request.Items.Count;
            return result;
        }

        // Validate OwnerId exists and is active
        var owner = await _dbContext.ServiceLocationOwners
            .FirstOrDefaultAsync(so => so.Id == request.OwnerId && so.IsActive, cancellationToken);
        if (owner == null)
        {
            result.Errors.Add(new BulkErrorDto
            {
                RowRef = "Request",
                Message = $"OwnerId {request.OwnerId} does not exist or is not active"
            });
            result.Skipped = request.Items.Count;
            return result;
        }

        // Preload existing service locations by ERP ID
        var erpIdsInRequest = request.Items.Select(i => i.ErpId).Distinct().ToList();
        var existingServiceLocations = await _dbContext.ServiceLocations
            .Where(sl => erpIdsInRequest.Contains(sl.ErpId))
            .ToDictionaryAsync(sl => sl.ErpId, cancellationToken);

        _logger.LogInformation("Found {Count} existing service locations out of {Total} requested", existingServiceLocations.Count, erpIdsInRequest.Count);

        // Process each item
        for (int i = 0; i < request.Items.Count; i++)
        {
            var item = request.Items[i];
            var rowRef = $"JSON item {i + 1}";

            // Validation
            if (item.ErpId <= 0)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    RowRef = rowRef,
                    Message = "ErpId must be greater than 0"
                });
                result.FailedItems.Add(new BulkServiceLocationFailedItem { RowRef = rowRef, Item = item });
                result.Skipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Name))
            {
                result.Errors.Add(new BulkErrorDto
                {
                    RowRef = rowRef,
                    Message = "Name is required"
                });
                result.FailedItems.Add(new BulkServiceLocationFailedItem { RowRef = rowRef, Item = item });
                result.Skipped++;
                continue;
            }

            if (item.Latitude == 0)
            {
                item.Latitude = null;
            }
            if (item.Longitude == 0)
            {
                item.Longitude = null;
            }

            var hasAddress = !string.IsNullOrWhiteSpace(item.Address);
            var hasLatitude = item.Latitude.HasValue;
            var hasLongitude = item.Longitude.HasValue;
            if (hasLatitude != hasLongitude)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    RowRef = rowRef,
                    Message = "Provide both Latitude and Longitude, or leave both empty"
                });
                result.FailedItems.Add(new BulkServiceLocationFailedItem { RowRef = rowRef, Item = item });
                result.Skipped++;
                continue;
            }

            if (!hasAddress && !hasLatitude)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    RowRef = rowRef,
                    Message = "Address or Latitude/Longitude is required"
                });
                result.FailedItems.Add(new BulkServiceLocationFailedItem { RowRef = rowRef, Item = item });
                result.Skipped++;
                continue;
            }

            if (!hasLatitude && hasAddress)
            {
                var geocode = await _geocodingService.GeocodeAddressAsync(item.Address!, cancellationToken);
                if (geocode == null)
                {
                    result.Errors.Add(new BulkErrorDto
                    {
                        RowRef = rowRef,
                        Message = "Unable to resolve Latitude/Longitude from Address"
                    });
                    result.FailedItems.Add(new BulkServiceLocationFailedItem { RowRef = rowRef, Item = item });
                    result.Skipped++;
                    continue;
                }
                item.Latitude = geocode.Latitude;
                item.Longitude = geocode.Longitude;
                hasLatitude = true;
                hasLongitude = true;
            }
            else if (!hasAddress && hasLatitude)
            {
                var reverseAddress = await _geocodingService.ReverseGeocodeAsync(item.Latitude!.Value, item.Longitude!.Value, cancellationToken);
                if (string.IsNullOrWhiteSpace(reverseAddress))
                {
                    result.Errors.Add(new BulkErrorDto
                    {
                        RowRef = rowRef,
                        Message = "Unable to resolve Address from Latitude/Longitude"
                    });
                    result.FailedItems.Add(new BulkServiceLocationFailedItem { RowRef = rowRef, Item = item });
                    result.Skipped++;
                    continue;
                }
                item.Address = reverseAddress;
                hasAddress = true;
            }

            if (!hasLatitude || !hasLongitude)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    RowRef = rowRef,
                    Message = "Latitude and Longitude are required after geocoding"
                });
                result.FailedItems.Add(new BulkServiceLocationFailedItem { RowRef = rowRef, Item = item });
                result.Skipped++;
                continue;
            }

            var latitude = item.Latitude!.Value;
            var longitude = item.Longitude!.Value;

            if (latitude < -90 || latitude > 90)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    RowRef = rowRef,
                    Message = "Latitude must be between -90 and 90"
                });
                result.FailedItems.Add(new BulkServiceLocationFailedItem { RowRef = rowRef, Item = item });
                result.Skipped++;
                continue;
            }

            if (longitude < -180 || longitude > 180)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    RowRef = rowRef,
                    Message = "Longitude must be between -180 and 180"
                });
                result.FailedItems.Add(new BulkServiceLocationFailedItem { RowRef = rowRef, Item = item });
                result.Skipped++;
                continue;
            }

            if (item.ServiceMinutes.HasValue && (item.ServiceMinutes < 1 || item.ServiceMinutes > 240))
            {
                result.Errors.Add(new BulkErrorDto
                {
                    RowRef = rowRef,
                    Message = "ServiceMinutes must be between 1 and 240"
                });
                result.FailedItems.Add(new BulkServiceLocationFailedItem { RowRef = rowRef, Item = item });
                result.Skipped++;
                continue;
            }

            // Check if ERP ID already exists - update instead of skip
            if (existingServiceLocations.TryGetValue(item.ErpId, out var existingServiceLocation))
            {
                var newDueDate = item.DueDate.ToDateTime(TimeOnly.MinValue).Date;
                var dueDateChanged = existingServiceLocation.DueDate.Date != newDueDate;
                var priorityDateProvided = item.PriorityDate.HasValue;
                var newPriorityDate = item.PriorityDate?.ToDateTime(TimeOnly.MinValue).Date;
                var priorityDateChanged = priorityDateProvided
                    && (existingServiceLocation.PriorityDate?.Date != newPriorityDate);

                // Update existing service location with fields from the request.
                existingServiceLocation.Name = item.Name.Trim();
                if (!string.IsNullOrWhiteSpace(item.Address))
                {
                    existingServiceLocation.Address = item.Address.Trim();
                }
                existingServiceLocation.Latitude = latitude;
                existingServiceLocation.Longitude = longitude;
                existingServiceLocation.DueDate = newDueDate;
                if (priorityDateProvided)
                {
                    existingServiceLocation.PriorityDate = newPriorityDate;
                }
                if (item.ServiceMinutes.HasValue)
                {
                    existingServiceLocation.ServiceMinutes = item.ServiceMinutes.Value;
                }
                existingServiceLocation.ServiceTypeId = request.ServiceTypeId; // Update ServiceTypeId from request
                existingServiceLocation.OwnerId = request.OwnerId; // Update OwnerId from request
                if (!string.IsNullOrWhiteSpace(item.DriverInstruction))
                {
                    existingServiceLocation.DriverInstruction = item.DriverInstruction.Trim();
                }
                if (existingServiceLocation.Status == ServiceLocationStatus.Done && (dueDateChanged || priorityDateChanged))
                {
                    existingServiceLocation.Status = ServiceLocationStatus.Open;
                }
                existingServiceLocation.UpdatedAtUtc = now;
                
                _dbContext.ServiceLocations.Update(existingServiceLocation);
                result.Updated++;
                continue;
            }

            // Create new ServiceLocation
            var serviceLocation = new ServiceLocation
            {
                ToolId = Guid.NewGuid(),
                ErpId = item.ErpId,
                Name = item.Name.Trim(),
                Address = item.Address?.Trim(),
                Latitude = latitude,
                Longitude = longitude,
                DueDate = item.DueDate.ToDateTime(TimeOnly.MinValue).Date,
                PriorityDate = item.PriorityDate?.ToDateTime(TimeOnly.MinValue).Date,
                ServiceMinutes = item.ServiceMinutes ?? 20,
                ServiceTypeId = request.ServiceTypeId, // Set ServiceTypeId from request
                OwnerId = request.OwnerId, // Set OwnerId from request
                DriverInstruction = string.IsNullOrWhiteSpace(item.DriverInstruction) ? null : item.DriverInstruction.Trim(),
                Status = ServiceLocationStatus.Open,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            _dbContext.ServiceLocations.Add(serviceLocation);
            result.Inserted++;
        }

        // Save all changes at once
        if (result.Inserted > 0 || result.Updated > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Inserted {InsertedCount} and updated {UpdatedCount} service locations", result.Inserted, result.Updated);
        }

        return result;
    }
}

