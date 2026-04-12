namespace MediaEngine.Api.Models;

/// <summary>Aggregated operational health summary for the Vault Overview dashboard.</summary>
public sealed class VaultOverviewDto
{
    /// <summary>Total items in the library.</summary>
    public int TotalItems { get; init; }

    /// <summary>Items added in the last 24 hours.</summary>
    public int Added24h { get; init; }

    /// <summary>Items added in the last 7 days.</summary>
    public int Added7d { get; init; }

    /// <summary>Items added in the last 30 days.</summary>
    public int Added30d { get; init; }

    /// <summary>Counts of identity jobs by state.</summary>
    public Dictionary<string, int> PipelineStates { get; init; } = new();

    /// <summary>Pipeline success rate (0.0-1.0).</summary>
    public double PipelineSuccessRate { get; init; }

    /// <summary>Items in the review queue by trigger category.</summary>
    public Dictionary<string, int> ReviewCategories { get; init; } = new();

    /// <summary>Total items pending review.</summary>
    public int ReviewTotal { get; init; }

    /// <summary>Items with a Wikidata QID assigned.</summary>
    public int WithQid { get; init; }

    /// <summary>Items without a Wikidata QID.</summary>
    public int WithoutQid { get; init; }

    /// <summary>Items with Stage 3 universe enrichment completed.</summary>
    public int EnrichedStage3 { get; init; }

    /// <summary>Items that haven't had Stage 3 enrichment yet.</summary>
    public int NotEnrichedStage3 { get; init; }

    /// <summary>Works assigned to a universe (hub).</summary>
    public int UniverseAssigned { get; init; }

    /// <summary>Works not assigned to any universe.</summary>
    public int UniverseUnassigned { get; init; }

    /// <summary>Items with enrichment older than 30 days.</summary>
    public int StaleItems { get; init; }

    /// <summary>Item counts by media type.</summary>
    public Dictionary<string, int> MediaTypeCounts { get; init; } = new();
}
