namespace MediaEngine.Providers.Models;

/// <summary>
/// Unified Stage 2 resolution result returned by
/// <c>ReconciliationAdapter.ResolveAsync</c> and <c>ResolveBatchAsync</c>.
///
/// <para>
/// The public Stage 2 result type — populated by ReconciliationAdapter via
/// the Tuvima.Wikidata Stage2Service sub-service plus a Data Extension follow-up.
/// Carries the resolved QID, claims fetched via Data Extension, any platform
/// IDs collected from Wikidata, and a tag identifying which strategy matched.
/// </para>
/// </summary>
public sealed class WikidataResolveResult
{
    /// <summary>Whether a Wikidata entity was found.</summary>
    public bool Found { get; init; }

    /// <summary>The resolved QID (edition or work, whichever was found first).</summary>
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
    public IReadOnlyDictionary<string, string> CollectedBridgeIds { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Identifies which resolution strategy actually produced this match.</summary>
    public ResolveStrategy MatchedBy { get; init; } = ResolveStrategy.NotResolved;

    /// <summary>
    /// When matched via <see cref="ResolveStrategy.BridgeId"/>, the bridge ID type
    /// (e.g. <c>tmdb_id</c>, <c>isbn</c>) of the first bridge ID that resolved.
    /// </summary>
    public string? PrimaryBridgeIdType { get; init; }

    /// <summary>Singleton not-found result.</summary>
    public static WikidataResolveResult NotFound { get; } = new()
    {
        Found     = false,
        MatchedBy = ResolveStrategy.NotResolved,
    };
}
