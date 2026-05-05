namespace MediaEngine.Domain.Entities;

public sealed class PlaybackSegment
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public string Kind { get; set; } = "";
    public double StartSeconds { get; set; }
    public double? EndSeconds { get; set; }
    public double Confidence { get; set; }
    public string Source { get; set; } = "";
    public string? PluginId { get; set; }
    public bool IsSkippable { get; set; }
    public string ReviewStatus { get; set; } = "detected";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
