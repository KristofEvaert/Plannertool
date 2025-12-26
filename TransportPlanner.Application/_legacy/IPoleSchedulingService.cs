namespace TransportPlanner.Application.Interfaces;

public interface IPoleSchedulingService
{
    Task SetFixedDateAsync(int poleId, DateTime fixedDate, CancellationToken cancellationToken = default);
    Task ClearFixedDateAsync(int poleId, CancellationToken cancellationToken = default);
}

