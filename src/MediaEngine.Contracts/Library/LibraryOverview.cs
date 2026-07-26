namespace MediaEngine.Contracts.Library;

/// <summary>Aggregated operational health summary for the library dashboard.</summary>
public sealed class LibraryOverviewDto
{
    public int TotalItems { get; init; }
    public int Added24h { get; init; }
    public int Added7d { get; init; }
    public int Added30d { get; init; }
    public Dictionary<string, int> PipelineStates { get; init; } = new();
    public double PipelineSuccessRate { get; init; }
    public Dictionary<string, int> ReviewCategories { get; init; } = new();
    public int ReviewTotal { get; init; }
    public int WithQid { get; init; }
    public int WithoutQid { get; init; }
    public int EnrichedStage3 { get; init; }
    public int NotEnrichedStage3 { get; init; }
    public int UniverseAssigned { get; init; }
    public int UniverseUnassigned { get; init; }
    public int StaleItems { get; init; }
    public Dictionary<string, int> MediaTypeCounts { get; init; } = new();
    public int HiddenByQualityGate { get; init; }
    public int ArtPending { get; init; }
    public int RetailNeedsReview { get; init; }
    public int QidNoMatch { get; init; }
    public int CompletedWithArt { get; init; }
}
