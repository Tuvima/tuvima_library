namespace MediaEngine.Api.Tests;

public sealed class TestHarnessGuardrailTests
{
    [Fact]
    public void Generator_KeepsVideoAndMusicFixturesIndependentOfFfmpeg()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "tools", "GenerateTestEpubs", "Program.cs"));

        Assert.Contains("var movieSeries", source);
        Assert.Contains("var tvSeries", source);
        Assert.Contains("var musicTracks", source);
        Assert.Contains("CreateMp4Fixture", source);
        Assert.Contains("CreateMp3Fixture", source);
        Assert.Contains("CreateMinimalMp4", source);
        Assert.Contains("CreateMinimalMp3", source);
        Assert.DoesNotContain("failed += movieSeries.Length", source);
        Assert.DoesNotContain("failed += tvSeries.Length", source);
        Assert.DoesNotContain("failed += musicTracks.Length", source);
    }

    [Fact]
    public void Generator_DeclaresSeriesAndMusicPeopleForEnrichmentValidation()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "tools", "GenerateTestEpubs", "Program.cs"));

        Assert.Contains("\"Aaron Paul\"", source);
        Assert.Contains("\"Q302491\"", source);
        Assert.Contains("\"Anna Gunn\"", source);
        Assert.Contains("\"Q271050\"", source);
        Assert.Contains("\"David Bowie\"", source);
        Assert.Contains("\"Q5383\"", source);
    }

    [Fact]
    public void Generator_KeepsExpectedWatchFixturesInFilesystemHarness()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "tools", "GenerateTestEpubs", "Program.cs"));

        Assert.Contains("Arrival (2016) {imdb-tt2543164}.mp4", source);
        Assert.Contains("The Shawshank Redemption (1994) {imdb-tt0111161}.mp4", source);
        Assert.Contains("Shogun S01E01 Anjin (2024).mp4", source);
        Assert.Contains("large ? \"118\" : \"47\"", source);
    }

    [Fact]
    public void Generator_HasLargeStressCorpusAcrossAllPrimaryMediaTypes()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "tools", "GenerateTestEpubs", "Program.cs"));
        var resetScript = File.ReadAllText(Path.Combine(FindRepoRoot(), "tools", "test-data", "reset-and-generate.ps1"));

        Assert.Contains("--large", source);
        Assert.Contains("var largeBooks", source);
        Assert.Contains("var largeAudiobooks", source);
        Assert.Contains("var largeMovies", source);
        Assert.Contains("var largeTv", source);
        Assert.Contains("var largeMusic", source);
        Assert.Contains("The Shining (1980) {imdb-tt0081505}.mp4", source);
        Assert.Contains("Blade Runner (1982) {imdb-tt0083658}.mp4", source);
        Assert.Contains("Foundation S01E01 The Emperor's Peace", source);
        Assert.Contains("Hans Zimmer", source);
        Assert.Contains("large ? \"118\" : \"47\"", source);
        Assert.Contains("[switch]$Large", resetScript);
        Assert.Contains(@"""C:\temp\tuvima-watch""", resetScript);
    }

    [Fact]
    public void PersonEnrichment_ReadsTvParentWorkLineage()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "MediaEngine.Providers", "Workers", "PersonEnrichmentWorker.cs"));

        Assert.Contains("GetWorkLineageIdsByMediaAssetAsync", source);
        Assert.Contains("TV cast is", source);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MediaEngine.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
