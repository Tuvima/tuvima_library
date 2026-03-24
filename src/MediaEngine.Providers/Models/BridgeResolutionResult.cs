namespace MediaEngine.Providers.Models;

/// <summary>
/// Result of resolving retail bridge IDs to Wikidata entities.
/// Produced by <c>ReconciliationAdapter.ResolveBridgeAsync</c> during Stage 2.
/// </summary>
public sealed class BridgeResolutionResult
{
    /// <summary>Whether a Wikidata entity was found for any bridge ID.</summary>
    public bool Found { get; init; }

    /// <summary>The resolved Wikidata QID (edition or work, whichever was found first).</summary>
    public string? Qid { get; init; }

    /// <summary>True if the resolved QID is an edition entity (has P629).</summary>
    public bool IsEdition { get; init; }

    /// <summary>The work QID (from P629 if edition, or the Qid itself if it's a work).</summary>
    public string? WorkQid { get; init; }

    /// <summary>The edition QID (same as Qid when IsEdition is true, null otherwise).</summary>
    public string? EditionQid { get; init; }

    /// <summary>All claims extracted from the entity's properties via Data Extension.</summary>
    public IReadOnlyList<ProviderClaim> Claims { get; init; } = [];

    /// <summary>All bridge IDs collected from the entity (all platform IDs Wikidata knows).</summary>
    public Dictionary<string, string> CollectedBridgeIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a not-found result.</summary>
    public static BridgeResolutionResult NotFound => new() { Found = false };
}
