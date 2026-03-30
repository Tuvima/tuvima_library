using MediaEngine.AI.Configuration;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Infrastructure;

/// <summary>
/// Manages loading/unloading of AI models with mutual exclusion.
/// Only one model is loaded at a time. Auto-unloads after idle timeout.
/// </summary>
public sealed class ModelLifecycleManager : IModelLifecycleManager, IDisposable
{
    private readonly AiSettings _settings;
    private readonly ModelInventory _inventory;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ModelLifecycleManager> _logger;

    private readonly SemaphoreSlim _modelLock = new(1, 1);
    private AiModelRole? _currentRole;
    private int _currentMemoryMB;
    private Timer? _idleTimer;
    private DateTimeOffset _lastAccessTime = DateTimeOffset.UtcNow;

    public ModelLifecycleManager(
        AiSettings settings,
        ModelInventory inventory,
        IEventPublisher eventPublisher,
        ILogger<ModelLifecycleManager> logger)
    {
        _settings = settings;
        _inventory = inventory;
        _eventPublisher = eventPublisher;
        _logger = logger;

        // Start the idle unload timer (checks every 30 seconds).
        _idleTimer = new Timer(CheckIdleUnload, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public AiModelRole? CurrentlyLoadedRole => _currentRole;
    public int CurrentMemoryUsageMB => _currentMemoryMB;

    public async Task LoadModelAsync(AiModelRole role, CancellationToken ct = default)
    {
        await _modelLock.WaitAsync(ct);
        try
        {
            if (_currentRole == role)
            {
                _lastAccessTime = DateTimeOffset.UtcNow;
                return; // Already loaded.
            }

            // Unload current model if any.
            if (_currentRole.HasValue)
            {
                await UnloadInternalAsync();
            }

            var state = _inventory.GetState(role);
            if (state != AiModelState.Ready)
            {
                throw new InvalidOperationException(
                    $"Cannot load model {role}: state is {state} (must be Ready)");
            }

            var modelPath = _inventory.GetModelPath(role);
            var definition = _inventory.GetDefinition(role);

            _logger.LogInformation("Loading model {Role} from {Path}...", role, modelPath);
            _inventory.SetState(role, AiModelState.Loaded);

            // TODO (Sprint 2): Actual LLamaSharp/Whisper model loading.
            // For Sprint 1, we mark the state and track memory.

            _currentRole = role;
            _currentMemoryMB = definition.SizeMB;
            _lastAccessTime = DateTimeOffset.UtcNow;

            _logger.LogInformation("Model {Role} loaded (~{MB} MB)", role, _currentMemoryMB);

            await _eventPublisher.PublishAsync(SignalREvents.ModelStateChanged, new
            {
                Role = role.ToString(),
                OldState = AiModelState.Ready.ToString(),
                NewState = AiModelState.Loaded.ToString(),
            }, ct);
        }
        finally
        {
            _modelLock.Release();
        }
    }

    public async Task UnloadCurrentAsync(CancellationToken ct = default)
    {
        await _modelLock.WaitAsync(ct);
        try
        {
            if (_currentRole.HasValue)
            {
                await UnloadInternalAsync();
            }
        }
        finally
        {
            _modelLock.Release();
        }
    }

    public async Task<bool> EnsureLoadedAsync(AiModelRole role, CancellationToken ct = default)
    {
        var state = _inventory.GetState(role);
        if (state == AiModelState.Loaded && _currentRole == role)
        {
            _lastAccessTime = DateTimeOffset.UtcNow;
            return true;
        }

        if (state != AiModelState.Ready && state != AiModelState.Loaded)
        {
            _logger.LogWarning("Cannot ensure model {Role}: state is {State}", role, state);
            return false;
        }

        try
        {
            await LoadModelAsync(role, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load model {Role}", role);
            return false;
        }
    }

    public AiHealthStatus GetHealthStatus()
    {
        var profile = _settings.HardwareProfile;
        bool gpuAvailable = profile.BenchmarkedAt.HasValue
            && !string.Equals(profile.Backend, "cpu", StringComparison.OrdinalIgnoreCase);

        return new AiHealthStatus
        {
            Models        = _inventory.GetAllStatuses(),
            MemoryUsedMB  = _currentMemoryMB,
            MemoryLimitMB = 3000, // TODO: read from memory profile config
            GpuAvailable  = gpuAvailable,
            MemoryProfile = "conservative", // TODO: read from config
            IsReady       = _inventory.AreAllReady(),
        };
    }

    private async Task UnloadInternalAsync()
    {
        if (!_currentRole.HasValue) return;

        var role = _currentRole.Value;
        _logger.LogInformation("Unloading model {Role}...", role);

        // TODO (Sprint 2): Actual LLamaSharp/Whisper model disposal.

        _inventory.SetState(role, AiModelState.Ready);
        _currentRole = null;
        _currentMemoryMB = 0;

        await _eventPublisher.PublishAsync(SignalREvents.ModelStateChanged, new
        {
            Role = role.ToString(),
            OldState = AiModelState.Loaded.ToString(),
            NewState = AiModelState.Ready.ToString(),
        }, CancellationToken.None);
    }

    private void CheckIdleUnload(object? state)
    {
        if (!_currentRole.HasValue) return;

        var idleTime = DateTimeOffset.UtcNow - _lastAccessTime;
        if (idleTime.TotalSeconds >= _settings.IdleUnloadSeconds)
        {
            _logger.LogInformation("Model {Role} idle for {Seconds}s — auto-unloading",
                _currentRole, (int)idleTime.TotalSeconds);

            // Fire and forget — the timer callback can't be async.
            _ = Task.Run(async () =>
            {
                try { await UnloadCurrentAsync(); }
                catch (Exception ex) { _logger.LogError(ex, "Auto-unload failed"); }
            });
        }
    }

    public void Dispose()
    {
        _idleTimer?.Dispose();
        _modelLock.Dispose();
    }
}
