using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.PersonalMedia;

namespace MediaEngine.Api.Services.LocalAssets;

/// <summary>
/// Owns the single managed View root and derives profile/source directories
/// from stable identities. Display names never participate in path authority.
/// </summary>
public sealed class ViewStorageService(
    IConfigurationLoader configuration,
    IViewPersonalSpaceRepository spaces)
{
    public string GetRootPath()
    {
        var settings = configuration.LoadLibraries();
        if (!string.Equals(settings.SchemaVersion, "5.0", StringComparison.Ordinal))
            throw new InvalidOperationException("View storage requires libraries.json schema_version 5.0.");
        if (settings.Libraries.Any(library =>
                string.Equals(library.Kind, LibraryKinds.Personal, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "Personal libraries are obsolete. Configure view_storage and reindex each profile's Personal Space.");

        var storage = settings.StorageLocations.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, settings.ViewStorage.StorageLocationId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"View storage location '{settings.ViewStorage.StorageLocationId}' is not configured.");
        if (!storage.AllowWrite)
            throw new InvalidOperationException("The configured View storage location must allow writes.");

        var basePath = Path.GetFullPath(storage.Path);
        var relative = settings.ViewStorage.RelativeRoot?.Trim();
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            throw new InvalidOperationException("The View relative root must be a non-empty relative path.");
        var root = Path.GetFullPath(Path.Combine(basePath, relative));
        EnsureContained(basePath, root, "The View root must remain within its configured storage location.");
        return Path.TrimEndingDirectorySeparator(root);
    }

    public string GetProfileRoot(ViewPersonalSpace space) =>
        ResolveManagedRelativePath($"profiles/{space.OwnerProfileId:N}");

    public string GetSourcePath(ViewPersonalSpace space, ViewSource source)
    {
        if (source.StorageMode == ViewSourceStorageMode.Linked)
        {
            if (string.IsNullOrWhiteSpace(source.ExternalPath))
                throw new InvalidOperationException($"Linked View source '{source.Name}' has no external path.");
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(source.ExternalPath));
        }

        if (string.IsNullOrWhiteSpace(source.RelativePath))
            throw new InvalidOperationException($"Managed View source '{source.Name}' has no relative path.");
        return ResolveManagedRelativePath(source.RelativePath);
    }

    public async Task<ViewPersonalSpace> EnsurePersonalSpaceAsync(Guid ownerProfileId, CancellationToken ct = default)
    {
        if (ownerProfileId == Guid.Empty) throw new ArgumentException("Profile ID is required.", nameof(ownerProfileId));
        var space = await spaces.GetByOwnerAsync(ownerProfileId, ct)
            ?? await spaces.CreateAsync(ownerProfileId, Guid.NewGuid(), ct);
        Directory.CreateDirectory(GetProfileRoot(space));

        var policy = configuration.LoadLibraries().PersonalLibraryPolicy;
        if (policy.AllowBrowserUpload)
            await EnsureManagedSourceAsync(space, "Browser uploads", ViewSourceType.BrowserUpload,
                "builtin:browser-uploads", ct);
        return space;
    }

    public async Task<ViewSource> EnsureManagedSourceAsync(
        ViewPersonalSpace space,
        string name,
        ViewSourceType sourceType,
        string sourceKey,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        var existing = (await spaces.GetSourcesAsync(space.Id, ct)).FirstOrDefault(candidate =>
            string.Equals(candidate.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Directory.CreateDirectory(GetSourcePath(space, existing));
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var source = new ViewSource(
            id, space.Id, sourceType, name.Trim(), sourceKey.Trim(), null, now, now,
            ViewSourceStorageMode.Managed,
            $"profiles/{space.OwnerProfileId:N}/sources/{id:N}",
            ExternalPath: null,
            IncludeSubdirectories: true,
            Enabled: true);
        source = await spaces.UpsertSourceAsync(source, ct);
        Directory.CreateDirectory(GetSourcePath(space, source));
        return source;
    }

    public async Task<ViewSource> AddLinkedSourceAsync(
        ViewPersonalSpace space,
        string name,
        string path,
        bool includeSubdirectories,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!configuration.LoadLibraries().PersonalLibraryPolicy.AllowExistingFolderAttachment)
            throw new InvalidOperationException("Linking an existing folder to View is disabled by policy.");
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Linked View folder '{fullPath}' does not exist.");
        if (IsWithin(GetRootPath(), fullPath))
            throw new InvalidOperationException("Folders inside the managed View root are created as managed sources, not linked sources.");

        var sources = await spaces.GetSourcesAsync(space.Id, ct);
        if (sources.Any(candidate => candidate.StorageMode == ViewSourceStorageMode.Linked
            && !string.IsNullOrWhiteSpace(candidate.ExternalPath)
            && string.Equals(Path.GetFullPath(candidate.ExternalPath), fullPath, PathComparison)))
            throw new InvalidOperationException("This folder is already linked to the Personal Space.");

        var now = DateTimeOffset.UtcNow;
        return await spaces.UpsertSourceAsync(new ViewSource(
            Guid.NewGuid(), space.Id, ViewSourceType.Folder, name.Trim(), $"linked:{Guid.NewGuid():N}",
            null, now, now, ViewSourceStorageMode.Linked, RelativePath: null, ExternalPath: fullPath,
            IncludeSubdirectories: includeSubdirectories, Enabled: true), ct);
    }

    public async Task<ViewSource> ImportFolderAsync(
        ViewPersonalSpace space,
        string name,
        string sourcePath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!configuration.LoadLibraries().PersonalLibraryPolicy.AllowManagedStorage)
            throw new InvalidOperationException("Importing folders into managed View storage is disabled by policy.");
        var origin = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
        if (!Directory.Exists(origin))
            throw new DirectoryNotFoundException($"Import folder '{origin}' does not exist.");
        if (IsWithin(GetRootPath(), origin))
            throw new InvalidOperationException("A folder already inside the View root does not need to be imported.");

        var source = await EnsureManagedSourceAsync(
            space, name, ViewSourceType.Folder, $"import:{Guid.NewGuid():N}", ct);
        var destination = GetSourcePath(space, source);
        foreach (var file in Directory.EnumerateFiles(origin, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            var relative = Path.GetRelativePath(origin, file);
            var target = Path.GetFullPath(Path.Combine(destination, relative));
            EnsureContained(destination, target, "An imported file resolved outside its managed source folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await input.CopyToAsync(output, ct);
            File.SetLastWriteTimeUtc(target, info.LastWriteTimeUtc);
        }
        return source;
    }

    public async Task<IReadOnlyList<(ViewPersonalSpace Space, ViewSource Source, string Path)>>
        GetEnabledSourcesAsync(CancellationToken ct = default)
    {
        var result = new List<(ViewPersonalSpace, ViewSource, string)>();
        foreach (var space in await spaces.GetAllAsync(ct))
        {
            foreach (var source in (await spaces.GetSourcesAsync(space.Id, ct)).Where(candidate => candidate.Enabled))
                result.Add((space, source, GetSourcePath(space, source)));
        }
        return result;
    }

    public static bool Contains(string root, string path, bool includeSubdirectories = true)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        if (!IsWithin(fullRoot, fullPath)) return false;
        return includeSubdirectories
            || string.Equals(Path.GetDirectoryName(fullPath), fullRoot, PathComparison);
    }

    private string ResolveManagedRelativePath(string relative)
    {
        if (Path.IsPathRooted(relative))
            throw new InvalidOperationException("Managed View paths must be relative to the View root.");
        var root = GetRootPath();
        var result = Path.GetFullPath(Path.Combine(root, relative));
        EnsureContained(root, result, "Managed View paths must remain within the View root.");
        return Path.TrimEndingDirectorySeparator(result);
    }

    private static void EnsureContained(string root, string path, string message)
    {
        if (!IsWithin(root, path)) throw new InvalidOperationException(message);
    }

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison)
            && !Path.IsPathRooted(relative);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
