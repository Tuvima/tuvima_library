using System.Text.Json;
using MediaEngine.Domain.Services;
using MediaEngine.Ingestion.Models;
using Microsoft.Extensions.Options;

namespace MediaEngine.Api.DevSupport;

/// <summary>
/// Records exact files created by development fixture generation. Reset flows
/// consult this manifest instead of assuming ownership from a familiar filename.
/// </summary>
internal static class DevFixtureManifestStore
{
    private const string ManifestDirectoryName = "dev-harness";
    private const string ManifestFileName = "generated-fixtures.json";

    public static void Record(
        IOptions<IngestionOptions> options,
        IEnumerable<string> createdPaths,
        ILogger logger)
    {
        var manifestPath = ResolveManifestPath(options);
        if (manifestPath is null)
        {
            logger.LogWarning("Development fixture manifest was not written because LibraryRoot is not configured");
            return;
        }

        var normalizedPaths = createdPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedPaths.Length == 0)
        {
            return;
        }

        var entries = Read(options, logger)
            .Concat(normalizedPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Replace(options, entries, logger);
    }

    public static void Replace(
        IOptions<IngestionOptions> options,
        IEnumerable<string> paths,
        ILogger logger)
    {
        var entries = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (entries.Length == 0)
        {
            Delete(options, logger);
            return;
        }

        var manifestPath = ResolveManifestPath(options);
        if (manifestPath is null)
        {
            logger.LogWarning("Development fixture manifest was not written because LibraryRoot is not configured");
            return;
        }

        var directory = Path.GetDirectoryName(manifestPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(entries, MediaEngineJson.Indented));
    }

    public static IReadOnlyList<string> Read(
        IOptions<IngestionOptions> options,
        ILogger logger)
    {
        var manifestPath = ResolveManifestPath(options);
        if (manifestPath is null || !File.Exists(manifestPath))
        {
            return [];
        }

        try
        {
            var entries = JsonSerializer.Deserialize<string[]>(File.ReadAllText(manifestPath));
            return entries?
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            logger.LogWarning(ex, "Development fixture manifest could not be read from {Path}", manifestPath);
            return [];
        }
    }

    public static void Delete(IOptions<IngestionOptions> options, ILogger logger)
    {
        var manifestPath = ResolveManifestPath(options);
        if (manifestPath is null || !File.Exists(manifestPath))
        {
            return;
        }

        try
        {
            File.Delete(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Development fixture manifest could not be deleted from {Path}", manifestPath);
        }
    }

    private static string? ResolveManifestPath(IOptions<IngestionOptions> options)
    {
        var libraryRoot = options.Value.LibraryRoot;
        return string.IsNullOrWhiteSpace(libraryRoot)
            ? null
            : Path.Combine(
                Path.GetFullPath(libraryRoot),
                ".data",
                ManifestDirectoryName,
                ManifestFileName);
    }
}
