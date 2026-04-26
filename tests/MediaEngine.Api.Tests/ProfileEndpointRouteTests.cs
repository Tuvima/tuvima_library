namespace MediaEngine.Api.Tests;

public sealed class ProfileEndpointRouteTests
{
    [Fact]
    public void ProfileEndpoints_ExposeUserOverviewWithoutUsingUnfilteredActivity()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\ProfileEndpoints.cs"));

        Assert.Contains("group.MapGet(\"/{id:guid}/overview\"", source, StringComparison.Ordinal);
        Assert.Contains("GetProfileOverview", source, StringComparison.Ordinal);
        Assert.Contains("GetRecentByProfileAsync(id, 20, ct)", source, StringComparison.Ordinal);
        Assert.Contains("GetRecentAsync(id, 50, ct)", source, StringComparison.Ordinal);
        Assert.Contains("Results.NotFound($\"Profile '{id}' not found.\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetSystemStatus", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemActivityRepository_HasProfileScopedQuery()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Storage\SystemActivityRepository.cs"));

        Assert.Contains("GetRecentByProfileAsync", source, StringComparison.Ordinal);
        Assert.Contains("WHERE  profile_id = @profileId", source, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath)
    {
        var baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {baseDir}");
    }
}
