using TransportPlanner.Application.Interfaces;

namespace TransportPlanner.Application.Services;

public class LockService : ILockService
{
    public async Task LockDayAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        // TODO: Implement day locking logic
        await Task.CompletedTask;
    }

    public async Task UnlockDayAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        // TODO: Implement day unlocking logic
        await Task.CompletedTask;
    }
}

