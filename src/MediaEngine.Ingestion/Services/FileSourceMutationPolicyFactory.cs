using MediaEngine.Domain.Configuration;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Services;

/// <summary>
/// Converts the authoritative configuration model into the immutable snapshot
/// consumed by the mutation gate. Resolving global policy here keeps the gate
/// independent from mutable configuration and makes a plan reproducible.
/// </summary>
public static class FileSourceMutationPolicyFactory
{
    public static FileSourceMutationPolicy Create(
        IncomingSourceEntry source,
        bool allowDelete = false)
    {
        ArgumentNullException.ThrowIfNull(source);

        return CreateCore(
            $"incoming:{source.Id}",
            source.Id,
            source.Path,
            managed: true,
            writable: true,
            participates: source.AllowsRoutingMutation,
            writebackOverride: false,
            globalMetadataWritebackEnabled: false,
            allowDelete);
    }

    public static FileSourceMutationPolicy Create(
        LibraryFolderEntry library,
        LibrarySourceEntry source,
        bool globalMetadataWritebackEnabled = false,
        bool allowDelete = false)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(source);

        bool belongsToLibrary = library.Sources.Any(candidate =>
            string.Equals(candidate.Id, source.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Path, source.Path, StringComparison.OrdinalIgnoreCase));
        if (!belongsToLibrary)
            throw new ArgumentException("The source is not part of the supplied library.", nameof(source));

        bool managed = string.Equals(
            source.ManagementMode,
            LibrarySourceManagementModes.ManagedByTuvima,
            StringComparison.OrdinalIgnoreCase);
        bool participates = managed && source.ParticipatesInOrganization;

        return CreateCore(
            library.Id,
            source.Id,
            source.Path,
            managed,
            source.IsWritable,
            participates,
            source.WritebackOverride,
            globalMetadataWritebackEnabled,
            allowDelete);
    }

    public static FileSourceMutationPolicy Create(
        LibraryFolderConfig library,
        LibrarySourceConfig source,
        bool globalMetadataWritebackEnabled = false,
        bool allowDelete = false)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(source);

        bool belongsToLibrary = library.Sources.Any(candidate =>
            string.Equals(candidate.Id, source.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Path, source.Path, StringComparison.OrdinalIgnoreCase));
        if (!belongsToLibrary)
            throw new ArgumentException("The source is not part of the supplied library.", nameof(source));

        bool managed = string.Equals(
            source.ManagementMode,
            LibrarySourceManagementModes.ManagedByTuvima,
            StringComparison.OrdinalIgnoreCase);
        bool participates = managed && source.ParticipatesInOrganization;

        return CreateCore(
            library.Id,
            source.Id,
            source.Path,
            managed,
            source.IsWritable,
            participates,
            source.WritebackOverride,
            globalMetadataWritebackEnabled,
            allowDelete);
    }

    private static FileSourceMutationPolicy CreateCore(
        string libraryId,
        string sourceId,
        string sourcePath,
        bool managed,
        bool writable,
        bool participates,
        bool? writebackOverride,
        bool globalMetadataWritebackEnabled,
        bool allowDelete)
        => new()
        {
            LibraryId = libraryId,
            SourceId = sourceId,
            RootPath = sourcePath,
            ManagementMode = managed
                ? FileSourceManagementMode.ManagedByTuvima
                : FileSourceManagementMode.ExistingLibrary,
            IsWritable = writable,
            AllowMove = participates,
            AllowRename = participates,
            AllowMetadataWriteback = managed
                && (writebackOverride ?? globalMetadataWritebackEnabled),
            AllowDelete = participates && allowDelete,
            AllowAsDestination = participates,
        };
}
