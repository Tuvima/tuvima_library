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
        var resolveStart = source.IndexOf("private static (string? Qid, string? Label) ResolveParentQid", StringComparison.Ordinal);
        Assert.True(resolveStart >= 0);

        var resolveEnd = source.IndexOf("private static bool TryGetQid", resolveStart, StringComparison.Ordinal);
        Assert.True(resolveEnd > resolveStart);

        var resolveSource = source[resolveStart..resolveEnd];
        Assert.Contains("TryGetQid(lookup, \"series\"", resolveSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetQid(lookup, \"franchise\"", resolveSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetQid(lookup, \"fictional_universe\"", resolveSource, StringComparison.Ordinal);
        Assert.Contains("multiple shelves share them", source, StringComparison.Ordinal);
        Assert.Contains("string.Equals(existingCollection.WikidataQid, parentQid", source, StringComparison.Ordinal);
        Assert.Contains("reassigning", source, StringComparison.Ordinal);
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
