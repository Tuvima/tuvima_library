namespace MediaEngine.Application.ReadModels;

public sealed record LibraryOverviewReadModel(
    int Added24h,
    int Added7d,
    int Added30d,
    IReadOnlyDictionary<string, int> PipelineStates,
    double PipelineSuccessRate);

public sealed class UniverseCandidateReadModel
{
    public Guid WorkId { get; init; }
    public Guid EntityId { get; init; }
    public string Title { get; init; } = "";
    public string MediaType { get; init; } = "";
    public string CandidateQid { get; init; } = "";
    public string CandidateType { get; init; } = "";
    public string CandidateLabel { get; init; } = "";
}
