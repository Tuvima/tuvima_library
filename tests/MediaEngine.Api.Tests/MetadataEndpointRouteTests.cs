namespace MediaEngine.Api.Tests;

public sealed class MetadataEndpointRouteTests
{
    [Fact]
    public void ClaimHistoryEndpoint_DelegatesReadProjectionToReadService()
    {
        var endpointSource = ReadSource("src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs");
        var serviceSource = ReadSource("src/MediaEngine.Api/Services/ReadServices/MetadataClaimHistoryReadService.cs");
        var registrations = ReadSource("src/MediaEngine.Api/DependencyInjection/ApiReadServiceCollectionExtensions.cs");

        var start = endpointSource.IndexOf("group.MapGet(\"/claims/{entityId:guid}\"", StringComparison.Ordinal);
        var end = endpointSource.IndexOf(".WithName(\"GetClaimHistory\")", start, StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.True(end > start);

        var route = endpointSource[start..end];
        Assert.Contains("IMetadataClaimHistoryReadService claimHistoryReadService", route, StringComparison.Ordinal);
        Assert.Contains("claimHistoryReadService.GetClaimHistoryAsync(entityId, ct)", route, StringComparison.Ordinal);
        Assert.DoesNotContain("IDatabaseConnection db", route, StringComparison.Ordinal);
        Assert.DoesNotContain("Query<", route, StringComparison.Ordinal);
        Assert.Contains(".RequireAnyRole()", endpointSource, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<IMetadataClaimHistoryReadService, MetadataClaimHistoryReadService>", registrations, StringComparison.Ordinal);
        Assert.Contains("SELECT ma.id", serviceSource, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
