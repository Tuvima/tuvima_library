namespace MediaEngine.Domain.Configuration;

/// <summary>
/// One internally consistent, immutable-by-convention view of the configuration
/// used by an ingestion or identity operation.
/// </summary>
public sealed record PipelineExecutionSnapshot(
    long Revision,
    DateTimeOffset LoadedAt,
    CoreConfiguration Core,
    HydrationSettings Hydration,
    PipelineConfiguration Pipelines,
    IReadOnlyList<ProviderConfiguration> Providers);
