namespace TransportPlanner.Infrastructure.Services.Vrp;

public interface IVrpInputBuilder
{
    Task<VrpInputBuildResult> BuildAsync(VrpSolveRequest request, CancellationToken cancellationToken);
}
