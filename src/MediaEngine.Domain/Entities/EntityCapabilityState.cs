namespace MediaEngine.Domain.Entities;

public sealed class EntityCapabilityState
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EntityId { get; init; }
    public string EntityKind { get; init; } = "";
    public string? MediaType { get; init; }
    public string CapabilityId { get; init; } = "";
    public string CapabilityKind { get; init; } = "";
    public string? CapabilityVersion { get; init; }
    public string? SubKey { get; init; }
    public string Status { get; init; } = EntityCapabilityStatus.Pending;
    public string Requiredness { get; init; } = CapabilityRequiredness.Optional;
    public string? Source { get; init; }
    public double? Confidence { get; init; }
    public int ArtifactCount { get; init; }
    public string? ArtifactSummary { get; init; }
    public string? ResultSummary { get; init; }
    public Guid? LastOperationId { get; init; }
    public DateTimeOffset? FirstAttemptedAt { get; init; }
    public DateTimeOffset? LastAttemptedAt { get; init; }
    public DateTimeOffset? SucceededAt { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
    public bool Stale { get; init; }
    public bool NeedsRerun { get; init; }
    public string? MissingReason { get; init; }
    public string? LastError { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CapabilityStateResult(
    string? Source = null,
    double? Confidence = null,
    int ArtifactCount = 0,
    string? ArtifactSummary = null,
    string? ResultSummary = null,
    Guid? OperationId = null);

public static class EntityCapabilityStatus
{
    public const string NotApplicable = "not_applicable";
    public const string Pending = "pending";
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string NoResult = "no_result";
    public const string MissingConfirmed = "missing_confirmed";
    public const string Blocked = "blocked";
    public const string FailedRetryable = "failed_retryable";
    public const string FailedTerminal = "failed_terminal";
    public const string Skipped = "skipped";
    public const string Stale = "stale";
}

public static class CapabilityRequiredness
{
    public const string Required = "required";
    public const string Recommended = "recommended";
    public const string Optional = "optional";
}

public static class CapabilityId
{
    public const string IdentityRetailMatch = "identity.retail_match";
    public const string IdentityWikidataBridge = "identity.wikidata_bridge";
    public const string IdentityQuickHydration = "identity.quick_hydration";
    public const string IdentityMediaTypeClassification = "identity.media_type_classification";
    public const string EnrichmentCoverArt = "enrichment.cover_art";
    public const string EnrichmentPeople = "enrichment.people";
    public const string EnrichmentDescription = "enrichment.description";
    public const string EnrichmentRelationships = "enrichment.relationships";
    public const string TextTrackLyrics = "text_track.lyrics";
    public const string TextTrackSubtitles = "text_track.subtitles";
    public const string PluginCommercialSkip = "plugin.commercial_skip";
    public const string WritebackMetadata = "writeback.metadata";
    public const string AiTldr = "ai.tldr";
    public const string AiVibeTags = "ai.vibe_tags";
    public const string AiSmartLabels = "ai.smart_labels";
}
