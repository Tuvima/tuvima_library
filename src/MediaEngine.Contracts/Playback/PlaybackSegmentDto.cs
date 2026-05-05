namespace MediaEngine.Contracts.Playback;

public sealed record PlaybackSegmentDto
{
    public Guid Id { get; init; }
    public Guid AssetId { get; init; }
    public string Kind { get; init; } = "";
    public double StartSeconds { get; init; }
    public double? EndSeconds { get; init; }
    public double Confidence { get; init; }
    public string Source { get; init; } = "";
    public string? PluginId { get; init; }
    public bool IsSkippable { get; init; }
    public string ReviewStatus { get; init; } = "detected";
}
