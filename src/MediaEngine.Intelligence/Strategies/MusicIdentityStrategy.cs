using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Intelligence.Strategies;

/// <summary>
/// Identity strategy for Music (albums, singles, tracks).
///
/// Music text-only Wikidata reconciliation is disabled. Track and song
/// titles are highly ambiguous — they routinely collide with movie titles,
/// album names, and other works in CirrusSearch. The Apple API (sole Stage 1
/// provider) reliably returns apple_music_id / apple_music_collection_id
/// bridge IDs, and MusicBrainz ISRC is available when present in file tags.
/// If no bridge ID is obtained from Stage 1, the item routes to review
/// rather than attempting a text search.
/// </summary>
public sealed class MusicIdentityStrategy : IMediaTypeIdentityStrategy
{
    /// <inheritdoc/>
    public MediaType MediaType => MediaType.Music;

    /// <inheritdoc/>
    public IReadOnlyList<string> PreferredBridgeIds { get; } =
        ["apple_music_id", "apple_music_collection_id", "musicbrainz_id"];

    /// <inheritdoc/>
    public IReadOnlyList<string> CriticalFields { get; } =
        ["artist", "album", "title"];

    /// <inheritdoc/>
    public bool AllowsTextFallback => false;

    /// <inheritdoc/>
    public double TextFallbackMinConfidence => ConfidenceBand.StrongFloor;

    /// <inheritdoc/>
    public bool RequiresCreatorForFallback => false;
}
