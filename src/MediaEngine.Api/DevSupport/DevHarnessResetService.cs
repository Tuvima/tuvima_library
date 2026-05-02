using MediaEngine.Domain.Services;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Options;

namespace MediaEngine.Api.DevSupport;

public enum DevHarnessWipeScope
{
    GeneratedState,
    Full,
}

public sealed record DevHarnessResetResult(
    DevHarnessWipeScope Scope,
    IReadOnlyList<string> Details);

/// <summary>
/// Central reset service for development ingestion harness endpoints.
/// The default generated-state scope preserves unrelated watch-folder source files.
/// </summary>
public sealed class DevHarnessResetService
{
    public const string GeneratedStateScopeName = "generated-state";
    public const string FullScopeName = "full";

    private readonly IDatabaseConnection _db;
    private readonly IOptions<IngestionOptions> _options;
    private readonly IConfigurationLoader _configLoader;
    private readonly IIngestionEngine _ingestionEngine;
    private readonly ILogger<DevHarnessResetService> _logger;

    public DevHarnessResetService(
        IDatabaseConnection db,
        IOptions<IngestionOptions> options,
        IConfigurationLoader configLoader,
        IIngestionEngine ingestionEngine,
        ILogger<DevHarnessResetService> logger)
    {
        _db = db;
        _options = options;
        _configLoader = configLoader;
        _ingestionEngine = ingestionEngine;
        _logger = logger;
    }

    public static DevHarnessWipeScope ParseScope(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DevHarnessWipeScope.GeneratedState;

        return value.Trim().ToLowerInvariant() switch
        {
            GeneratedStateScopeName or "generated" or "state" => DevHarnessWipeScope.GeneratedState,
            FullScopeName or "all" or "dangerous-full" => DevHarnessWipeScope.Full,
            _ => throw new ArgumentException(
                $"Unknown wipe scope '{value}'. Use '{GeneratedStateScopeName}' or '{FullScopeName}'.",
                nameof(value)),
        };
    }

    public async Task<DevHarnessResetResult> WipeAsync(
        DevHarnessWipeScope scope,
        bool resumeWatcher,
        CancellationToken ct = default)
    {
        var details = new List<string>();

        PauseWatcher(details);

        try
        {
            WipeGeneratedLibraryState(details);

            if (scope == DevHarnessWipeScope.Full)
                WipeAllSourcePaths(details);
            else
                WipeKnownSeedFiles(details);

            await ResetDatabaseAsync(details, ct).ConfigureAwait(false);
            WipeRuntimeLogs(details);
        }
        finally
        {
            if (resumeWatcher)
                ResumeWatcher(details);
            else
                details.Add("Ingestion engine: FSW resume deferred");
        }

        return new DevHarnessResetResult(scope, details);
    }

    public void PauseWatcher(List<string>? details = null)
    {
        try
        {
            _ingestionEngine.PauseWatcher();
            _logger.LogInformation("[HarnessReset] Ingestion engine FSW paused");
            details?.Add("Ingestion engine: FSW paused");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HarnessReset] Failed to pause ingestion engine");
            details?.Add($"Ingestion engine pause: FAILED - {ex.Message}");
        }
    }

    public void ResumeWatcher(List<string>? details = null)
    {
        try
        {
            _ingestionEngine.ResumeWatcher();
            _logger.LogInformation("[HarnessReset] Ingestion engine FSW resumed");
            details?.Add("Ingestion engine: FSW resumed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HarnessReset] Failed to resume ingestion engine");
            details?.Add($"Ingestion engine resume: FAILED - {ex.Message}");
        }
    }

    private void WipeGeneratedLibraryState(List<string> details)
    {
        string? libraryRoot = _options.Value.LibraryRoot;
        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            details.Add("Library root: not configured - skipped");
            return;
        }

        var assetPathService = new AssetPathService(libraryRoot);
        WipePathWithReport(details, "Central artwork cache", assetPathService.LegacyImagesRoot);
        WipePathWithReport(details, "Central artwork and renditions", assetPathService.AssetsRoot);

        if (Directory.Exists(libraryRoot))
        {
            try
            {
                int count = WipeDirectoryContentsExcept(libraryRoot, ".data");
                details.Add($"Library output ({libraryRoot}): {count} items deleted");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HarnessReset] Failed to wipe library output");
                details.Add($"Library output ({libraryRoot}): FAILED - {ex.Message}");
            }
        }
        else
        {
            details.Add($"Library output ({libraryRoot}): directory does not exist - skipped");
        }
    }

    private void WipeKnownSeedFiles(List<string> details)
    {
        var seedFiles = DevSeedEndpoints.GetSeedFilePaths(_options, _configLoader);
        int deleted = 0;
        int missing = 0;

        foreach (string path in seedFiles)
        {
            if (!File.Exists(path))
            {
                missing++;
                continue;
            }

            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                deleted++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[HarnessReset] Failed to delete seed fixture {Path}", path);
                details.Add($"Seed fixture ({path}): FAILED - {ex.Message}");
            }
        }

        details.Add($"Seed fixtures: {deleted} known fixture file(s) deleted, {missing} absent");
    }

    private void WipeAllSourcePaths(List<string> details)
    {
        var libConfig = _configLoader.LoadLibraries();
        foreach (var lib in libConfig.Libraries)
        {
            var paths = lib.SourcePaths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList()
                        ?? [];
            if (paths.Count == 0 && !string.IsNullOrWhiteSpace(lib.SourcePath))
                paths.Add(lib.SourcePath);

            foreach (string srcPath in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(srcPath))
                    continue;

                try
                {
                    int count = WipeDirectoryContents(srcPath);
                    details.Add($"Library source ({srcPath}): {count} items deleted");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[HarnessReset] Failed to wipe source path {Path}", srcPath);
                    details.Add($"Library source ({srcPath}): FAILED - {ex.Message}");
                }
            }
        }

        string? watchDir = _options.Value.WatchDirectory;
        if (!string.IsNullOrWhiteSpace(watchDir) && Directory.Exists(watchDir))
        {
            try
            {
                int count = WipeDirectoryContents(watchDir);
                details.Add($"Watch folder ({watchDir}): {count} items deleted");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HarnessReset] Failed to wipe watch folder");
                details.Add($"Watch folder ({watchDir}): FAILED - {ex.Message}");
            }
        }
    }

    private async Task ResetDatabaseAsync(List<string> details, CancellationToken ct)
    {
        await _db.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            var conn = _db.Open();

            using (var fkOff = conn.CreateCommand())
            {
                fkOff.CommandText = "PRAGMA foreign_keys = OFF;";
                fkOff.ExecuteNonQuery();
            }

            var tables = new List<string>();
            using (var listCmd = conn.CreateCommand())
            {
                listCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
                using var reader = listCmd.ExecuteReader();
                while (reader.Read())
                    tables.Add(reader.GetString(0));
            }

            foreach (string table in tables)
            {
                ct.ThrowIfCancellationRequested();
                using var dropCmd = conn.CreateCommand();
                dropCmd.CommandText = $"DROP TABLE IF EXISTS [{table}];";
                dropCmd.ExecuteNonQuery();
            }

            using (var ftsCmd = conn.CreateCommand())
            {
                ftsCmd.CommandText = "DROP TABLE IF EXISTS search_index;";
                ftsCmd.ExecuteNonQuery();
            }

            using (var fkOn = conn.CreateCommand())
            {
                fkOn.CommandText = "PRAGMA foreign_keys = ON;";
                fkOn.ExecuteNonQuery();
            }

            using (var vacuumCmd = conn.CreateCommand())
            {
                vacuumCmd.CommandText = "VACUUM;";
                vacuumCmd.ExecuteNonQuery();
            }

            _db.InitializeSchema();
            _db.RunStartupChecks();

            details.Add($"Database: dropped {tables.Count} table(s) and reinitialized schema");
            _logger.LogInformation("[HarnessReset] Database reset: dropped {Count} table(s)", tables.Count);
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    private void WipeRuntimeLogs(List<string> details)
    {
        string logsPath = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        if (!Directory.Exists(logsPath))
            return;

        try
        {
            int count = WipeDirectoryContents(logsPath);
            details.Add($"Logs ({logsPath}): {count} items deleted");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HarnessReset] Failed to wipe logs");
            details.Add($"Logs ({logsPath}): FAILED - {ex.Message}");
        }
    }

    private void WipePathWithReport(List<string> details, string label, string path)
    {
        if (!Directory.Exists(path))
        {
            details.Add($"{label} ({path}): not found - skipped");
            return;
        }

        try
        {
            int count = WipeDirectoryContents(path);
            details.Add($"{label} ({path}): {count} items deleted");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HarnessReset] Failed to wipe {Label}", label);
            details.Add($"{label} ({path}): FAILED - {ex.Message}");
        }
    }

    private static int WipeDirectoryContents(string dirPath)
    {
        if (!Directory.Exists(dirPath))
            return 0;

        int count = 0;
        var dir = new DirectoryInfo(dirPath);
        foreach (FileInfo file in dir.GetFiles("*", SearchOption.AllDirectories))
        {
            try
            {
                file.Attributes = FileAttributes.Normal;
                file.Delete();
                count++;
            }
            catch
            {
                // Best-effort cleanup for dev harness state.
            }
        }

        foreach (DirectoryInfo sub in dir.GetDirectories())
        {
            try
            {
                sub.Delete(recursive: true);
                count++;
            }
            catch
            {
                // Best-effort cleanup for dev harness state.
            }
        }

        return count;
    }

    private static int WipeDirectoryContentsExcept(string dirPath, params string[] excludedChildNames)
    {
        if (!Directory.Exists(dirPath))
            return 0;

        var excluded = new HashSet<string>(excludedChildNames, StringComparer.OrdinalIgnoreCase);
        int count = 0;
        var dir = new DirectoryInfo(dirPath);

        foreach (FileInfo file in dir.GetFiles())
        {
            try
            {
                file.Attributes = FileAttributes.Normal;
                file.Delete();
                count++;
            }
            catch
            {
                // Best-effort cleanup for dev harness state.
            }
        }

        foreach (DirectoryInfo sub in dir.GetDirectories())
        {
            if (excluded.Contains(sub.Name))
                continue;

            try
            {
                sub.Delete(recursive: true);
                count++;
            }
            catch
            {
                // Best-effort cleanup for dev harness state.
            }
        }

        return count;
    }
}
