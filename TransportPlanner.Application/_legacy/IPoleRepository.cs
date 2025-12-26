using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Application.Interfaces;

public interface IPoleRepository
{
    Task<Pole?> GetBySerialAsync(string serial, CancellationToken cancellationToken = default);
    Task AddAsync(Pole pole, CancellationToken cancellationToken = default);
    Task UpdateAsync(Pole pole, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

