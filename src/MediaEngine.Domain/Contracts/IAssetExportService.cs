namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Keeps optional local artwork exports in sync with the canonical central asset store.
/// </summary>
public interface IAssetExportService
{
    /// <summary>
    /// Reconcile local export state for one artwork owner + type pair.
    /// </summary>
    Task ReconcileArtworkAsync(
        string entityId,
        string entityType,
        string assetType,
        CancellationToken ct = default);

    /// <summary>
    /// Remove any managed local export for one artwork owner + type pair.
    /// </summary>
    Task ClearArtworkExportAsync(
        string entityId,
        string entityType,
        string assetType,
        CancellationToken ct = default);

    /// <summary>
    /// Reconcile every tracked artwork slot. Used at startup after legacy migration.
    /// </summary>
    Task ReconcileAllArtworkAsync(CancellationToken ct = default);
}
