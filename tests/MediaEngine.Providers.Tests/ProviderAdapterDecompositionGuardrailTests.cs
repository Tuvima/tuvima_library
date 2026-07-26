using System.Reflection;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Workers;

namespace MediaEngine.Providers.Tests;

public sealed class ProviderAdapterDecompositionGuardrailTests
{
    [Theory]
    [InlineData("Adapters", "ReconciliationAdapter.cs")]
    [InlineData("Adapters", "ConfigDrivenAdapter.cs")]
    [InlineData("Workers", "RetailMatchWorker.cs")]
    [InlineData("Workers", "WikidataBridgeWorker.cs")]
    public void PublicProviderFacades_RemainFocused(string area, string fileName)
    {
        var path = Path.Combine(FindRepoRoot(), "src", "MediaEngine.Providers", area, fileName);

        Assert.True(
            File.ReadLines(path).Count() <= 500,
            $"{fileName} must remain a focused public facade of at most 500 lines.");
    }

    [Theory]
    [InlineData("Adapters", "ReconciliationAdapter")]
    [InlineData("Adapters", "ConfigDrivenAdapter")]
    [InlineData("Workers", "RetailMatchWorker")]
    [InlineData("Workers", "WikidataBridgeWorker")]
    public void ExtractedProviderInternals_RemainReviewable(string area, string facadeName)
    {
        var root = Path.Combine(
            FindRepoRoot(),
            "src",
            "MediaEngine.Providers",
            area,
            "Internals");
        var files = Directory.GetFiles(root, $"{facadeName}.*.cs", SearchOption.TopDirectoryOnly);

        Assert.True(files.Length >= 2);

        var oversized = files
            .Select(path => new { Path = path, Lines = File.ReadLines(path).Count() })
            .Where(file => file.Lines > 1_500)
            .Select(file => $"{Path.GetFileName(file.Path)} ({file.Lines} lines)")
            .ToList();

        Assert.Empty(oversized);
    }

    [Fact]
    public void PublicAdapterMethodSurface_RemainsStable()
    {
        Assert.Equal(
            [
                "CanHandle",
                "CheckEntityStalenessAsync",
                "DiscoverAudiobookEditionsAsync",
                "ExtendAsync",
                "FetchAsync",
                "FilterByMediaTypeAsync",
                "LookupFictionalEntityAsync",
                "ResolveAndDownloadPersonImageAsync",
                "ResolveAsync",
                "ResolveBatchAsync",
                "SearchAsync",
            ],
            PublicMethodNames<ReconciliationAdapter>());

        Assert.Equal(
            [
                "CanHandle",
                "FetchAsync",
                "SearchAsync",
            ],
            PublicMethodNames<ConfigDrivenAdapter>());

        Assert.Equal(
            ["CapabilityTags", "Domain", "Name", "ProviderId", "ReviewThreshold"],
            PublicPropertyNames<ReconciliationAdapter>());
        Assert.Equal(
            ["CapabilityTags", "Domain", "Name", "ProviderId"],
            PublicPropertyNames<ConfigDrivenAdapter>());

        Assert.Equal(["PollAsync"], PublicMethodNames<RetailMatchWorker>());
        Assert.Equal(["PollAsync"], PublicMethodNames<WikidataBridgeWorker>());
        Assert.Empty(PublicPropertyNames<RetailMatchWorker>());
        Assert.Empty(PublicPropertyNames<WikidataBridgeWorker>());
    }

    private static string[] PublicMethodNames<T>() =>
        typeof(T)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] PublicPropertyNames<T>() =>
        typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
