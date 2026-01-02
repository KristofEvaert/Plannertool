namespace TransportPlanner.Infrastructure.Services.Vrp;

public interface IMatrixProvider
{
    Task<MatrixResult> GetMatrixAsync(string cacheKey, IReadOnlyList<MatrixPoint> points, CancellationToken cancellationToken);
}
