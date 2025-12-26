using TransportPlanner.Application.DTOs;

namespace TransportPlanner.Application.Interfaces;

public interface IDriverQueries
{
    Task<List<DriverDto>> GetDriversAsync(CancellationToken cancellationToken = default);
    Task<List<DriverAvailabilityDto>> GetDriverAvailabilityAsync(int driverId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
}

