using MediaEngine.Domain.Configuration;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Services;

/// <summary>Applies the library's naming policy to ingestion and managed promotion.</summary>
public static class LibraryOrganizationPath
{
    public static string Calculate(IFileOrganizer organizer, IngestionCandidate candidate,
        LibraryFolderEntry? library, string standardTemplate)
    {
        if (library?.OrganizationMode == LibraryOrganizationModes.KeepOriginalNames)
        {
            var source = library.Sources.Where(source =>
                    PathSafety.IsContainedBy(Path.GetFullPath(candidate.Path), Path.GetFullPath(source.Path)))
                .OrderByDescending(source => source.Path.Length).FirstOrDefault();
            if (source is null)
            {
                return Path.GetFileName(candidate.Path);
            }

            var root = source.Path;
            var staging = Path.Combine(root, ".data", "staging");
            if (PathSafety.IsContainedBy(Path.GetFullPath(candidate.Path), Path.GetFullPath(staging)))
            {
                root = staging;
            }

            return Path.GetRelativePath(root, candidate.Path);
        }

        var template = library?.OrganizationMode == LibraryOrganizationModes.Custom
            ? library.CustomOrganizationTemplate ?? throw new InvalidOperationException("A custom naming template is required.")
            : standardTemplate;
        return organizer.CalculatePath(candidate, template);
    }
}
