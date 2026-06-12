using MediaEngine.Providers.Services;
using MediaEngine.Domain.Enums;
using Tuvima.Wikidata;

namespace MediaEngine.Providers.Tests;

public sealed class CollectionAssignmentServiceTests
{
    [Fact]
    public void CollectionAssignment_UsesSeriesAsShelfAndDoesNotFallbackToBroadUniverse()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Providers\Services\CollectionAssignmentService.cs"));
        var resolveStart = source.IndexOf("private static ShelfIdentity? ResolveShelfIdentity", StringComparison.Ordinal);
        Assert.True(resolveStart >= 0);

        var resolveEnd = source.IndexOf("private static string? ResolveProviderKey", resolveStart, StringComparison.Ordinal);
        Assert.True(resolveEnd > resolveStart);

        var resolveSource = source[resolveStart..resolveEnd];
        Assert.Contains("TryGetQid(lookup, \"series\"", resolveSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetQid(lookup, \"franchise\"", resolveSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetQid(lookup, \"fictional_universe\"", resolveSource, StringComparison.Ordinal);
        Assert.Contains("multiple shelves share them", source, StringComparison.Ordinal);
        Assert.Contains("FindShelfCollectionAsync(shelf", source, StringComparison.Ordinal);
        Assert.Contains("reassigning", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionAssignment_SupportsProviderBackedShelfIdentityBeforeQidUpgrade()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Providers\Services\CollectionAssignmentService.cs"));

        Assert.Contains("internal sealed record ShelfIdentity", File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Providers\Models\ShelfIdentity.cs")), StringComparison.Ordinal);
        Assert.Contains("FindByRuleHashAsync(shelf.ProviderKey", source, StringComparison.Ordinal);
        Assert.Contains("collection.RuleHash, shelf.ProviderKey", source, StringComparison.Ordinal);
        Assert.Contains("tmdb:collection:{tmdbCollectionId}", source, StringComparison.Ordinal);
        Assert.Contains("tmdb:tv:{tmdbTvId}", source, StringComparison.Ordinal);
        Assert.Contains("tvdb:tv:{tvdbId}", source, StringComparison.Ordinal);
        Assert.Contains("UpgradeCollectionIdentityAsync(existingCollection, shelf", source, StringComparison.Ordinal);
        Assert.Contains("WikidataQid = shelf.Qid", source, StringComparison.Ordinal);
        Assert.Contains("RuleHash = shelf.ProviderKey", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TmdbAdapter_AddsMovieCollectionClaimsForWatchShelves()
    {
        var adapter = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Providers\Adapters\ConfigDrivenAdapter.cs"));
        var config = File.ReadAllText(GetRepoFilePath(@"config\providers\tmdb.json"));

        Assert.Contains("AddTmdbMovieCollectionClaims", adapter, StringComparison.Ordinal);
        Assert.Contains("belongs_to_collection", adapter, StringComparison.Ordinal);
        Assert.Contains("\"tmdb_collection_id\"", config, StringComparison.Ordinal);
        Assert.Contains("\"tmdb_collection_name\"", config, StringComparison.Ordinal);
        Assert.Contains("MetadataFieldConstants.Series", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void SeriesManifestHydration_UsesImmediateSeriesBeforeBroaderCollectionFallback()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Providers\Services\WikidataSeriesManifestHydrationService.cs"));
        var resolveStart = source.IndexOf("private static bool TryResolveFromClaims", StringComparison.Ordinal);
        Assert.True(resolveStart >= 0);

        var resolveEnd = source.IndexOf("private static IEnumerable<SeriesManifestItem> FilterManifestItems", resolveStart, StringComparison.Ordinal);
        Assert.True(resolveEnd > resolveStart);

        var resolveSource = source[resolveStart..resolveEnd];
        Assert.Contains("\"series_qid\"", resolveSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"franchise_qid\"", resolveSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"fictional_universe_qid\"", resolveSource, StringComparison.Ordinal);
        Assert.Contains("RelType, \"series\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SeriesManifestHydration_BuildsDiagnosticsForWikidataOrderWarnings()
    {
        var diagnostics = WikidataSeriesManifestHydrationService.BuildWarningDiagnostics(
        [
            new SeriesManifestWarning
            {
                Code = "BrokenPreviousNextChain",
                Message = "Book 2 does not point back to Book 1.",
                Qid = "Q2",
            },
            new SeriesManifestWarning
            {
                Code = "PreviousNextConflictsWithOrdinal",
                Message = "The ordinal and previous/next data disagree.",
                Qid = "Q3",
            },
        ],
        seriesQid: "QSeries",
        resolvedWorkQid: "QOwned",
        resolvedWorkPresentInManifest: false);

        Assert.Equal(["BrokenPreviousNextChain", "PreviousNextConflictsWithOrdinal", "ResolvedWorkMissingFromManifest"],
            diagnostics.Select(d => d.Code));
        Assert.Equal("QOwned", diagnostics.Last().Qid);
    }

    [Fact]
    public void SeriesManifestHydration_NormalizesDuplicateItemsAndDenseDisplayOrder()
    {
        var normalized = WikidataSeriesManifestHydrationService.NormalizeManifestItems(
        [
            new SeriesManifestItem
            {
                Qid = "Q1",
                Label = "The Matrix",
                ParsedSeriesOrdinal = 1,
                PublicationDate = new DateOnly(1999, 3, 31),
            },
            new SeriesManifestItem
            {
                Qid = "Q1",
                Label = "The Matrix duplicate from expanded collection",
                ParsedSeriesOrdinal = 1,
                PublicationDate = new DateOnly(1999, 3, 31),
                IsExpandedFromCollection = true,
            },
            new SeriesManifestItem
            {
                Qid = "Q2",
                Label = "The Animatrix",
                ParsedSeriesOrdinal = 1,
                PublicationDate = new DateOnly(2003, 6, 3),
            },
            new SeriesManifestItem
            {
                Qid = "Q3",
                Label = "The Matrix Reloaded",
                ParsedSeriesOrdinal = 2,
                PublicationDate = new DateOnly(2003, 5, 15),
            },
        ]);

        Assert.Equal(["Q1", "Q2", "Q3"], normalized.Select(item => item.Item.Qid));
        Assert.Equal([1, 2, 3], normalized.Select(item => item.SortOrder));
    }

    [Fact]
    public void SeriesManifestHydration_FiltersLiteraryManifestToMainlineNumberedWorks()
    {
        var items = new List<SeriesManifestItem>
        {
            new()
            {
                Qid = "QPrequel",
                Label = "Series prequel",
                ParsedSeriesOrdinal = 0.5m,
            },
            new()
            {
                Qid = "QBook1",
                Label = "Book One",
                ParsedSeriesOrdinal = 1,
            },
            new()
            {
                Qid = "QPlay",
                Label = "The Stage Play",
                Description = "A two-part stage play script.",
                ParsedSeriesOrdinal = 8,
            },
            new()
            {
                Qid = "QBook2",
                Label = "Book Two",
                ParsedSeriesOrdinal = 2,
            },
        };

        var context = new SeriesManifestHydrationContext(
            AssetId: Guid.NewGuid(),
            WorkId: Guid.NewGuid(),
            ResolvedWorkQid: "QBook1",
            MediaType: MediaType.Audiobooks,
            Title: "Book One",
            SeriesHint: "Series",
            IngestionRunId: null,
            Lineage: null,
            FullClaims: []);

        var method = typeof(WikidataSeriesManifestHydrationService).GetMethod(
            "FilterManifestItems",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);
        var filtered = Assert.IsAssignableFrom<IEnumerable<SeriesManifestItem>>(
            method!.Invoke(null, [items, context, "QSeries"]));

        Assert.Equal(["QBook1", "QBook2"], filtered.Select(item => item.Qid));
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
