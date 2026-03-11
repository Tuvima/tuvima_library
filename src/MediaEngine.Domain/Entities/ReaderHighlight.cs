namespace MediaEngine.Domain.Entities;

/// <summary>
/// A text highlight with optional note in an EPUB book.
/// </summary>
public sealed class ReaderHighlight
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid AssetId { get; set; }
    public int ChapterIndex { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }

    /// <summary>The selected text that was highlighted.</summary>
    public string SelectedText { get; set; } = string.Empty;

    /// <summary>Highlight colour as a CSS hex code (e.g. "#EAB308").</summary>
    public string Color { get; set; } = MediaEngine.Domain.Enums.HighlightColor.Yellow;

    /// <summary>Optional user note attached to this highlight.</summary>
    public string? NoteText { get; set; }

    public DateTime CreatedAt { get; set; }
}
