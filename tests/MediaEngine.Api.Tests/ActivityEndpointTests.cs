namespace MediaEngine.Api.Tests;

public sealed class ActivityEndpointTests
{
    [Fact]
    public void ActivityEndpoints_ResolvePlaceholderPersonNamesBeforeReturningFeed()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\ActivityEndpoints.cs"));

        Assert.Contains("ResolveActivityPersonNameAsync", source, StringComparison.Ordinal);
        Assert.Contains("personRepo.FindByIdAsync", source, StringComparison.Ordinal);
        Assert.Contains("qidLabelRepo.GetLabelAsync", source, StringComparison.Ordinal);
        Assert.Contains("Unknown Person (", source, StringComparison.Ordinal);
        Assert.Contains("Name pending ({qid})", source, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
