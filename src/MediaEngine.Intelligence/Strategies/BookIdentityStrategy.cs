using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Intelligence.Strategies;

/// <summary>
/// Identity strategy for Books (EPUB, PDF ebooks).
///
/// Books are identified primarily by ISBN, with Open Library as a secondary
/// bridge. Text-only Wikidata fallback is permitted but requires an author
/// to be present — a bare title is too ambiguous across editions and formats.
/// </summary>
public sealed class BookIdentityStrategy : IMediaTypeIdentityStrategy
{
    /// <inheritdoc/>
    public MediaType MediaType => MediaType.Books;

    /// <inheritdoc/>
    public IReadOnlyList<string> PreferredBridgeIds { get; } =
        ["isbn", "open_library_id"];

    /// <inheritdoc/>
    public IReadOnlyList<string> CriticalFields { get; } =
        ["author", "title"];

    /// <inheritdoc/>
    public bool AllowsTextFallback => true;

    /// <inheritdoc/>
    public double TextFallbackMinConfidence => ConfidenceBand.StrongFloor;

    /// <inheritdoc/>
    public bool RequiresCreatorForFallback => true;
}
