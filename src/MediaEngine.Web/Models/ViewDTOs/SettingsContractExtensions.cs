using MediaEngine.Contracts.Settings;

namespace MediaEngine.Web.Models.ViewDTOs;

public static class SettingsContractExtensions
{
    public static IReadOnlyList<string> GetEffectiveWatchDirectories(this FolderSettingsDto settings) =>
        settings.WatchDirectories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<string> GetEffectiveSourcePaths(this LibraryFolderDto library) =>
        library.SourcePaths;
}
