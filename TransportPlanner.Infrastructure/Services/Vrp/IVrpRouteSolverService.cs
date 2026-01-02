namespace TransportPlanner.Infrastructure.Services.Vrp;

public interface IVrpRouteSolverService
{
    Task<VrpSolveResult> SolveDayAsync(VrpSolveRequest request, CancellationToken cancellationToken);
}
