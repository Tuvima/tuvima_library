namespace MediaEngine.Contracts.Metadata;

/// <summary>
/// Named wire responses for deferred Pass 2 enrichment. Member names preserve the
/// lower_snake_case JSON shape of the anonymous objects replaced.
/// </summary>
public sealed record DeferredEnrichmentTriggerResponse(int pending_count, string message);

public sealed record DeferredEnrichmentStatusResponse(int pending_count, bool two_pass_enabled);
