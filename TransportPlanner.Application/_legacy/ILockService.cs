namespace TransportPlanner.Application.Interfaces;

public interface ILockService
{
    Task LockDayAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task UnlockDayAsync(DateOnly date, CancellationToken cancellationToken = default);
}

