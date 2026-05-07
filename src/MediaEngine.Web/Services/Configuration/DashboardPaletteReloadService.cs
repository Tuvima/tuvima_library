using MediaEngine.Web.Services.Theming;

namespace MediaEngine.Web.Services.Configuration;

public sealed class DashboardPaletteReloadService : IHostedService, IDisposable
{
    private readonly DashboardConfigurationReader _reader;
    private readonly ILogger<DashboardPaletteReloadService> _logger;
    private readonly string _palettePath;
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;

    public DashboardPaletteReloadService(
        DashboardConfigurationReader reader,
        IConfiguration configuration,
        ILogger<DashboardPaletteReloadService> logger)
    {
        _reader = reader;
        _logger = logger;
        var configDir = Environment.GetEnvironmentVariable("TUVIMA_CONFIG_DIR")
                        ?? configuration["MediaEngine:ConfigDirectory"]
                        ?? "config";
        _palettePath = Path.Combine(configDir, "ui", "palette.json");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_palettePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return Task.CompletedTask;

        _watcher = new FileSystemWatcher(directory, Path.GetFileName(_palettePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnPaletteChanged;
        _watcher.Created += OnPaletteChanged;
        _watcher.Renamed += OnPaletteRenamed;
        _debounceTimer = new Timer(_ => ReloadPalette(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    private void OnPaletteChanged(object sender, FileSystemEventArgs args) =>
        _debounceTimer?.Change(TimeSpan.FromMilliseconds(250), Timeout.InfiniteTimeSpan);

    private void OnPaletteRenamed(object sender, RenamedEventArgs args) =>
        OnPaletteChanged(sender, args);

    private void ReloadPalette()
    {
        try
        {
            PaletteProvider.Reload(_reader.LoadPalette());
            _logger.LogInformation("Dashboard palette reloaded from {PalettePath}", _palettePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dashboard palette reload rejected; keeping last-known-good values.");
        }
    }

    public void Dispose()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnPaletteChanged;
            _watcher.Created -= OnPaletteChanged;
            _watcher.Renamed -= OnPaletteRenamed;
            _watcher.Dispose();
        }

        _debounceTimer?.Dispose();
    }
}
