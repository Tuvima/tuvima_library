using MediaEngine.Contracts.Settings;
using MediaEngine.Contracts.Setup;
using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Contracts;

namespace MediaEngine.Api.Services.Settings;

/// <summary>
/// Validates the configured catalogued-library sources through the same approved-root
/// boundary used by the interactive server folder picker.
/// </summary>
public sealed class SetupMediaLocationValidationService(
    IConfigurationLoader configuration,
    ServerFolderBrowserService folderBrowser)
{
    public SetupMediaLocationsDto Validate()
    {
        var libraries = configuration.LoadLibraries().Libraries
            .Where(library => library.Kind == LibraryKinds.Catalogued)
            .ToList();
        var sources = libraries.SelectMany(library => library.Sources).ToList();
        if (sources.Count == 0)
        {
            return new SetupMediaLocationsDto(
                false,
                0,
                0,
                "Add at least one readable media location before completing setup.");
        }

        var readable = 0;
        foreach (var library in libraries)
        {
            foreach (var source in library.Sources)
            {
                ServerFolderValidationResultDto validation;
                try
                {
                    validation = folderBrowser.Validate(new ValidateServerFolderRequest
                    {
                        ManualPath = source.Path,
                        CurrentSourceId = source.Id,
                        SelectionMode = source.ManagementMode == LibrarySourceManagementModes.ManagedByTuvima
                            ? ServerFolderSelectionModes.ManagedLibrary
                            : ServerFolderSelectionModes.ExistingLibrary,
                    });
                }
                catch (ServerFolderAccessException exception)
                {
                    return Failure(sources.Count, readable, source.Path, exception.Message);
                }

                if (validation.HasRead) readable++;
                if (!validation.CanSelect)
                {
                    var reason = validation.Issues.FirstOrDefault(issue =>
                        string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase))?.Message
                        ?? "This folder does not meet the selected access requirements.";
                    return Failure(sources.Count, readable, source.Path, reason);
                }
            }

            var primaryError = ValidatePrimaryDestination(library);
            if (primaryError is not null)
            {
                var firstManagedPath = library.Sources.FirstOrDefault(source =>
                    source.ManagementMode == LibrarySourceManagementModes.ManagedByTuvima)?.Path;
                return Failure(sources.Count, readable, firstManagedPath ?? library.Name, primaryError);
            }
        }

        var noun = sources.Count == 1 ? "location" : "locations";
        return new SetupMediaLocationsDto(
            true,
            sources.Count,
            readable,
            $"All {sources.Count} configured media {noun} passed server access and library-role validation.");
    }

    private static string? ValidatePrimaryDestination(LibraryFolderConfig library)
    {
        var managed = library.Sources
            .Where(source => source.ManagementMode == LibrarySourceManagementModes.ManagedByTuvima)
            .ToList();
        var primaryRoles = library.Sources
            .Where(source => source.Role == LibrarySourceRoles.PrimaryDestination)
            .ToList();

        if (managed.Count == 0)
        {
            return string.IsNullOrWhiteSpace(library.PrimaryDestinationSourceId) && primaryRoles.Count == 0
                ? null
                : "A read-only library cannot define a primary destination.";
        }

        if (primaryRoles.Count != 1)
            return "A library with managed folders must have exactly one primary destination.";

        var primary = primaryRoles[0];
        if (primary.ManagementMode != LibrarySourceManagementModes.ManagedByTuvima
            || primary.AccessMode != LibrarySourceAccessModes.Writable)
        {
            return "The primary destination must be a managed writable folder.";
        }

        return string.Equals(library.PrimaryDestinationSourceId, primary.Id, StringComparison.OrdinalIgnoreCase)
            ? null
            : "The library primary destination must reference its managed primary folder.";
    }

    private static SetupMediaLocationsDto Failure(
        int configured,
        int readable,
        string path,
        string reason) => new(
            false,
            configured,
            readable,
            $"Media location '{path}' needs attention: {reason}");
}
