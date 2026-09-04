namespace MediaEngine.Web.Tests;

public sealed class ViewProfileSettingsUiTests
{
    [Fact]
    public void UsersSettings_UsesSharedControlsAndTruthfulViewManagement()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\UsersTab.razor");

        Assert.Contains("OpenViewPolicyDialogAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetViewProfilePolicyAsync", source, StringComparison.Ordinal);
        Assert.Contains("UpdateViewProfilePolicyAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetViewProfileSourcesAsync", source, StringComparison.Ordinal);
        Assert.Contains("Sources &amp; Devices", source, StringComparison.Ordinal);
        Assert.Contains("No persisted sources are attached.", source, StringComparison.Ordinal);
        Assert.Contains("No persisted devices are connected.", source, StringComparison.Ordinal);
        Assert.Contains("No placeholder status is shown.", source, StringComparison.Ordinal);
        Assert.Contains("Label=\"View enabled\"", source, StringComparison.Ordinal);
        Assert.Contains("Label=\"Access Shared View\"", source, StringComparison.Ordinal);
        Assert.Contains("Label=\"Include in Shared View\"", source, StringComparison.Ordinal);
        Assert.Contains("Label=\"Allow Gallery Sharing\"", source, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(source, "<AppSwitchRow"));
        Assert.DoesNotContain("MudSwitch", source, StringComparison.Ordinal);
        Assert.DoesNotContain("quota", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SourceKey", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientDeviceId", source, StringComparison.Ordinal);
        Assert.Contains("DeviceContext.IsMobile", source, StringComparison.Ordinal);
        Assert.DoesNotContain("there are no separate or simulated View AI controls", source, StringComparison.Ordinal);
        Assert.Contains("AddPersonalSpaceFolderAsync", source, StringComparison.Ordinal);
        Assert.Contains("DialogParameters<ServerFolderPicker>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("/settings/media-management", source, StringComparison.Ordinal);
        Assert.Contains("/settings/ai", source, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            relativePath)));
}
