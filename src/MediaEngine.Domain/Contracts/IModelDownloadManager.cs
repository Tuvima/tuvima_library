using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Downloads AI model files to the models directory with progress reporting.
/// Validates SHA-256 checksums after download. Reusable for setup wizard.
/// </summary>
public interface IModelDownloadManager
{
    /// <summary>Start downloading a model by its role. Returns immediately; progress via SignalR.</summary>
    Task StartDownloadAsync(AiModelRole role, CancellationToken ct = default);

    /// <summary>Cancel an in-progress download.</summary>
    Task CancelDownloadAsync(AiModelRole role, CancellationToken ct = default);

    /// <summary>Get download status for a specific model role.</summary>
    AiModelStatus GetStatus(AiModelRole role);

    /// <summary>Get download status for all model roles.</summary>
    IReadOnlyList<AiModelStatus> GetAllStatuses();

    /// <summary>Check if all required models are downloaded and verified.</summary>
    bool AreAllModelsReady();

    /// <summary>Delete a downloaded model file.</summary>
    Task DeleteModelAsync(AiModelRole role, CancellationToken ct = default);
}
