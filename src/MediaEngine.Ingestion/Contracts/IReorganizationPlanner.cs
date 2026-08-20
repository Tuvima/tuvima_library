using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Contracts;

public interface IReorganizationPlanner
{
    ReorganizationPlan CreatePlan(ReorganizationPlanningRequest request);
}
