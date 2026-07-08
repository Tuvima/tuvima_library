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

        Assert.Equal(["apple_api"], ReadPriority(document, "Music", "track_number"));
        Assert.Equal(["apple_api"], ReadPriority(document, "Music", "disc_number"));
        Assert.Equal(["apple_api"], ReadPriority(document, "Music", "cover"));
        Assert.Equal(["apple_api", "musicbrainz"], ReadPriority(document, "Music", "album"));
        Assert.Equal(["apple_api", "musicbrainz"], ReadPriority(document, "Music", "year"));
        Assert.Equal(["apple_api", "musicbrainz"], ReadPriority(document, "Music", "track_count"));
        Assert.Equal(["apple_api", "musicbrainz"], ReadPriority(document, "Music", "sequence_total"));
    }

    [Fact]
    public void MusicBrainz_IsEnabledForEnrichmentButNotStageOneMusicLookup()
    {
        using var pipelines = JsonDocument.Parse(File.ReadAllText(FindRepoFile("config", "pipelines.json")));
        using var provider = JsonDocument.Parse(File.ReadAllText(FindRepoFile("config", "providers", "musicbrainz.json")));

        var musicProviders = pipelines.RootElement
            .GetProperty("Music")
            .GetProperty("providers")
            .EnumerateArray()
            .Select(element => element.GetProperty("name").GetString() ?? "")
            .ToArray();

        Assert.Equal(["apple_api"], musicProviders);
        Assert.True(provider.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal([3], provider.RootElement.GetProperty("hydration_stages").EnumerateArray().Select(element => element.GetInt32()).ToArray());
        Assert.Contains("musicbrainz_release_group_id", provider.RootElement.GetProperty("preferred_bridge_ids").GetProperty("Music").EnumerateArray().Select(element => element.GetString()));
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
