using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Capabilities;

public sealed record CapabilityDefinition(
    string Id,
    string Kind,
    string CurrentVersion,
    IReadOnlySet<string> EntityKinds,
    IReadOnlySet<string> MediaTypes,
    string DefaultRequiredness,
    string Policy,
    bool RerunOnVersionChange,
    ReviewPolicy ReviewPolicy);

public sealed record ReviewPolicy(
    bool CreateReviewOnNoResult,
    bool CreateReviewOnMissingConfirmed,
    bool CreateReviewOnBlocked,
    bool CreateReviewOnTerminalFailure,
    double? ReviewBelowConfidence,
    bool OptionalNoResultCreatesReview = false);

public sealed class CapabilityRegistry
{
    private readonly IReadOnlyDictionary<string, CapabilityDefinition> _definitions;

    public CapabilityRegistry()
    {
        var all = new[]
        {
            Required(CapabilityId.IdentityMediaTypeClassification, "identity", noResultReview: true),
            Required(CapabilityId.IdentityRetailMatch, "identity", noResultReview: true),
            Optional(CapabilityId.IdentityWikidataBridge, "identity", noResultReview: false),
            Optional(CapabilityId.IdentityQuickHydration, "identity"),
            Recommended(CapabilityId.EnrichmentCoverArt, "enrichment", missingReview: false),
            Optional(CapabilityId.EnrichmentPeople, "enrichment"),
            Optional(CapabilityId.EnrichmentDescription, "enrichment"),
            Optional(CapabilityId.EnrichmentRelationships, "enrichment"),
            Optional(CapabilityId.TextTrackLyrics, "text_track"),
            Optional(CapabilityId.TextTrackSubtitles, "text_track"),
            Optional(CapabilityId.PluginCommercialSkip, "plugin"),
            Optional(CapabilityId.WritebackMetadata, "writeback", terminalReview: true),
            Optional(CapabilityId.AiTldr, "ai"),
            Optional(CapabilityId.AiVibeTags, "ai"),
            Optional(CapabilityId.AiSmartLabels, "ai")
        };

        _definitions = all.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<CapabilityDefinition> All => _definitions.Values.ToArray();

    public CapabilityDefinition? Find(string capabilityId)
        => _definitions.TryGetValue(capabilityId, out var definition) ? definition : null;

    private static CapabilityDefinition Optional(
        string id,
        string kind,
        bool noResultReview = false,
        bool missingReview = false,
        bool blockedReview = true,
        bool terminalReview = false)
        => Definition(id, kind, CapabilityRequiredness.Optional, noResultReview, missingReview, blockedReview, terminalReview);

    private static CapabilityDefinition Recommended(
        string id,
        string kind,
        bool noResultReview = false,
        bool missingReview = false,
        bool blockedReview = true,
        bool terminalReview = false)
        => Definition(id, kind, CapabilityRequiredness.Recommended, noResultReview, missingReview, blockedReview, terminalReview);

    private static CapabilityDefinition Required(
        string id,
        string kind,
        bool noResultReview = true,
        bool missingReview = true,
        bool blockedReview = true,
        bool terminalReview = true)
        => Definition(id, kind, CapabilityRequiredness.Required, noResultReview, missingReview, blockedReview, terminalReview);

    private static CapabilityDefinition Definition(
        string id,
        string kind,
        string requiredness,
        bool noResultReview,
        bool missingReview,
        bool blockedReview,
        bool terminalReview)
        => new(
            id,
            kind,
            "1.0",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "asset", "work", "edition" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            requiredness,
            "auto",
            RerunOnVersionChange: true,
            new ReviewPolicy(
                noResultReview,
                missingReview,
                blockedReview,
                terminalReview,
                ReviewBelowConfidence: null));
}

public static class ReviewEligibility
{
    public static bool IsReviewEligible(MediaOperation operation) => operation.Status switch
    {
        MediaOperationStatus.Pending => false,
        MediaOperationStatus.Queued => false,
        MediaOperationStatus.Leased => false,
        MediaOperationStatus.Running => false,
        MediaOperationStatus.RetryWaiting => false,
        MediaOperationStatus.FailedRetryable => false,
        MediaOperationStatus.Succeeded => false,
        MediaOperationStatus.Skipped => false,
        MediaOperationStatus.NotApplicable => false,
        MediaOperationStatus.NoResult => false,
        MediaOperationStatus.MissingConfirmed => true,
        MediaOperationStatus.Blocked => true,
        MediaOperationStatus.FailedTerminal => true,
        MediaOperationStatus.DeadLettered => true,
        _ => false
    };

    public static bool IsReviewEligible(EntityCapabilityState state, CapabilityDefinition? definition = null)
    {
        var policy = definition?.ReviewPolicy;
        var optional = string.Equals(state.Requiredness, CapabilityRequiredness.Optional, StringComparison.OrdinalIgnoreCase);

        return state.Status switch
        {
            EntityCapabilityStatus.Pending => false,
            EntityCapabilityStatus.Queued => false,
            EntityCapabilityStatus.Running => false,
            EntityCapabilityStatus.FailedRetryable => false,
            EntityCapabilityStatus.Stale => false,
            EntityCapabilityStatus.Succeeded => false,
            EntityCapabilityStatus.Skipped => false,
            EntityCapabilityStatus.NotApplicable => false,
            EntityCapabilityStatus.NoResult => policy?.CreateReviewOnNoResult == true && (!optional || policy.OptionalNoResultCreatesReview),
            EntityCapabilityStatus.MissingConfirmed => policy?.CreateReviewOnMissingConfirmed == true,
            EntityCapabilityStatus.Blocked => policy?.CreateReviewOnBlocked != false,
            EntityCapabilityStatus.FailedTerminal => policy?.CreateReviewOnTerminalFailure != false,
            _ => false
        };
    }
}
