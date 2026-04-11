using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Per-media-type identity rules. Extracted from scattered if/else
/// branches in workers into declarative strategy objects.
///
/// One implementation exists per <see cref="MediaType"/> value.
/// Implementations live in <c>MediaEngine.Intelligence</c> and are
/// registered per-type so callers can resolve the correct strategy via
/// <c>IEnumerable&lt;IMediaTypeIdentityStrategy&gt;</c> injection.
/// </summary>
public interface IMediaTypeIdentityStrategy
{
    /// <summary>The media type this strategy applies to.</summary>
    MediaType MediaType { get; }

    /// <summary>
    /// Preferred bridge ID keys for this media type, in priority order.
    /// Examples: ["isbn", "asin"] for Books, ["tmdb_id", "imdb_id"] for Movies.
    /// The pipeline tries each key in order when building the Stage 2 request.
    /// </summary>
    IReadOnlyList<string> PreferredBridgeIds { get; }

    /// <summary>
    /// Metadata fields that are critical for retail scoring.
    /// When a critical field is missing from either the file or the candidate,
    /// the scoring service may redistribute weights or apply a penalty.
    /// Example: ["author"] for Books, ["artist"] for Music.
    /// </summary>
    IReadOnlyList<string> CriticalFields { get; }

    /// <summary>
    /// Whether text-only Wikidata reconciliation (CirrusSearch) is allowed
    /// as a fallback when no bridge IDs are available.
    /// False for media types where text-only matching is too ambiguous
    /// (e.g. Music, where track titles collide with movie titles).
    /// </summary>
    bool AllowsTextFallback { get; }

    /// <summary>
    /// Minimum composite confidence score from the best retail candidate
    /// required before a text-only Wikidata fallback is attempted (0.0–1.0).
    /// Prevents low-quality retail matches from triggering expensive text
    /// reconciliation that is unlikely to produce a valid QID.
    /// Ignored when <see cref="AllowsTextFallback"/> is <c>false</c>.
    /// </summary>
    double TextFallbackMinConfidence { get; }

    /// <summary>
    /// Whether a creator field (author, artist, director) must be present
    /// in the file metadata before text-only Wikidata fallback is attempted.
    /// When <c>true</c> and no creator is found, text fallback is skipped
    /// and the item routes to review instead.
    /// Ignored when <see cref="AllowsTextFallback"/> is <c>false</c>.
    /// </summary>
    bool RequiresCreatorForFallback { get; }
}
