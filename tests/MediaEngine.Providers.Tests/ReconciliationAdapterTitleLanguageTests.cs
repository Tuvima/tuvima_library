namespace MediaEngine.Providers.Tests;

public sealed class ReconciliationAdapterTitleLanguageTests
{
    [Fact]
    public void ForeignLanguageLabelsRemainOriginalTitlesInsteadOfDisplayTitles()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Providers\Adapters\ReconciliationAdapter.cs"));

        Assert.Contains("emit it as \"original_title\" only", source, StringComparison.Ordinal);
        Assert.Contains("claims.Add(new ProviderClaim(MetadataFieldConstants.OriginalTitle, fileLangLabel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceLanguageTitleConfidence", source, StringComparison.Ordinal);
        Assert.DoesNotContain("claims.Add(new ProviderClaim(MetadataFieldConstants.Title, fileLangLabel", source, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
