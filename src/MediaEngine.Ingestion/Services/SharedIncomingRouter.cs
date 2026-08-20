using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Enums;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Services;

/// <summary>
/// Pure routing policy for files that arrived through a shared incoming source.
/// A media type is never guessed into one of several eligible libraries.
/// </summary>
public static class SharedIncomingRouter
{
    public static SharedIncomingRoutingResult Route(
        IntakeContext? intake,
        MediaType detectedMediaType,
        IReadOnlyList<LibraryFolderEntry> libraries)
    {
        ArgumentNullException.ThrowIfNull(libraries);

        if (!string.Equals(
                intake?.SourceKind,
                IntakeSourceKinds.SharedIncoming,
                StringComparison.OrdinalIgnoreCase))
        {
            return SharedIncomingRoutingResult.NotApplicable();
        }

        var sharedIntake = intake!;
        if (sharedIntake.HasDestinationHint)
        {
            var hinted = libraries.FirstOrDefault(library =>
                string.Equals(library.Id, sharedIntake.DestinationLibraryId, StringComparison.OrdinalIgnoreCase));
            if (hinted is null)
            {
                return SharedIncomingRoutingResult.Unresolved(
                    $"Explicit destination library '{sharedIntake.DestinationLibraryId}' is not configured.");
            }

            return IsPersonalView(hinted)
                ? UnsupportedPersonalView(hinted)
                : SharedIncomingRoutingResult.Routed(hinted, explicitHint: true);
        }

        var eligible = libraries
            .Where(library => library.AcceptedIntakeModes.Any(mode =>
                string.Equals(mode, LibraryIntakeModes.IncomingFolder, StringComparison.OrdinalIgnoreCase)))
            .Where(library => library.MediaTypes.Contains(detectedMediaType)
                || (IsPersonalView(library) && library.MediaTypes.Count == 0))
            .ToList();

        if (eligible.Count != 1)
        {
            return eligible.Count == 0
                ? SharedIncomingRoutingResult.Unresolved(
                    $"No library accepts incoming-folder intake for detected media type '{detectedMediaType}'.")
                : SharedIncomingRoutingResult.Unresolved(
                $"Multiple libraries accept incoming-folder intake for detected media type '{detectedMediaType}': "
                + string.Join(", ", eligible.Select(library => $"{library.Name} ({library.Id})"))
                + ". Choose an explicit destination before retrying.");
        }

        return IsPersonalView(eligible[0])
            ? UnsupportedPersonalView(eligible[0])
            : SharedIncomingRoutingResult.Routed(eligible[0], explicitHint: false);
    }

    private static bool IsPersonalView(LibraryFolderEntry library) =>
        string.Equals(library.Kind, LibraryKinds.Personal, StringComparison.OrdinalIgnoreCase)
        || string.Equals(library.Area, LibraryAreas.View, StringComparison.OrdinalIgnoreCase);

    private static SharedIncomingRoutingResult UnsupportedPersonalView(LibraryFolderEntry library) =>
        SharedIncomingRoutingResult.Unresolved(
            $"Personal View library '{library.Name}' accepts this incoming file, but shared incoming-to-View indexing is not available yet. "
            + "The file remains in the incoming source for review and is not sent through catalogue providers.");
}

public sealed record SharedIncomingRoutingResult
{
    public bool Applies { get; init; }

    public LibraryFolderEntry? Library { get; init; }

    public bool UsedExplicitHint { get; init; }

    public string? FailureReason { get; init; }

    public bool IsResolved => Applies && Library is not null;

    public static SharedIncomingRoutingResult NotApplicable() => new();

    public static SharedIncomingRoutingResult Routed(LibraryFolderEntry library, bool explicitHint) => new()
    {
        Applies = true,
        Library = library,
        UsedExplicitHint = explicitHint,
    };

    public static SharedIncomingRoutingResult Unresolved(string reason) => new()
    {
        Applies = true,
        FailureReason = reason,
    };
}
