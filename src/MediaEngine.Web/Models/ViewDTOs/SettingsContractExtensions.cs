using MediaEngine.Contracts.Settings;

namespace MediaEngine.Web.Models.ViewDTOs;

public static class SettingsContractExtensions
{
    public static IReadOnlyList<string> GetEffectiveSourcePaths(this LibraryFolderDto library) =>
        library.Sources
            .Select(source => source.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static LibrarySourceDto? GetPrimaryDestination(this LibraryFolderDto library) =>
        string.IsNullOrWhiteSpace(library.PrimaryDestinationSourceId)
            ? null
            : library.Sources.FirstOrDefault(source => string.Equals(
                source.Id,
                library.PrimaryDestinationSourceId,
                StringComparison.OrdinalIgnoreCase));
}
