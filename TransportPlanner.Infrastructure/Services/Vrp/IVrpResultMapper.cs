using Google.OrTools.ConstraintSolver;

namespace TransportPlanner.Infrastructure.Services.Vrp;

public interface IVrpResultMapper
{
    VrpSolution MapSolution(
        VrpInput input,
        RoutingModel routing,
        RoutingIndexManager manager,
        Assignment solution,
        MatrixResult matrix);
}
