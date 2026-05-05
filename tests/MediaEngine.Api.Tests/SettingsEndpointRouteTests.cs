using System.IO;

namespace MediaEngine.Api.Tests;

public sealed class SettingsEndpointRouteTests
{
    [Fact]
    public void OrganizationTemplatePreview_IsReadOnlyEndpointBesideExplicitSave()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\SettingsEndpoints.cs"));

        Assert.Contains("grp.MapPost(\"/organization-template/preview\"", source, StringComparison.Ordinal);
        Assert.Contains(".WithName(\"PreviewOrganizationTemplate\")", source, StringComparison.Ordinal);
        Assert.Contains("grp.MapPut(\"/organization-template\"", source, StringComparison.Ordinal);
        Assert.Contains("configLoader.SaveCore(core);", source, StringComparison.Ordinal);

        var previewStart = source.IndexOf("grp.MapPost(\"/organization-template/preview\"", StringComparison.Ordinal);
        var putStart = source.IndexOf("grp.MapPut(\"/organization-template\"", StringComparison.Ordinal);
        Assert.True(previewStart >= 0);
        Assert.True(putStart > previewStart);
        Assert.DoesNotContain("SaveCore", source[previewStart..putStart], StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileEndpoints_ExposePlaybackSettingsRoutes()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\ProfileEndpoints.cs"));

        Assert.Contains("MapGet(\"/{id:guid}/settings/playback\"", source, StringComparison.Ordinal);
        Assert.Contains("MapPut(\"/{id:guid}/settings/playback\"", source, StringComparison.Ordinal);
        Assert.Contains("IUserPlaybackSettingsService", source, StringComparison.Ordinal);
        Assert.Contains("GetOrCreateDefaultsAsync", source, StringComparison.Ordinal);
        Assert.Contains("UpdateAsync(id, request", source, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
