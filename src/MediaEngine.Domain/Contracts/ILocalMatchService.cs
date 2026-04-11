namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Stage 0: Local Match — checks if a file's metadata matches an entity already
/// in the library before making any external API calls.
///
/// Uses the persistent <c>bridge_ids</c> table and canonical values as a local
/// cache across all batches and sessions. For episodic content (TV,
/// music tracks), a local match means only the episode/track-specific data needs
/// to be fetched externally; the series/album/show metadata is reused.
/// </summary>
public interface ILocalMatchService
{
    /// <summary>
    /// Attempts to match a file's metadata against existing library entities.
    /// </summary>
    /// <param name="hints">File metadata hints (title, author, isbn, asin, etc.).</param>
    /// <param name="mediaType">The file's media type for scoped matching.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A match result containing the existing Work/Hub IDs and bridge IDs if found,
    /// or a not-found result if no local match exists.
    /// </returns>
    Task<LocalMatchResult> TryMatchAsync(
        IReadOnlyDictionary<string, string> hints,
        MediaEngine.Domain.Enums.MediaType mediaType,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a Stage 0 local match attempt.
/// </summary>
public sealed class LocalMatchResult
{
    /// <summary>Whether a local match was found.</summary>
    public bool Found { get; init; }

    /// <summary>The matched entity ID (MediaAsset or Work).</summary>
    public Guid? EntityId { get; init; }

    /// <summary>The matched Work ID.</summary>
    public Guid? WorkId { get; init; }

    /// <summary>The matched Hub ID.</summary>
    public Guid? HubId { get; init; }

    /// <summary>The Wikidata QID of the matched Work (if resolved).</summary>
    public string? WikidataQid { get; init; }

    /// <summary>Which bridge ID type produced the match (e.g. "isbn", "asin").</summary>
    public string? MatchedByIdType { get; init; }

    /// <summary>Whether this is an exact ID match (high confidence) or fuzzy title match.</summary>
    public bool IsExactIdMatch { get; init; }

    /// <summary>Creates a not-found result.</summary>
    public static LocalMatchResult NotFound => new() { Found = false };
}
