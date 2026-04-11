using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Intelligence.Strategies;

/// <summary>
/// Identity strategy for TV (episodic television, web series).
///
/// TV identification is show-first: TMDB and TVDB are both valid bridge
/// sources. CriticalFields reflect the episode-level specificity required
/// for correct matching — show name, season, and episode number together
/// identify a unique episode. Text fallback on show name alone is permitted
/// without a creator because TV shows are unambiguously identified by title.
/// </summary>
public sealed class TvIdentityStrategy : IMediaTypeIdentityStrategy
{
    /// <inheritdoc/>
    public MediaType MediaType => MediaType.TV;

    /// <inheritdoc/>
    public IReadOnlyList<string> PreferredBridgeIds { get; } =
        ["tmdb_id", "tvdb_id"];

    /// <inheritdoc/>
    public IReadOnlyList<string> CriticalFields { get; } =
        ["show_name", "season_number", "episode_number"];

    /// <inheritdoc/>
    public bool AllowsTextFallback => true;

    /// <inheritdoc/>
    public double TextFallbackMinConfidence => ConfidenceBand.StrongFloor;

    /// <inheritdoc/>
    public bool RequiresCreatorForFallback => false;
}
