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
        Assert.Contains("Label=\"View enabled\"", source, StringComparison.Ordinal);
        Assert.Contains("Label=\"Access Shared View\"", source, StringComparison.Ordinal);
        Assert.Contains("Label=\"Include in Shared View\"", source, StringComparison.Ordinal);
        Assert.Contains("Label=\"Allow Gallery Sharing\"", source, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(source, "<AppSwitchRow"));
        Assert.DoesNotContain("MudSwitch", source, StringComparison.Ordinal);
        Assert.DoesNotContain("quota", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("there are no separate or simulated View AI controls", source, StringComparison.Ordinal);
        Assert.Contains("/settings/media-management/libraries", source, StringComparison.Ordinal);
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
