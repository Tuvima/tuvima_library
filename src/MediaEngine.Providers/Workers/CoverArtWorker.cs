using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Workers;

/// <summary>
/// Handles cover art operations: download, pHash comparison, thumbnail + hero generation.
/// Extracted from HydrationPipelineService for single-responsibility.
/// </summary>
public sealed class CoverArtWorker
{
    private readonly ILogger<CoverArtWorker> _logger;

    public CoverArtWorker(ILogger<CoverArtWorker> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Downloads cover art from the provider URL, generates thumbnail and hero banner.
    /// </summary>
    public Task DownloadAndPersistAsync(Guid entityId, string? wikidataQid, CancellationToken ct = default)
    {
        // TODO: Wire to extracted CoverArtService (Phase 3 extraction)
        // For now, the existing HydrationPipelineService.PersistCoverFromUrlAsync handles this.
        _logger.LogDebug("CoverArtWorker.DownloadAndPersistAsync called for entity {Id} — delegated to legacy path", entityId);
        return Task.CompletedTask;
    }
}
