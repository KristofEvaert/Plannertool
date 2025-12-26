using TransportPlanner.Application.DTOs;

namespace TransportPlanner.Application.Interfaces;

public interface IDriverAdminService
{
    Task<DriverDto> UpdateMaxWorkMinutesAsync(int driverId, int minutes, CancellationToken cancellationToken = default);
}

