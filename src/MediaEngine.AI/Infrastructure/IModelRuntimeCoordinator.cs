using MediaEngine.Domain.Enums;

namespace MediaEngine.AI.Infrastructure;

/// <summary>
/// Coordinates model lifetime with active inference. A lease keeps the selected
/// model loaded and prevents another role from replacing it until inference ends.
/// </summary>
public interface IModelRuntimeCoordinator
{
    ValueTask<IAsyncDisposable> AcquireInferenceLeaseAsync(
        AiModelRole role,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers an owner that can release native resources for a loaded role.
    /// The callback is awaited before the lifecycle state is changed to Ready.
    /// </summary>
    IDisposable RegisterModelDisposer(
        Func<AiModelRole, CancellationToken, ValueTask> disposer);
}
