namespace MediaEngine.Domain.Models;

/// <summary>
/// A single alignment segment mapping ebook text to audiobook audio position.
/// Produced by the WhisperSync alignment pipeline.
/// </summary>
public sealed record AlignmentSegment(
    int ChapterIndex,
    int TextStartOffset,
    int TextEndOffset,
    double AudioStartSeconds,
    double AudioEndSeconds,
    string Text);
