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

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
