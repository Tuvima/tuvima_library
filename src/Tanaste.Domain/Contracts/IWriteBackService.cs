namespace Tanaste.Domain.Contracts;

/// <summary>
/// Writes resolved canonical metadata back into the physical media file.
/// Implementations resolve the file path, load canonical values, select the
/// appropriate tagger, and optionally create a backup before modifying.
///
/// Called from:
/// <list type="bullet">
///   <item><c>HydrationPipelineService</c> after Stage 1 (auto-match) and Stage 2 (universe enrichment).</item>
///   <item><c>MetadataEndpoints</c> after manual override (<c>PUT /metadata/{entityId}/override</c>).</item>
/// </list>
/// </summary>
public interface IWriteBackService
{
    /// <summary>
    /// Writes current canonical metadata into the physical file for the given asset.
    /// No-op if write-back is disabled or no tagger supports the file format.
    /// </summary>
    /// <param name="assetId">The <c>media_assets.id</c> identifying the file.</param>
    /// <param name="trigger">The write-back trigger (e.g. "auto_match", "manual_override", "universe_enrichment").</param>
    /// <param name="ct">Cancellation token.</param>
    Task WriteMetadataAsync(Guid assetId, string trigger, CancellationToken ct = default);
}
