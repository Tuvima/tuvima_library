using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Entities;

/// <summary>
/// A managed timed-text asset such as synced lyrics or subtitles.
/// </summary>
public sealed class TextTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssetId { get; set; }
    public TextTrackKind Kind { get; set; }
    public string Language { get; set; } = "und";
    public string Provider { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string? SourceId { get; set; }
    public string? SourceUrl { get; set; }
    public string SourceFormat { get; set; } = string.Empty;
    public string NormalizedFormat { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string? SidecarPath { get; set; }
    public string TimingMode { get; set; } = "Line";
    public double? DurationMatchScore { get; set; }
    public bool IsHearingImpaired { get; set; }
    public bool IsPreferred { get; set; }
    public bool IsUserOwned { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
