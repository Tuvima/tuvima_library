namespace MediaEngine.Web.Tests;

public sealed class ServerFolderPickerUiTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void SharedPicker_IsAUniversalServerVisibleFolderComponent()
    {
        var picker = Read("src/MediaEngine.Web/Components/Shared/ServerFolderPicker.razor");

        Assert.Contains("@namespace MediaEngine.Web.Components.Shared", picker, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public string SelectionMode", picker, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public string? CurrentSourceId", picker, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public IReadOnlyCollection<string>? AllowedStorageLocationIds", picker, StringComparison.Ordinal);
        Assert.Contains("GetServerFolderRootsAsync", picker, StringComparison.Ordinal);
        Assert.Contains("BrowseServerFoldersAsync", picker, StringComparison.Ordinal);
        Assert.Contains("ValidateServerFolderAsync", picker, StringComparison.Ordinal);
        Assert.Contains("Enter path manually", picker, StringComparison.Ordinal);
        Assert.Contains("Select this folder", picker, StringComparison.Ordinal);
        Assert.DoesNotContain("InputFile", picker, StringComparison.Ordinal);
    }

    [Fact]
    public void Picker_IsReusedAcrossLibraryImportAndPersonalSpaceFlows()
    {
        var libraries = Read("src/MediaEngine.Web/Components/Settings/LibrariesTab.razor");
        var wizard = Read("src/MediaEngine.Web/Components/Settings/AddLibraryWizard.razor");
        var users = Read("src/MediaEngine.Web/Components/Settings/UsersTab.razor");

        Assert.Contains("DialogParameters<ServerFolderPicker>", libraries, StringComparison.Ordinal);
        Assert.Contains("ServerFolderSelectionModes.Incoming", libraries, StringComparison.Ordinal);
        Assert.Contains("DialogParameters<ServerFolderPicker>", wizard, StringComparison.Ordinal);
        Assert.Contains("ServerFolderSelectionModes.ManagedLibrary", wizard, StringComparison.Ordinal);
        Assert.Contains("ServerFolderSelectionModes.ExistingLibrary", wizard, StringComparison.Ordinal);
        Assert.Contains("DialogParameters<ServerFolderPicker>", users, StringComparison.Ordinal);
        Assert.Contains("ServerFolderSelectionModes.PersonalSpaceManaged", libraries, StringComparison.Ordinal);
        Assert.Contains("ServerFolderSelectionModes.PersonalSpaceExisting", users, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredWizardAndLibraryDetailsExposeCanonicalFlow()
    {
        var settings = Read("src/MediaEngine.Web/Components/Pages/Settings.razor");
        var wizard = Read("src/MediaEngine.Web/Components/Settings/AddLibraryWizard.razor");
        var libraries = Read("src/MediaEngine.Web/Components/Settings/LibrariesTab.razor");

        Assert.Contains("IsLibraryRouteSegment", settings, StringComparison.Ordinal);
        Assert.Contains("<AddLibraryWizard />", settings, StringComparison.Ordinal);
        Assert.Contains("[SupplyParameterFromQuery(Name = \"tab\")] public string? DetailTab", settings, StringComparison.Ordinal);
        Assert.Contains("<LibrariesTab Subsection=\"@_activeSubsection\" DetailTab=\"@DetailTab\" />", settings, StringComparison.Ordinal);
        Assert.Contains("Select type", wizard, StringComparison.Ordinal);
        Assert.Contains("Add folders", wizard, StringComparison.Ordinal);
        Assert.Contains("Organization", wizard, StringComparison.Ordinal);
        Assert.Contains("Review", wizard, StringComparison.Ordinal);
        Assert.Contains("PrimaryDestinationSourceId", wizard, StringComparison.Ordinal);
        Assert.Contains("Overview", libraries, StringComparison.Ordinal);
        Assert.Contains("Folders", libraries, StringComparison.Ordinal);
        Assert.Contains("Organization", libraries, StringComparison.Ordinal);
        Assert.Contains("Advanced", libraries, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public string? DetailTab", libraries, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath));
}
