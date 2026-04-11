using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Intelligence.Strategies;

/// <summary>
/// Identity strategy for Comics (CBZ, CBR, PDF comics, manga).
///
/// ComicVine is the sole Stage 1 provider and returns a comicvine_id bridge
/// when a match is found. CriticalFields are series, issue number, and title
/// — together these uniquely identify a comic issue. Text fallback is
/// permitted on series + issue when no bridge ID is available; no creator
/// requirement since many comics lack a writer tag in embedded metadata.
/// </summary>
public sealed class ComicIdentityStrategy : IMediaTypeIdentityStrategy
{
    /// <inheritdoc/>
    public MediaType MediaType => MediaType.Comics;

    /// <inheritdoc/>
    public IReadOnlyList<string> PreferredBridgeIds { get; } =
        ["comicvine_id"];

    /// <inheritdoc/>
    public IReadOnlyList<string> CriticalFields { get; } =
        ["series", "issue_number", "title"];

    /// <inheritdoc/>
    public bool AllowsTextFallback => true;

    /// <inheritdoc/>
    public double TextFallbackMinConfidence => ConfidenceBand.StrongFloor;

    /// <inheritdoc/>
    public bool RequiresCreatorForFallback => false;
}
