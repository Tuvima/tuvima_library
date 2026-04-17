using System.IO;

namespace MediaEngine.Api.Tests;

public sealed class LibraryEndpointRouteTests
{
    [Fact]
    public void LibraryEndpoints_ExposeWorkFeedForHomePage()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\LibraryEndpoints.cs"));

        Assert.Contains("group.MapGet(\"/works\", async (", source, StringComparison.Ordinal);
        Assert.Contains(".WithName(\"GetLibraryWorks\")", source, StringComparison.Ordinal);
        Assert.Contains(".Produces<List<LibraryWorkListItemDto>>(StatusCodes.Status200OK)", source, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
