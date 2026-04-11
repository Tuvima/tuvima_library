using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Intelligence.Strategies;

/// <summary>
/// Identity strategy for Audiobooks (M4B, MP3 audiobooks).
///
/// Audiobooks carry multiple bridge candidates: ISBN from the source work,
/// ASIN from Audible/retail, and Apple Music identifiers when the Apple API
/// is the Stage 1 provider. Text fallback requires an author because
/// audiobook titles frequently share names with their print editions.
/// </summary>
public sealed class AudiobookIdentityStrategy : IMediaTypeIdentityStrategy
{
    /// <inheritdoc/>
    public MediaType MediaType => MediaType.Audiobooks;

    /// <inheritdoc/>
    public IReadOnlyList<string> PreferredBridgeIds { get; } =
        ["isbn", "asin", "apple_music_id"];

    /// <inheritdoc/>
    public IReadOnlyList<string> CriticalFields { get; } =
        ["author", "narrator", "title"];

    /// <inheritdoc/>
    public bool AllowsTextFallback => true;

    /// <inheritdoc/>
    public double TextFallbackMinConfidence => ConfidenceBand.StrongFloor;

    /// <inheritdoc/>
    public bool RequiresCreatorForFallback => true;
}
