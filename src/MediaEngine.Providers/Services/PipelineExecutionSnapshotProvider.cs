using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Caches the configuration used on pipeline hot paths and atomically replaces
/// the complete snapshot only after a valid configuration reload.
/// </summary>
public sealed class PipelineExecutionSnapshotProvider : IPipelineExecutionSnapshotProvider, IDisposable
{
    private readonly IConfigurationLoader _loader;
    private readonly ILogger<PipelineExecutionSnapshotProvider> _logger;
    private PipelineExecutionSnapshot _current;
    private long _revision;
    private bool _disposed;

    public PipelineExecutionSnapshotProvider(
        IConfigurationLoader loader,
        ILogger<PipelineExecutionSnapshotProvider> logger)
    {
        _loader = loader;
        _logger = logger;
        _current = LoadSnapshot();
        _loader.ConfigurationChanged += OnConfigurationChanged;
    }

    public PipelineExecutionSnapshot Current => Volatile.Read(ref _current);

    private PipelineExecutionSnapshot LoadSnapshot() => new(
        Revision: Interlocked.Increment(ref _revision),
        LoadedAt: DateTimeOffset.UtcNow,
        Core: _loader.LoadCore(),
        Hydration: _loader.LoadHydration(),
        Pipelines: _loader.LoadPipelines(),
        Providers: _loader.LoadAllProviders());

    private void OnConfigurationChanged(object? sender, ConfigurationFileChangedEventArgs args)
    {
        if (!args.Applied)
        {
            _logger.LogWarning(
                args.Error,
                "Pipeline configuration reload rejected for {Path}; revision {Revision} remains active",
                args.RelativePath,
                Current.Revision);
            return;
        }

        try
        {
            var next = LoadSnapshot();
            Volatile.Write(ref _current, next);
            _logger.LogInformation(
                "Pipeline configuration revision {Revision} activated after {Path} changed",
                next.Revision,
                args.RelativePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Pipeline configuration snapshot rebuild failed after {Path}; revision {Revision} remains active",
                args.RelativePath,
                Current.Revision);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _loader.ConfigurationChanged -= OnConfigurationChanged;
    }
}
