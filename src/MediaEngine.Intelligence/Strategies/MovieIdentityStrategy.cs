using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Intelligence.Strategies;

/// <summary>
/// Identity strategy for Movies (feature films, short films).
///
/// TMDB is the primary retail provider for movies and reliably produces a
/// bridge ID. Text fallback is permitted for title + year searches — no
/// creator is required because many films are commonly identified by title
/// and release year alone without a known director in the file metadata.
/// </summary>
public sealed class MovieIdentityStrategy : IMediaTypeIdentityStrategy
{
    /// <inheritdoc/>
    public MediaType MediaType => MediaType.Movies;

    /// <inheritdoc/>
    public IReadOnlyList<string> PreferredBridgeIds { get; } =
        ["tmdb_id"];

    /// <inheritdoc/>
    public IReadOnlyList<string> CriticalFields { get; } =
        ["title", "year"];

    /// <inheritdoc/>
    public bool AllowsTextFallback => true;

    /// <inheritdoc/>
    public double TextFallbackMinConfidence => ConfidenceBand.StrongFloor;

    /// <inheritdoc/>
    public bool RequiresCreatorForFallback => false;
}
