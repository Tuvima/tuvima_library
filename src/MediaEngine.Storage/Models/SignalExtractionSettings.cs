namespace MediaEngine.Storage.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Typed model for <c>config/signal_extraction.json</c>.
/// Drives the description signal extraction pipeline.
/// </summary>
public sealed class SignalExtractionSettings
{
    [JsonPropertyName("global_settings")]
    public SignalExtractionGlobalSettings GlobalSettings { get; set; } = new();

    [JsonPropertyName("categories")]
    public Dictionary<string, SignalExtractionCategory> Categories { get; set; } = new();
}

public sealed class SignalExtractionGlobalSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("name_separators")]
    public List<string> NameSeparators { get; set; } = [" and ", " & ", ", "];

    [JsonPropertyName("stop_names")]
    public List<string> StopNames { get; set; } = ["Others", "More", "Various", "All Ages", "Various Artists", "Full Cast", "Ensemble", "Multiple"];

    [JsonPropertyName("min_name_words")]
    public int MinNameWords { get; set; } = 2;

    [JsonPropertyName("max_name_length")]
    public int MaxNameLength { get; set; } = 60;

    [JsonPropertyName("min_wikidata_score")]
    public int MinWikidataScore { get; set; } = 70;

    [JsonPropertyName("wikidata_person_class")]
    public string WikidataPersonClass { get; set; } = "Q5";

    [JsonPropertyName("confidence_from_description")]
    public double ConfidenceFromDescription { get; set; } = 0.60;

    [JsonPropertyName("confidence_from_file_metadata")]
    public double ConfidenceFromFileMetadata { get; set; } = 0.75;

    [JsonPropertyName("confidence_verified_with_role")]
    public double ConfidenceVerifiedWithRole { get; set; } = 0.85;

    [JsonPropertyName("confidence_verified_no_role")]
    public double ConfidenceVerifiedNoRole { get; set; } = 0.65;

    [JsonPropertyName("max_extractions_per_entity")]
    public int MaxExtractionsPerEntity { get; set; } = 10;

    [JsonPropertyName("description_max_chars")]
    public int DescriptionMaxChars { get; set; } = 3000;
}

public sealed class SignalExtractionCategory
{
    [JsonPropertyName("extraction_rules")]
    public List<SignalExtractionRule> ExtractionRules { get; set; } = [];
}

public sealed class SignalExtractionRule
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("patterns")]
    public List<string> Patterns { get; set; } = [];

    [JsonPropertyName("occupation_classes")]
    public List<string> OccupationClasses { get; set; } = [];

    [JsonPropertyName("source_fields")]
    public List<string> SourceFields { get; set; } = ["description"];

    [JsonPropertyName("file_metadata_keys")]
    public List<string> FileMetadataKeys { get; set; } = [];
}
