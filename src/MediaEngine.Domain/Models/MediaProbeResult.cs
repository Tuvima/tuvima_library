namespace MediaEngine.Domain.Models;

/// <summary>
/// Structured metadata extracted from a media file via ffprobe.
/// </summary>
public sealed record MediaProbeResult
{
    // ── Duration & size ──────────────────────────────────────────────────────

    public TimeSpan Duration    { get; init; }
    public long FileSizeBytes   { get; init; }

    // ── Embedded metadata tags ───────────────────────────────────────────────

    public string? Title        { get; init; }
    public string? Artist       { get; init; }
    public string? Album        { get; init; }
    public string? AlbumArtist  { get; init; }
    public string? Genre        { get; init; }
    public string? Date         { get; init; }
    public string? Comment      { get; init; }
    public string? Narrator     { get; init; }
    public int?    TrackNumber  { get; init; }
    public string? Publisher    { get; init; }
    public string? Description  { get; init; }

    // ── Audio stream ─────────────────────────────────────────────────────────

    public string? AudioCodec   { get; init; }
    public int?    AudioBitrate { get; init; }   // kbps
    public int?    SampleRate   { get; init; }   // Hz
    public int?    Channels     { get; init; }

    // ── Video stream (if present) ────────────────────────────────────────────

    public string? VideoCodec   { get; init; }
    public int?    Width        { get; init; }
    public int?    Height       { get; init; }
    public double? FrameRate    { get; init; }

    // ── Cover art & chapters ─────────────────────────────────────────────────

    public bool HasEmbeddedCover              { get; init; }
    public int  ChapterCount                  { get; init; }
    public IReadOnlyList<string> SubtitleLanguages { get; init; } = [];
}
