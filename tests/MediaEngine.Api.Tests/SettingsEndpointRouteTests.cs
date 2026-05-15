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

    [Fact]
    public void SettingsEndpoints_ExposePhase6AdminControlRoutes()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\SettingsEndpoints.cs"));

        Assert.Contains("grp.MapGet(\"/folders\"", source, StringComparison.Ordinal);
        Assert.Contains("grp.MapPut(\"/folders\"", source, StringComparison.Ordinal);
        Assert.Contains("fileWatcher.UpdateDirectory", source, StringComparison.Ordinal);
        Assert.Contains("grp.MapPost(\"/test-path\"", source, StringComparison.Ordinal);
        Assert.Contains("HasRead", source, StringComparison.Ordinal);
        Assert.Contains("HasWrite", source, StringComparison.Ordinal);
        Assert.Contains("grp.MapGet(\"/providers\"", source, StringComparison.Ordinal);
        Assert.Contains("grp.MapGet(\"/providers/health\"", source, StringComparison.Ordinal);
        Assert.Contains("grp.MapPost(\"/providers/{name}/test\"", source, StringComparison.Ordinal);
        Assert.Contains("grp.MapPut(\"/providers/{name}/config\"", source, StringComparison.Ordinal);
        Assert.Contains("grp.MapGet(\"/pipelines\"", source, StringComparison.Ordinal);
        Assert.Contains("grp.MapPut(\"/pipelines\"", source, StringComparison.Ordinal);
        Assert.Contains("grp.MapGet(\"/media-types\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderSettingsStatus_DoesNotRunLiveExternalProbesOnPageLoad()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\SettingsEndpoints.cs"));
        var start = source.IndexOf("grp.MapGet(\"/providers\", async", StringComparison.Ordinal);
        var end = source.IndexOf(".WithName(\"GetProviderStatus\")", StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        var endpoint = source[start..end];

        Assert.Contains("healthRepo.GetAllAsync", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("IHttpClientFactory", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("SendAsync", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("GetBaseUrlForProvider", endpoint, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
