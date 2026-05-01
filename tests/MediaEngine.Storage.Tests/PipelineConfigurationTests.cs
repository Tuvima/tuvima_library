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
            ["Books"] = ["apple_api", "open_library"],
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
            Assert.Equal("Wikidata Reconciliation", priorities[0]);
            Assert.Equal(fallbacks, priorities.Skip(1).ToArray());
        }

        Assert.Equal(
            ["Wikidata Reconciliation", "tmdb"],
            ReadPriority(document, "TV", "episode_description"));
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
