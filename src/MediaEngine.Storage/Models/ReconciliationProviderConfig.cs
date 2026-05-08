using System.Text.Json.Serialization;
using MediaEngine.Domain;

namespace MediaEngine.Storage.Models;

/// <summary>Configuration for the Wikidata Reconciliation + Data Extension adapter.</summary>
public sealed class ReconciliationProviderConfig
{
    [JsonPropertyName("name")] public string Name { get; set; } = "wikidata_reconciliation";
    [JsonPropertyName("provider_id")] public string ProviderId { get; set; } = WellKnownProviders.Wikidata.ToString();
    [JsonPropertyName("adapter_type")] public string AdapterType { get; set; } = "reconciliation";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("hydration_stages")] public List<int> HydrationStages { get; set; } = [1];
    [JsonPropertyName("endpoints")] public ReconciliationEndpoints Endpoints { get; set; } = new();
    [JsonPropertyName("throttle_ms")] public int ThrottleMs { get; set; } = 200;
    [JsonPropertyName("max_concurrency")] public int MaxConcurrency { get; set; } = 3;
    [JsonPropertyName("cache_ttl_hours")] public int CacheTtlHours { get; set; } = 168;
    [JsonPropertyName("batch_size")] public int BatchSize { get; set; } = 50;
    [JsonPropertyName("reconciliation")] public ReconciliationSettings Reconciliation { get; set; } = new();
    [JsonPropertyName("instance_of_classes")] public Dictionary<string, List<string>> InstanceOfClasses { get; set; } = new();
    [JsonPropertyName("exclude_classes")] public Dictionary<string, List<string>> ExcludeClasses { get; set; } = new();
    [JsonPropertyName("child_entity_discovery")] public ChildEntityDiscoveryConfig ChildEntityDiscovery { get; set; } = new();
    [JsonPropertyName("edition_pivot")] public Dictionary<string, EditionPivotRuleEntry> EditionPivot { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonPropertyName("data_extension")] public DataExtensionSettings DataExtension { get; set; } = new();

    /// <summary>
    /// Returns the edition pivot rules as an <see cref="EditionPivotConfiguration"/>.
    /// This replaces the former standalone <c>config/edition-pivot.json</c> file.
    /// </summary>
    public EditionPivotConfiguration GetEditionPivotConfiguration() => new()
    {
        Rules = new Dictionary<string, EditionPivotRuleEntry>(EditionPivot, StringComparer.OrdinalIgnoreCase),
    };
}

public sealed class ReconciliationEndpoints
{
    [JsonPropertyName("reconciliation")] public string Reconciliation { get; set; } = "https://wikidata.reconci.link/en/api";
    [JsonPropertyName("commons_file_path")] public string CommonsFilePath { get; set; } = "https://commons.wikimedia.org/wiki/Special:FilePath/";
}

public sealed class ReconciliationSettings
{
    [JsonPropertyName("auto_accept_threshold")] public int AutoAcceptThreshold { get; set; } = 95;
    [JsonPropertyName("review_threshold")] public int ReviewThreshold { get; set; } = 70;
    [JsonPropertyName("max_candidates")] public int MaxCandidates { get; set; } = 5;
    [JsonPropertyName("person_property_constraints")] public Dictionary<string, string> PersonPropertyConstraints { get; set; } = new();
    [JsonPropertyName("cast_member_limit")] public int CastMemberLimit { get; set; } = 20;
}

public sealed class DataExtensionSettings
{
    [JsonPropertyName("work_properties")] public DataExtensionPropertyGroup WorkProperties { get; set; } = new();
    [JsonPropertyName("person_properties")] public DataExtensionPropertyGroup PersonProperties { get; set; } = new();
    [JsonPropertyName("audiobook_edition_properties")] public List<string> AudiobookEditionProperties { get; set; } = [];
    [JsonPropertyName("character_properties")] public DataExtensionPropertyGroup CharacterProperties { get; set; } = new();
    [JsonPropertyName("location_properties")] public DataExtensionPropertyGroup LocationProperties { get; set; } = new();
    [JsonPropertyName("organization_properties")] public DataExtensionPropertyGroup OrganizationProperties { get; set; } = new();
    [JsonPropertyName("property_labels")] public Dictionary<string, string> PropertyLabels { get; set; } = new();
}

public sealed class DataExtensionPropertyGroup
{
    [JsonPropertyName("core")] public List<string> Core { get; set; } = [];
    [JsonPropertyName("bridges")] public List<string> Bridges { get; set; } = [];
    [JsonPropertyName("editions")] public List<string> Editions { get; set; } = [];
    [JsonPropertyName("social")] public List<string> Social { get; set; } = [];
    [JsonPropertyName("pen_names")] public List<string> PenNames { get; set; } = [];
}

// ── Child entity discovery config ──────────────────────────────────────────

public sealed class ChildEntityDiscoveryConfig
{
    [JsonPropertyName("TV")] public TvChildDiscoveryConfig Tv { get; set; } = new();
    [JsonPropertyName("Music")] public MusicChildDiscoveryConfig Music { get; set; } = new();
    [JsonPropertyName("Comics")] public ComicsChildDiscoveryConfig Comics { get; set; } = new();
}

public sealed class TvChildDiscoveryConfig
{
    [JsonPropertyName("season_property")] public string SeasonProperty { get; set; } = "P527";
    [JsonPropertyName("season_type_filter")] public List<string> SeasonTypeFilter { get; set; } = [];
    [JsonPropertyName("episode_property")] public string EpisodeProperty { get; set; } = "P527";
    [JsonPropertyName("episode_type_filter")] public List<string> EpisodeTypeFilter { get; set; } = [];
    [JsonPropertyName("episode_properties")] public List<string> EpisodeProperties { get; set; } = [];
}

public sealed class MusicChildDiscoveryConfig
{
    [JsonPropertyName("track_property")] public string TrackProperty { get; set; } = "P658";
    [JsonPropertyName("track_property_fallback")] public string TrackPropertyFallback { get; set; } = "P527";
    [JsonPropertyName("track_type_filter")] public List<string> TrackTypeFilter { get; set; } = [];
    [JsonPropertyName("track_properties")] public List<string> TrackProperties { get; set; } = [];
}

public sealed class ComicsChildDiscoveryConfig
{
    [JsonPropertyName("issue_property")] public string IssueProperty { get; set; } = "^P179";
    [JsonPropertyName("issue_type_filter")] public List<string> IssueTypeFilter { get; set; } = [];
    [JsonPropertyName("issue_properties")] public List<string> IssueProperties { get; set; } = [];
}
