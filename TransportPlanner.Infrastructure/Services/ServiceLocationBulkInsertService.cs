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
        var openStatusLocationIds = new List<int>();
        var geocodeCache = new Dictionary<string, GeocodeResult?>(StringComparer.OrdinalIgnoreCase);
        var reverseGeocodeCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

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

        if (serviceType.OwnerId != request.OwnerId)
        {
            result.Errors.Add(new BulkErrorDto
            {
                RowRef = "Request",
                Message = $"ServiceTypeId {request.ServiceTypeId} does not belong to OwnerId {request.OwnerId}"
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

        var existingLocationIds = existingServiceLocations.Values.Select(sl => sl.Id).ToList();
        var constraintsByLocationId = existingLocationIds.Count == 0
            ? new Dictionary<int, ServiceLocationConstraint>()
            : await _dbContext.ServiceLocationConstraints
                .Where(c => existingLocationIds.Contains(c.ServiceLocationId))
                .ToDictionaryAsync(c => c.ServiceLocationId, cancellationToken);

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
                var normalizedAddress = NormalizeAddress(item.Address);
                if (string.IsNullOrWhiteSpace(normalizedAddress))
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

                item.Address = normalizedAddress;
                if (!geocodeCache.TryGetValue(normalizedAddress, out var geocode))
                {
                    geocode = await _geocodingService.GeocodeAddressAsync(normalizedAddress, cancellationToken);
                    geocodeCache[normalizedAddress] = geocode;
                }
                if (geocode == null)
                {
                    result.Errors.Add(new BulkErrorDto
                    {
                        RowRef = rowRef,
                        Message = $"Unable to resolve Latitude/Longitude from Address: {item.Address}"
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
                var reverseKey = $"{item.Latitude!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},{item.Longitude!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                if (!reverseGeocodeCache.TryGetValue(reverseKey, out var reverseAddress))
                {
                    reverseAddress = await _geocodingService.ReverseGeocodeAsync(item.Latitude!.Value, item.Longitude!.Value, cancellationToken);
                    reverseGeocodeCache[reverseKey] = reverseAddress;
                }
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

            if (item.MinVisitDurationMinutes.HasValue && item.MinVisitDurationMinutes.Value < 0)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    RowRef = rowRef,
                    Message = "MinVisitDurationMinutes must be >= 0"
                });
                result.FailedItems.Add(new BulkServiceLocationFailedItem { RowRef = rowRef, Item = item });
                result.Skipped++;
                continue;
            }

            if (item.MaxVisitDurationMinutes.HasValue && item.MaxVisitDurationMinutes.Value < 0)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    RowRef = rowRef,
                    Message = "MaxVisitDurationMinutes must be >= 0"
                });
                result.FailedItems.Add(new BulkServiceLocationFailedItem { RowRef = rowRef, Item = item });
                result.Skipped++;
                continue;
            }

            if (item.MinVisitDurationMinutes.HasValue
                && item.MaxVisitDurationMinutes.HasValue
                && item.MinVisitDurationMinutes.Value > item.MaxVisitDurationMinutes.Value)
            {
                result.Errors.Add(new BulkErrorDto
                {
                    RowRef = rowRef,
                    Message = "MinVisitDurationMinutes must be <= MaxVisitDurationMinutes"
                });
                result.FailedItems.Add(new BulkServiceLocationFailedItem { RowRef = rowRef, Item = item });
                result.Skipped++;
                continue;
            }

            List<ServiceLocationOpeningHours>? normalizedHours = null;
            if (item.OpeningHours != null)
            {
                if (!TryNormalizeOpeningHours(item.OpeningHours, out normalizedHours, out var errorMessage))
                {
                    result.Errors.Add(new BulkErrorDto
                    {
                        RowRef = rowRef,
                        Message = errorMessage
                    });
                    result.FailedItems.Add(new BulkServiceLocationFailedItem { RowRef = rowRef, Item = item });
                    result.Skipped++;
                    continue;
                }
            }

            List<ServiceLocationException>? normalizedExceptions = null;
            if (item.Exceptions != null)
            {
                if (!TryNormalizeExceptions(item.Exceptions, out normalizedExceptions, out var errorMessage))
                {
                    result.Errors.Add(new BulkErrorDto
                    {
                        RowRef = rowRef,
                        Message = errorMessage
                    });
                    result.FailedItems.Add(new BulkServiceLocationFailedItem { RowRef = rowRef, Item = item });
                    result.Skipped++;
                    continue;
                }
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
                if (!string.IsNullOrWhiteSpace(item.AccountId))
                {
                    existingServiceLocation.AccountId = item.AccountId.Trim();
                }
                if (!string.IsNullOrWhiteSpace(item.SerialNumber))
                {
                    existingServiceLocation.SerialNumber = item.SerialNumber.Trim();
                }
                if (!string.IsNullOrWhiteSpace(item.DriverInstruction))
                {
                    existingServiceLocation.DriverInstruction = item.DriverInstruction.Trim();
                }
                if (item.ExtraInstructions != null)
                {
                    existingServiceLocation.ExtraInstructions = NormalizeInstructions(item.ExtraInstructions);
                }
                if (existingServiceLocation.Status == ServiceLocationStatus.Done && (dueDateChanged || priorityDateChanged))
                {
                    existingServiceLocation.Status = ServiceLocationStatus.Open;
                    openStatusLocationIds.Add(existingServiceLocation.Id);
                }
                existingServiceLocation.UpdatedAtUtc = now;

                if (item.MinVisitDurationMinutes.HasValue || item.MaxVisitDurationMinutes.HasValue)
                {
                    if (!constraintsByLocationId.TryGetValue(existingServiceLocation.Id, out var constraint))
                    {
                        constraint = new ServiceLocationConstraint
                        {
                            ServiceLocationId = existingServiceLocation.Id
                        };
                        _dbContext.ServiceLocationConstraints.Add(constraint);
                        constraintsByLocationId[existingServiceLocation.Id] = constraint;
                    }

                    if (item.MinVisitDurationMinutes.HasValue)
                    {
                        constraint.MinVisitDurationMinutes = item.MinVisitDurationMinutes.Value;
                    }

                    if (item.MaxVisitDurationMinutes.HasValue)
                    {
                        constraint.MaxVisitDurationMinutes = item.MaxVisitDurationMinutes.Value;
                    }
                }

                if (normalizedHours != null)
                {
                    foreach (var hour in normalizedHours)
                    {
                        hour.ServiceLocationId = existingServiceLocation.Id;
                    }

                    await _dbContext.ServiceLocationOpeningHours
                        .Where(x => x.ServiceLocationId == existingServiceLocation.Id)
                        .ExecuteDeleteAsync(cancellationToken);

                    if (normalizedHours.Count > 0)
                    {
                        _dbContext.ServiceLocationOpeningHours.AddRange(normalizedHours);
                    }
                }

                if (normalizedExceptions != null)
                {
                    foreach (var exception in normalizedExceptions)
                    {
                        exception.ServiceLocationId = existingServiceLocation.Id;
                    }

                    await _dbContext.ServiceLocationExceptions
                        .Where(x => x.ServiceLocationId == existingServiceLocation.Id)
                        .ExecuteDeleteAsync(cancellationToken);

                    if (normalizedExceptions.Count > 0)
                    {
                        _dbContext.ServiceLocationExceptions.AddRange(normalizedExceptions);
                    }
                }
                
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
                AccountId = string.IsNullOrWhiteSpace(item.AccountId) ? null : item.AccountId.Trim(),
                SerialNumber = string.IsNullOrWhiteSpace(item.SerialNumber) ? null : item.SerialNumber.Trim(),
                DriverInstruction = string.IsNullOrWhiteSpace(item.DriverInstruction) ? null : item.DriverInstruction.Trim(),
                ExtraInstructions = NormalizeInstructions(item.ExtraInstructions),
                Status = ServiceLocationStatus.Open,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            _dbContext.ServiceLocations.Add(serviceLocation);

            if (item.MinVisitDurationMinutes.HasValue || item.MaxVisitDurationMinutes.HasValue)
            {
                var constraint = new ServiceLocationConstraint
                {
                    ServiceLocation = serviceLocation,
                    MinVisitDurationMinutes = item.MinVisitDurationMinutes,
                    MaxVisitDurationMinutes = item.MaxVisitDurationMinutes
                };
                _dbContext.ServiceLocationConstraints.Add(constraint);
            }

            if (normalizedHours != null)
            {
                foreach (var hour in normalizedHours)
                {
                    hour.ServiceLocation = serviceLocation;
                }

                if (normalizedHours.Count > 0)
                {
                    _dbContext.ServiceLocationOpeningHours.AddRange(normalizedHours);
                }
            }

            if (normalizedExceptions != null)
            {
                foreach (var exception in normalizedExceptions)
                {
                    exception.ServiceLocation = serviceLocation;
                }

                if (normalizedExceptions.Count > 0)
                {
                    _dbContext.ServiceLocationExceptions.AddRange(normalizedExceptions);
                }
            }
            result.Inserted++;
        }

        // Save all changes at once
        if (result.Inserted > 0 || result.Updated > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (openStatusLocationIds.Count > 0)
            {
                await RemoveFromFutureRoutesAsync(openStatusLocationIds, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            _logger.LogInformation("Inserted {InsertedCount} and updated {UpdatedCount} service locations", result.Inserted, result.Updated);
        }

        return result;
    }

    private static string? NormalizeAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        return address
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
    }

    private static TimeSpan? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return TimeSpan.TryParseExact(value, "hh\\:mm", null, out var parsed)
            ? parsed
            : null;
    }

    private static bool TryNormalizeOpeningHours(
        IEnumerable<ServiceLocationOpeningHoursDto> items,
        out List<ServiceLocationOpeningHours> normalized,
        out string errorMessage)
    {
        normalized = new List<ServiceLocationOpeningHours>();
        errorMessage = string.Empty;

        var seenDays = new HashSet<int>();
        foreach (var item in items)
        {
            if (item.DayOfWeek < 0 || item.DayOfWeek > 6)
            {
                errorMessage = "DayOfWeek must be between 0 and 6.";
                return false;
            }

            if (!seenDays.Add(item.DayOfWeek))
            {
                errorMessage = "Duplicate opening hours for the same day.";
                return false;
            }

            var openTime = ParseTime(item.OpenTime);
            var closeTime = ParseTime(item.CloseTime);
            var openTime2 = ParseTime(item.OpenTime2);
            var closeTime2 = ParseTime(item.CloseTime2);

            if (!item.IsClosed && (!openTime.HasValue || !closeTime.HasValue))
            {
                errorMessage = "OpenTime and CloseTime are required when not closed.";
                return false;
            }

            if (openTime.HasValue && closeTime.HasValue && openTime.Value >= closeTime.Value)
            {
                errorMessage = "OpenTime must be before CloseTime.";
                return false;
            }

            if (!item.IsClosed && (openTime2.HasValue ^ closeTime2.HasValue))
            {
                errorMessage = "OpenTime2 and CloseTime2 are required together when using a lunch break.";
                return false;
            }

            if (!item.IsClosed && openTime2.HasValue && closeTime2.HasValue && openTime2.Value >= closeTime2.Value)
            {
                errorMessage = "OpenTime2 must be before CloseTime2.";
                return false;
            }

            if (!item.IsClosed && openTime.HasValue && closeTime.HasValue && openTime2.HasValue && closeTime2.HasValue
                && closeTime.Value > openTime2.Value)
            {
                errorMessage = "CloseTime must be before OpenTime2 when using a lunch break.";
                return false;
            }

            normalized.Add(new ServiceLocationOpeningHours
            {
                DayOfWeek = item.DayOfWeek,
                OpenTime = item.IsClosed ? null : openTime,
                CloseTime = item.IsClosed ? null : closeTime,
                OpenTime2 = item.IsClosed ? null : openTime2,
                CloseTime2 = item.IsClosed ? null : closeTime2,
                IsClosed = item.IsClosed
            });
        }

        return true;
    }

    private static bool TryNormalizeExceptions(
        IEnumerable<ServiceLocationExceptionDto> items,
        out List<ServiceLocationException> normalized,
        out string errorMessage)
    {
        normalized = new List<ServiceLocationException>();
        errorMessage = string.Empty;

        foreach (var item in items)
        {
            var openTime = ParseTime(item.OpenTime);
            var closeTime = ParseTime(item.CloseTime);

            if (!item.IsClosed && (!openTime.HasValue || !closeTime.HasValue))
            {
                errorMessage = "OpenTime and CloseTime are required when not closed.";
                return false;
            }

            if (openTime.HasValue && closeTime.HasValue && openTime.Value >= closeTime.Value)
            {
                errorMessage = "OpenTime must be before CloseTime.";
                return false;
            }

            normalized.Add(new ServiceLocationException
            {
                Date = item.Date.Date,
                OpenTime = item.IsClosed ? null : openTime,
                CloseTime = item.IsClosed ? null : closeTime,
                IsClosed = item.IsClosed,
                Note = string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Trim()
            });
        }

        return true;
    }

    private static List<string> NormalizeInstructions(IEnumerable<string>? instructions)
    {
        if (instructions == null)
        {
            return new List<string>();
        }

        return instructions
            .Select(line => line?.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Cast<string>()
            .ToList();
    }

    private async Task RemoveFromFutureRoutesAsync(List<int> serviceLocationIds, CancellationToken cancellationToken)
    {
        var today = DateTime.Today;
        var affectedRouteIds = await _dbContext.RouteStops
            .Where(rs => rs.ServiceLocationId.HasValue
                && serviceLocationIds.Contains(rs.ServiceLocationId.Value)
                && rs.Route.Date.Date >= today)
            .Select(rs => rs.RouteId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (affectedRouteIds.Count == 0)
        {
            return;
        }

        await _dbContext.RouteStops
            .Where(rs => rs.ServiceLocationId.HasValue
                && serviceLocationIds.Contains(rs.ServiceLocationId.Value)
                && rs.Route.Date.Date >= today)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var routeId in affectedRouteIds)
        {
            var remainingCount = await _dbContext.RouteStops
                .Where(rs => rs.RouteId == routeId)
                .CountAsync(cancellationToken);

            if (remainingCount == 0)
            {
                var route = await _dbContext.Routes.FirstOrDefaultAsync(r => r.Id == routeId, cancellationToken);
                if (route != null)
                {
                    _dbContext.Routes.Remove(route);
                }
            }
        }
    }
}

