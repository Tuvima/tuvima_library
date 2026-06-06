namespace MediaEngine.Ingestion.Tests;

public sealed class IngestionEngineReliabilityGuardrailTests
{
    [Fact]
    public void IngestionEngine_UsesInjectedScoringConfiguration()
    {
        var source = ReadIngestionEngineSource();

        Assert.DoesNotContain("new ScoringConfiguration()", source, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(source, "Configuration   = _scoringConfig") +
                        CountOccurrences(source, "Configuration           = _scoringConfig"));
    }

    [Fact]
    public void IngestionEngine_PollingUsesEveryEffectiveWatchDirectory()
    {
        var source = ReadIngestionEngineSource();
        var pollStart = source.IndexOf("private async Task PollWatchDirectoryAsync", StringComparison.Ordinal);
        var pollEnd = source.IndexOf("// Re-organize already-ingested files", pollStart, StringComparison.Ordinal);
        Assert.True(pollStart >= 0 && pollEnd > pollStart);

        var pollSource = source[pollStart..pollEnd];
        Assert.Contains("_options.EffectiveWatchDirectories", pollSource, StringComparison.Ordinal);
        Assert.Contains("foreach (var watchDirectory in watchDirectories)", pollSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.EnumerateFiles(\r\n                             _options.WatchDirectory", pollSource, StringComparison.Ordinal);
    }

    [Fact]
    public void IngestionEngine_FswBufferFlushIsTaskBasedAndFingerprintAware()
    {
        var source = ReadIngestionEngineSource();

        Assert.DoesNotContain("async void FlushFswBuffer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult()", source, StringComparison.Ordinal);
        Assert.Contains("private async Task FlushFswBufferAsync", source, StringComparison.Ordinal);
        Assert.Contains("PauseWatcherAsync", source, StringComparison.Ordinal);
        Assert.Contains("_queuedFingerprints", source, StringComparison.Ordinal);
        Assert.Contains("_activePaths", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_enqueuedPaths", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IngestionEngine_UsesHashCacheAndRetryableLockProbeFailures()
    {
        var source = ReadIngestionEngineSource();

        Assert.Contains("ComputeHashWithCacheAsync", source, StringComparison.Ordinal);
        Assert.Contains("_fileHashCache.TryGetAsync", source, StringComparison.Ordinal);
        Assert.Contains("_fileHashCache.UpsertAsync", source, StringComparison.Ordinal);
        Assert.Contains("MarkRetryableOperationAsync", source, StringComparison.Ordinal);
        Assert.Contains("MarkInterruptedOperationAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("await NoResultOperationAsync(durableOperation, candidate.FailureReason", source, StringComparison.Ordinal);
    }

    private static string ReadIngestionEngineSource()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "MediaEngine.Ingestion",
            "IngestionEngine.cs"));

        return File.ReadAllText(path);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
