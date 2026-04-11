using System.Text.Json.Serialization;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Storage.Models;

/// <summary>
/// A single provider entry in a media type's pipeline configuration.
/// Providers are executed in <see cref="Rank"/> order according to
/// the pipeline's <see cref="MediaTypePipeline.Strategy"/>.
/// </summary>
public sealed class PipelineProviderEntry
{
    /// <summary>
    /// Execution order within the pipeline. Lower numbers run first.
    /// Must be unique within a media type's provider list.
    /// </summary>
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    /// <summary>
    /// Provider config name (e.g. "apple_api", "musicbrainz", "tmdb").
    /// Must match the <c>name</c> field in the corresponding provider config file.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Pipeline configuration for a single media type.
/// Defines the execution strategy and ordered provider list for Stage 1
/// (Retail Identification) of the hydration pipeline.
/// </summary>
public sealed class MediaTypePipeline
{
    /// <summary>
    /// How providers collaborate: Waterfall (first match wins), Cascade (all run,
    /// claims merge), or Sequential (chained, each feeds the next).
    /// </summary>
    [JsonPropertyName("strategy")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ProviderStrategy Strategy { get; set; } = ProviderStrategy.Waterfall;

    /// <summary>
    /// Ranked list of providers for this media type. Executed in
    /// <see cref="PipelineProviderEntry.Rank"/> order.
    /// </summary>
    [JsonPropertyName("providers")]
    public List<PipelineProviderEntry> Providers { get; set; } = [];

    /// <summary>
    /// Per-field provider priority overrides for this media type.
    /// Key = claim key (e.g. "cover", "description", "narrator").
    /// Value = ordered list of provider names; first provider with a claim wins.
    ///
    /// Only needed for fields where Wikidata is silent (cover, rating, description,
    /// narrator, duration, page_count). For structured fields (title, author, year,
    /// genre, series), Wikidata always wins via Tier C of the Priority Cascade.
    /// </summary>
    [JsonPropertyName("field_priorities")]
    public Dictionary<string, List<string>> FieldPriorities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Complete pipeline configuration for all media types.
///
/// Loaded from <c>config/pipelines.json</c>. Supports unlimited ranked
/// providers per media type and three execution strategies.
/// </summary>
public sealed class PipelineConfiguration
{
    /// <summary>
    /// Maps media type display names to their pipeline configuration.
    /// Keys: "Books", "Audiobooks", "Comics", "Movies", "TV", "Music".
    /// </summary>
    [JsonPropertyName("pipelines")]
    public Dictionary<string, MediaTypePipeline> Pipelines { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the pipeline config for a given media type display name.
    /// Returns an empty Waterfall pipeline if the media type is not configured.
    /// </summary>
    public MediaTypePipeline GetPipelineForMediaType(string mediaTypeDisplayName)
    {
        return Pipelines.TryGetValue(mediaTypeDisplayName, out var pipeline) ? pipeline : new();
    }

    /// <summary>
    /// Resolves the pipeline config for a given <see cref="MediaType"/> enum value.
    /// </summary>
    public MediaTypePipeline GetPipelineForMediaType(MediaType mediaType)
    {
        return GetPipelineForMediaType(mediaType.ToString());
    }
}
