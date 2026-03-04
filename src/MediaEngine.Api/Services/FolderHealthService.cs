using Microsoft.Extensions.Options;
using MediaEngine.Domain.Contracts;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Api.Services;

/// <summary>
/// Background service that periodically checks accessibility of the Watch Folder
/// and Library Root, broadcasting <c>FolderHealthChanged</c> SignalR events only
/// when the status changes — so the Dashboard <c>LibrariesTab</c> can update its
/// green/red status dots in real-time.
///
/// Check interval: every 30 seconds (configurable via <c>MediaEngine:FolderHealthIntervalSeconds</c>).
/// </summary>
public sealed class FolderHealthService : BackgroundService
{
    private readonly IOptionsMonitor<IngestionOptions> _options;
    private readonly IEventPublisher                   _publisher;
    private readonly ILogger<FolderHealthService>      _logger;
    private readonly int                               _intervalSeconds;

    /// <summary>
    /// In-memory cache of the last-known health state per folder path.
    /// Only paths that change status trigger a SignalR broadcast.
    /// </summary>
    private readonly Dictionary<string, FolderState> _lastState = new(StringComparer.OrdinalIgnoreCase);

    public FolderHealthService(
        IOptionsMonitor<IngestionOptions> options,
        IEventPublisher                   publisher,
        IConfiguration                    config,
        ILogger<FolderHealthService>      logger)
    {
        _options          = options;
        _publisher        = publisher;
        _logger           = logger;
        _intervalSeconds  = config.GetValue("MediaEngine:FolderHealthIntervalSeconds", 30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "FolderHealthService started — checking every {Interval}s", _intervalSeconds);

        // Small initial delay to let the rest of the app start up.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckFoldersAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "FolderHealthService check cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
        }
    }

    private async Task CheckFoldersAsync(CancellationToken ct)
    {
        var opts = _options.CurrentValue;

        if (!string.IsNullOrWhiteSpace(opts.WatchDirectory))
            await CheckAndBroadcastAsync(opts.WatchDirectory, ct);

        if (!string.IsNullOrWhiteSpace(opts.LibraryRoot))
            await CheckAndBroadcastAsync(opts.LibraryRoot, ct);
    }

    private async Task CheckAndBroadcastAsync(string path, CancellationToken ct)
    {
        var current = ProbePath(path);

        // Only broadcast if state has actually changed (or first run).
        if (_lastState.TryGetValue(path, out var previous) && previous == current)
            return;

        _lastState[path] = current;

        _logger.LogDebug(
            "FolderHealthChanged: {Path} → Accessible={Accessible} Read={Read} Write={Write}",
            path, current.IsAccessible, current.HasRead, current.HasWrite);

        await _publisher.PublishAsync("FolderHealthChanged", new
        {
            path,
            is_accessible = current.IsAccessible,
            has_read      = current.HasRead,
            has_write     = current.HasWrite,
            checked_at    = DateTimeOffset.UtcNow,
        }, ct);
    }

    /// <summary>
    /// Probes a directory path for existence, read access, and write access.
    /// Matches the same logic used by <c>POST /settings/test-path</c>.
    /// </summary>
    private static FolderState ProbePath(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return new FolderState(false, false, false);

            // Read probe: can we enumerate the directory?
            bool hasRead;
            try
            {
                _ = Directory.EnumerateFileSystemEntries(path).FirstOrDefault();
                hasRead = true;
            }
            catch
            {
                hasRead = false;
            }

            // Write probe: can we create and delete a temp file?
            bool hasWrite;
            try
            {
                var testFile = Path.Combine(path, $".tuvima_probe_{Guid.NewGuid():N}");
                File.WriteAllBytes(testFile, []);
                File.Delete(testFile);
                hasWrite = true;
            }
            catch
            {
                hasWrite = false;
            }

            return new FolderState(true, hasRead, hasWrite);
        }
        catch
        {
            return new FolderState(false, false, false);
        }
    }

    private readonly record struct FolderState(bool IsAccessible, bool HasRead, bool HasWrite);
}
