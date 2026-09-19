using MediaEngine.Domain.Contracts;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;
using Microsoft.Extensions.Options;

namespace MediaEngine.Api.DevSupport;

public sealed record DevelopmentTestResult(
    string Message,
    int FilesCreated,
    IReadOnlyList<string> MediaTypes,
    string FixtureSet,
    IReadOnlyList<string> ScannedDirectories,
    DevHarnessResetResult Reset);

/// <summary>
/// Intent-level orchestration for the human-facing Development Tools page.
/// Fixture creation and discovery deliberately flow through the same lower-level
/// services used by the production ingestion system.
/// </summary>
public sealed class DevelopmentTestService(
    IOptions<IngestionOptions> options,
    IConfigurationLoader configLoader,
    IIngestionEngine ingestionEngine,
    DevHarnessResetService resetService,
    ILogger<DevelopmentTestService> logger)
{
    private static readonly string[] SupportedMediaTypes =
        ["books", "audiobooks", "movies", "tv", "music", "comics"];

    public async Task<DevelopmentTestResult> ResetAndSeedAsync(
        IEnumerable<string> selectedMediaTypes,
        DevelopmentFixtureSet fixtureSet,
        CancellationToken ct = default)
    {
        var mediaTypes = selectedMediaTypes
            .Select(NormalizeMediaType)
            .Where(type => SupportedMediaTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (mediaTypes.Count == 0)
        {
            throw new ArgumentException("Select at least one supported media type.", nameof(selectedMediaTypes));
        }

        var reset = await resetService.ResetLibraryDataAsync(resumeWatcher: false, ct).ConfigureAwait(false);
        var filesCreated = 0;
        IReadOnlyList<IngestionScanTarget> scanTargets = [];

        try
        {
            filesCreated = await DevSeedEndpoints.SeedAllAsync(
                options,
                configLoader,
                mediaTypes,
                logger,
                fixtureSet).ConfigureAwait(false);

            scanTargets = ResolveScanTargets(mediaTypes);
            if (scanTargets.Count > 0)
            {
                await ingestionEngine.ScanDirectories(scanTargets, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            await resetService.ResumeWatcherAsync(ct: ct).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Development reset and seed created {FileCount} {FixtureSet} fixture file(s) across {MediaTypes}",
            filesCreated,
            fixtureSet,
            string.Join(",", mediaTypes));

        return new DevelopmentTestResult(
            $"{filesCreated} test files created. Ingestion triggered.",
            filesCreated,
            SupportedMediaTypes.Where(mediaTypes.Contains).ToArray(),
            fixtureSet.ToString(),
            scanTargets.Select(target => target.Path).ToArray(),
            reset);
    }

    public Task<DevHarnessResetResult> ResetLibraryDataAsync(CancellationToken ct = default) =>
        resetService.ResetLibraryDataAsync(resumeWatcher: true, ct);

    public Task<DevHarnessResetResult> FactoryResetAsync(CancellationToken ct = default) =>
        resetService.FactoryResetAsync(resumeWatcher: true, ct);

    private IReadOnlyList<IngestionScanTarget> ResolveScanTargets(HashSet<string> mediaTypes)
    {
        var targets = configLoader.LoadLibraries().Libraries
            .Where(library =>
                mediaTypes.Contains(NormalizeMediaType(library.Category))
                || library.MediaTypes.Any(type => mediaTypes.Contains(NormalizeMediaType(type))))
            .SelectMany(library => library.ScannableSources.Select(source =>
                new IngestionScanTarget(Path.GetFullPath(source.Path), source.IncludeSubdirectories)))
            .Where(target => Directory.Exists(target.Path))
            .GroupBy(target => target.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => new IngestionScanTarget(
                group.Key,
                group.Any(target => target.IncludeSubdirectories)))
            .ToList();

        return targets
            .Where(target => !targets.Any(other =>
                !string.Equals(target.Path, other.Path, StringComparison.OrdinalIgnoreCase)
                && other.IncludeSubdirectories
                && IsDirectoryAncestor(other.Path, target.Path)))
            .ToArray();
    }

    private static string NormalizeMediaType(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "book" or "books" or "ebook" or "epub" => "books",
            "audiobook" or "audiobooks" => "audiobooks",
            "movie" or "movies" => "movies",
            "tv" or "television" => "tv",
            "music" => "music",
            "comic" or "comics" => "comics",
            _ => string.Empty,
        };

    private static bool IsDirectoryAncestor(string candidateParent, string candidateChild)
    {
        var parent = Path.GetFullPath(candidateParent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var child = Path.GetFullPath(candidateChild)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }
}
