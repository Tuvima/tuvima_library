using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Manages loading/unloading of AI models with mutual exclusion.
/// Only one model is loaded at a time. Auto-unloads after idle timeout.
/// </summary>
public interface IModelLifecycleManager
{
    /// <summary>Load a model by role. Unloads any currently loaded model first.</summary>
    Task LoadModelAsync(AiModelRole role, CancellationToken ct = default);

    /// <summary>Unload the currently loaded model, freeing memory.</summary>
    Task UnloadCurrentAsync(CancellationToken ct = default);

    /// <summary>Which model role is currently loaded, or null if none.</summary>
    AiModelRole? CurrentlyLoadedRole { get; }

    /// <summary>Estimated memory used by the currently loaded model (MB).</summary>
    int CurrentMemoryUsageMB { get; }

    /// <summary>Overall AI health status.</summary>
    AiHealthStatus GetHealthStatus();

    /// <summary>
    /// Ensure a model role is loaded, loading it if necessary.
    /// Returns false if the model could not be loaded (not downloaded, error).
    /// </summary>
    Task<bool> EnsureLoadedAsync(AiModelRole role, CancellationToken ct = default);
}
