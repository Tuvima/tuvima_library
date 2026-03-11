namespace MediaEngine.Domain.Entities;

/// <summary>
/// A bookmark placed by the user at a specific position in an EPUB book.
/// </summary>
public sealed class ReaderBookmark
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid AssetId { get; set; }
    public int ChapterIndex { get; set; }

    /// <summary>
    /// JSON-encoded CFI (Canonical Fragment Identifier) or page position
    /// within the chapter for precise location restoration.
    /// </summary>
    public string? CfiPosition { get; set; }

    /// <summary>User-supplied label for this bookmark (e.g. "Important passage").</summary>
    public string? Label { get; set; }

    public DateTime CreatedAt { get; set; }
}
