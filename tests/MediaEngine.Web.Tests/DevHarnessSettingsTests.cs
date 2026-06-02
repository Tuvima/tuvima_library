namespace MediaEngine.Web.Tests;

public sealed class DevHarnessSettingsTests
{
    [Fact]
    public void SettingsShell_RendersTemporaryDevHarnessTab()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Web\Components\Pages\Settings.razor");
        var nav = ReadRepoFile(@"src\MediaEngine.Web\Models\ViewDTOs\SettingsNav.cs");

        Assert.Contains("case SettingsSection.DevHarness", source, StringComparison.Ordinal);
        Assert.Contains("<DevHarnessTab />", source, StringComparison.Ordinal);
        Assert.Contains("dev-harness", nav, StringComparison.Ordinal);
        Assert.Contains("Test Harness", nav, StringComparison.Ordinal);
    }

    [Fact]
    public void DevHarnessTab_UsesCoreHarnessEndpointsAndOptions()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\DevHarnessTab.razor");
        var client = ReadRepoFile(@"src\MediaEngine.Web\Services\Integration\IEngineApiClient.cs");
        var implementation = ReadRepoFile(@"src\MediaEngine.Web\Services\Integration\EngineApiClient.cs");

        Assert.Contains("/dev/full-test", source, StringComparison.Ordinal);
        Assert.Contains("/dev/reingest-library", source, StringComparison.Ordinal);
        Assert.Contains("/dev/integration-test", source, StringComparison.Ordinal);
        Assert.Contains("/dev/seed-library", source, StringComparison.Ordinal);
        Assert.Contains("/dev/wipe", source, StringComparison.Ordinal);
        Assert.Contains("wipeScope", source, StringComparison.Ordinal);
        Assert.Contains("generated-state", source, StringComparison.Ordinal);
        Assert.Contains("full", source, StringComparison.Ordinal);
        Assert.Contains("stages", source, StringComparison.Ordinal);
        Assert.Contains("types", source, StringComparison.Ordinal);

        foreach (var mediaType in new[] { "books", "audiobooks", "movies", "tv", "music", "comics" })
        {
            Assert.Contains(mediaType, source, StringComparison.Ordinal);
        }

        Assert.Contains("RunDevHarnessAsync", client, StringComparison.Ordinal);
        Assert.Contains("RunDevHarnessAsync", implementation, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaEngine.Storage", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath)));
}
