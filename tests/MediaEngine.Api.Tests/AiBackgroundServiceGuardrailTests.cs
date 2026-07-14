namespace MediaEngine.Api.Tests;

public sealed class AiBackgroundServiceGuardrailTests
{
    [Theory]
    [InlineData("VibeBatchService.cs")]
    [InlineData("SeriesAlignmentBackgroundService.cs")]
    [InlineData("TasteProfileBackgroundService.cs")]
    [InlineData("DescriptionIntelligenceBatchService.cs")]
    public void AiWorkers_UseExplicitDependenciesAndDurableFeatureOutcomes(string fileName)
    {
        var source = File.ReadAllText(GetRepoFilePath($@"src\MediaEngine.Api\Services\{fileName}"));

        Assert.DoesNotContain("IServiceScopeFactory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<", source, StringComparison.Ordinal);
        Assert.Contains("IAiFeaturePersistenceRepository", source, StringComparison.Ordinal);
        Assert.Contains("OperationCanceledException", source, StringComparison.Ordinal);
        Assert.Contains("RecordAiFeatureFailureAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalAiWorkers_UseAtomicFeatureReplacement()
    {
        foreach (var fileName in new[]
                 {
                     "VibeBatchService.cs",
                     "SeriesAlignmentBackgroundService.cs",
                     "DescriptionIntelligenceBatchService.cs",
                 })
        {
            var source = File.ReadAllText(GetRepoFilePath($@"src\MediaEngine.Api\Services\{fileName}"));
            Assert.Contains("ReplaceAiFeatureAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new CanonicalValue", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EngineRegistersFeatureAndTastePersistenceContracts()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Program.cs"));

        Assert.Contains("IAiFeaturePersistenceRepository", source, StringComparison.Ordinal);
        Assert.Contains("ITasteProfileRepository, TasteProfileRepository", source, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {AppContext.BaseDirectory}");
    }
}
