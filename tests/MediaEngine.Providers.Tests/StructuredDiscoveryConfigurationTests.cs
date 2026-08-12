using System.Text.Json;
using MediaEngine.Domain;
using MediaEngine.Domain.Constants;

namespace MediaEngine.Providers.Tests;

public sealed class StructuredDiscoveryConfigurationTests
{
    [Fact]
    public void RuntimeWikidataConfigurationCoversEveryDeclaredDiscoveryProperty()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            FindRepoRoot(), "config", "providers", "wikidata_reconciliation.json")));
        var extension = document.RootElement.GetProperty("data_extension");
        var core = extension.GetProperty("work_properties").GetProperty("core")
            .EnumerateArray().Select(item => item.GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var labels = extension.GetProperty("property_labels");

        foreach (var field in StructuredDiscoveryFieldCatalog.Fields
            .Where(field => field.Source == DiscoveryFactSource.StructuredProvider && field.WikidataProperty is not null))
        {
            Assert.Contains(field.WikidataProperty, core);
            Assert.True(labels.TryGetProperty(field.WikidataProperty!, out var label),
                $"{field.WikidataProperty} has no runtime property label mapping.");
            Assert.Equal(field.Key, label.GetString());
            Assert.Contains(field.Key, MetadataFieldConstants.MultiValuedKeys);
            Assert.Contains(field.Key + MetadataFieldConstants.CompanionQidSuffix, MetadataFieldConstants.MultiValuedKeys);
        }
    }

    [Fact]
    public void SubjectiveAttributesRemainLocalAiFields()
    {
        Assert.All(
            StructuredDiscoveryFieldCatalog.Fields.Where(field => field.Key is "themes" or "mood" or "vibe" or "content_warnings"),
            field => Assert.Equal(DiscoveryFactSource.LocalAi, field.Source));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "MediaEngine.slnx")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
