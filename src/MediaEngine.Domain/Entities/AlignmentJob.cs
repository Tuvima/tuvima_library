using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Entities;

/// <summary>
/// A WhisperSync alignment job that maps ebook text to audiobook audio segments.
/// Jobs are created when a Hub contains both an EPUB and an audiobook.
/// </summary>
public sealed class AlignmentJob
{
    public Guid Id { get; set; }
    public Guid EbookAssetId { get; set; }
    public Guid AudiobookAssetId { get; set; }
    public AlignmentJobStatus Status { get; set; } = AlignmentJobStatus.Pending;

    /// <summary>
    /// JSON-encoded alignment data: array of segments mapping text offsets
    /// to audio timestamps. Populated on successful completion.
    /// </summary>
    public string? AlignmentData { get; set; }

    /// <summary>Error message when status is Failed.</summary>
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
