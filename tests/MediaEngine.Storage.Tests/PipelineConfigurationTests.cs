using System.Text.Json;

namespace MediaEngine.Storage.Tests;

public sealed class PipelineConfigurationTests
{
    [Fact]
    public void MediaPipelines_PreferWikidataDescriptionsBeforeRetailFallbacks()
    {
        var configPath = FindRepoFile("config", "pipelines.json");
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));

        var expectedRetailFallbacks = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Books"] = ["apple_api"],
            ["Audiobooks"] = ["apple_api"],
            ["Music"] = ["apple_api"],
            ["Movies"] = ["tmdb"],
            ["TV"] = ["tmdb"],
            ["Comics"] = ["comicvine"],
        };

        foreach (var (mediaType, fallbacks) in expectedRetailFallbacks)
        {
            var priorities = ReadPriority(document, mediaType, "description");

            Assert.NotEmpty(priorities);
            Assert.Equal("wikidata_reconciliation", priorities[0]);
            Assert.Equal(fallbacks, priorities.Skip(1).ToArray());
        }

        Assert.Equal(
            ["wikidata_reconciliation", "tmdb"],
            ReadPriority(document, "TV", "episode_description"));
    }

    [Fact]
    public void AudiobookPipeline_PrefersRetailEditionMetadataForDisplayIdentity()
    {
        var configPath = FindRepoFile("config", "pipelines.json");
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));

        Assert.Equal(["apple_api"], ReadPriority(document, "Audiobooks", "title"));
        Assert.Equal(["apple_api"], ReadPriority(document, "Audiobooks", "author"));
        Assert.Equal(["apple_api"], ReadPriority(document, "Audiobooks", "series"));
        Assert.Equal(["apple_api"], ReadPriority(document, "Audiobooks", "narrator"));
        Assert.Equal(["apple_api"], ReadPriority(document, "Audiobooks", "cover"));
    }

    [Fact]
    public void MediaPipelines_AssignSequenceFieldsToMediaSpecificProviders()
    {
        var configPath = FindRepoFile("config", "pipelines.json");
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));

        Assert.Equal(["tmdb", "wikidata_reconciliation"], ReadPriority(document, "Movies", "series"));
        Assert.Equal(["tmdb", "wikidata_reconciliation"], ReadPriority(document, "Movies", "series_position"));
        Assert.Equal(["tmdb", "wikidata_reconciliation"], ReadPriority(document, "Movies", "sequence_total"));

        Assert.Equal(["tmdb", "wikidata_reconciliation"], ReadPriority(document, "TV", "episode_number"));
        Assert.Equal(["tmdb", "wikidata_reconciliation"], ReadPriority(document, "TV", "episode_count"));
        Assert.Equal(["tmdb", "wikidata_reconciliation"], ReadPriority(document, "TV", "sequence_total"));

        Assert.Equal(["comicvine", "wikidata_reconciliation"], ReadPriority(document, "Comics", "series"));
        Assert.Equal(["comicvine", "wikidata_reconciliation"], ReadPriority(document, "Comics", "issue_number"));
        Assert.Equal(["comicvine", "local_processor"], ReadPriority(document, "Comics", "issue_title"));
        Assert.Equal(["comicvine", "local_processor"], ReadPriority(document, "Comics", "issue_description"));
        Assert.Equal(["comicvine"], ReadPriority(document, "Comics", "issue_source_url"));
        Assert.Equal(["comicvine", "wikidata_reconciliation"], ReadPriority(document, "Comics", "sequence_total"));

        Assert.Equal(["musicbrainz", "apple_api"], ReadPriority(document, "Music", "title"));
        Assert.Equal(["musicbrainz", "apple_api"], ReadPriority(document, "Music", "author"));
        Assert.Equal(["musicbrainz", "apple_api"], ReadPriority(document, "Music", "artist"));
        Assert.Equal(["apple_api"], ReadPriority(document, "Music", "track_number"));
        Assert.Equal(["apple_api"], ReadPriority(document, "Music", "disc_number"));
        Assert.Equal(["apple_api"], ReadPriority(document, "Music", "cover"));
        Assert.Equal(["musicbrainz", "apple_api"], ReadPriority(document, "Music", "album"));
        Assert.Equal(["musicbrainz", "apple_api"], ReadPriority(document, "Music", "year"));
        Assert.Equal(["musicbrainz", "apple_api"], ReadPriority(document, "Music", "track_count"));
        Assert.Equal(["musicbrainz", "apple_api"], ReadPriority(document, "Music", "sequence_total"));
    }

    [Fact]
    public void MusicBrainz_IsConfiguredBeforeAppleForStageOneMusicIdentity()
    {
        using var pipelines = JsonDocument.Parse(File.ReadAllText(FindRepoFile("config", "pipelines.json")));
        using var provider = JsonDocument.Parse(File.ReadAllText(FindRepoFile("config", "providers", "musicbrainz.json")));

        var musicPipeline = pipelines.RootElement
            .GetProperty("Music")
            .GetProperty("providers");
        var musicProviders = musicPipeline
            .EnumerateArray()
            .Select(element => element.GetProperty("name").GetString() ?? "")
            .ToArray();
        var musicPurposes = musicPipeline
            .EnumerateArray()
            .Select(element => element.GetProperty("purpose").GetString() ?? "")
            .ToArray();

        Assert.Equal("Sequential", pipelines.RootElement.GetProperty("Music").GetProperty("strategy").GetString());
        Assert.Equal(["musicbrainz", "apple_api"], musicProviders);
        Assert.Equal(["identity", "enrichment"], musicPurposes);
        Assert.True(provider.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal([1, 3], provider.RootElement.GetProperty("hydration_stages").EnumerateArray().Select(element => element.GetInt32()).ToArray());
        Assert.Contains("musicbrainz_release_group_id", provider.RootElement.GetProperty("preferred_bridge_ids").GetProperty("Music").EnumerateArray().Select(element => element.GetString()));
    }

    [Fact]
    public void MusicBridgePriority_PrefersMusicBrainzIdsBeforeAppleIds()
    {
        using var provider = JsonDocument.Parse(File.ReadAllText(FindRepoFile("config", "providers", "musicbrainz.json")));
        using var wikidata = JsonDocument.Parse(File.ReadAllText(FindRepoFile("config", "providers", "wikidata_reconciliation.json")));

        var preferred = provider.RootElement
            .GetProperty("preferred_bridge_ids")
            .GetProperty("Music")
            .EnumerateArray()
            .Select(element => element.GetString() ?? "")
            .ToArray();

        Assert.Equal("musicbrainz_release_group_id", preferred[0]);
        Assert.Contains("musicbrainz_recording_id", preferred);
        Assert.DoesNotContain("apple_music_id", preferred);

        var labels = wikidata.RootElement
            .GetProperty("data_extension")
            .GetProperty("property_labels");
        Assert.Equal("musicbrainz_release_group_id", labels.GetProperty("P436").GetString());
        Assert.Equal("musicbrainz_recording_id", labels.GetProperty("P4404").GetString());
        Assert.Equal("apple_music_id", labels.GetProperty("P10110").GetString());
    }

    [Fact]
    public void HydrationConfig_DeclaresCollectionRollupRelationshipTypes()
    {
        using var hydration = JsonDocument.Parse(File.ReadAllText(FindRepoFile("config", "hydration.json")));
        var types = hydration.RootElement
            .GetProperty("collection_rollup_relationship_types")
            .EnumerateArray()
            .Select(element => element.GetString() ?? "")
            .ToArray();

        Assert.Equal(["series", "franchise", "fictional_universe", "based_on"], types);
    }

    [Fact]
    public void Documentation_DescribesMusicBrainzFirstMusicIdentity()
    {
        var providerGuide = File.ReadAllText(FindRepoFile("docs", "guides", "configuring-providers.md"));
        var architecture = File.ReadAllText(FindRepoFile("docs", "architecture", "ingestion-identity-enrichment-pipeline.md"));
        var mediaTypes = File.ReadAllText(FindRepoFile("docs", "reference", "media-types.md"));

        Assert.Contains("MusicBrainz first for identity, then Apple", providerGuide, StringComparison.Ordinal);
        Assert.Contains("For music, Stage 1 is configured as MusicBrainz identity first and Apple API enrichment second.", architecture, StringComparison.Ordinal);
        Assert.Contains("MusicBrainz - Stage 1 identity", mediaTypes, StringComparison.Ordinal);
        Assert.Contains("Apple API - Stage 1 enrichment after MusicBrainz identity", mediaTypes, StringComparison.Ordinal);
    }

    private static string[] ReadPriority(JsonDocument document, string mediaType, string field)
    {
        var root = document.RootElement;
        Assert.True(root.TryGetProperty(mediaType, out var mediaConfig), $"{mediaType} pipeline is missing");
        Assert.True(mediaConfig.TryGetProperty("field_priorities", out var priorities), $"{mediaType} priorities are missing");
        Assert.True(priorities.TryGetProperty(field, out var fieldPriorities), $"{mediaType}.{field} priority is missing");

        return fieldPriorities
            .EnumerateArray()
            .Select(element => element.GetString() ?? "")
            .ToArray();
    }

    private static string FindRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {Path.Combine(parts)} from {AppContext.BaseDirectory}");
    }
}
