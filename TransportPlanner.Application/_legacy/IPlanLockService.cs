namespace TransportPlanner.Application.Interfaces;

public interface IPlanLockService
{
    Task LockDayAsync(DateTime date, CancellationToken cancellationToken = default);
    Task UnlockDayAsync(DateTime date, CancellationToken cancellationToken = default);
}

