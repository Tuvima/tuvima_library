using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MediaEngine.Domain;
using MediaEngine.Domain.Capabilities;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Contracts.Realtime;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Detection;
using MediaEngine.Ingestion.Models;
using MediaEngine.Ingestion.Services;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Models;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Helpers;
using MediaEngine.Processors.Contracts;

namespace MediaEngine.Ingestion;

public sealed partial class IngestionEngine
{
    private async Task HandleDeletedAsync(IngestionCandidate candidate, CancellationToken ct)
    {
        _logger.LogInformation("File deleted: {Path}", candidate.Path);

        await DeleteHashCacheEntryAsync(candidate.Path, ct).ConfigureAwait(false);

        // Look up the asset by its stored file path.
        // The file is gone so we can't hash it, but file_path_root is still in the DB.
        var asset = await _assetRepo.FindByPathRootAsync(candidate.Path, ct)
                                    .ConfigureAwait(false);

        if (asset is null)
        {
            _logger.LogDebug(
                "No asset record found for deleted path {Path} — nothing to orphan.",
                candidate.Path);
            return;
        }

        await _assetRepo.UpdateStatusAsync(asset.Id, Domain.Enums.AssetStatus.Orphaned, ct)
                        .ConfigureAwait(false);

        _logger.LogInformation(
            "Asset {AssetId} marked Orphaned (file no longer exists at {Path}).",
            asset.Id, candidate.Path);

        await SafePublishAsync(
            SignalREvents.MediaRemoved,
            new MediaRemovedEvent(asset.Id, candidate.Path, "Orphaned"),
            ct).ConfigureAwait(false);
    }

    private async Task<HashLookupResult> ComputeHashWithCacheAsync(string filePath, CancellationToken ct)
    {
        var info = new FileInfo(filePath);
        var absolutePath = Path.GetFullPath(filePath);
        var mtimeUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);

        if (_hashStageDependencies.FileHashCache is not null)
        {
            try
            {
                var cachedHash = await _hashStageDependencies.FileHashCache.TryGetAsync(
                    absolutePath,
                    info.Length,
                    mtimeUtc,
                    ct).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(cachedHash))
                {
                    return new HashLookupResult(new HashResult
                    {
                        FilePath = absolutePath,
                        Hex = cachedHash,
                        FileSize = info.Length,
                        Elapsed = TimeSpan.Zero,
                    }, CacheHit: true);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Hash cache lookup failed for {Path}; computing hash", absolutePath);
            }
        }

        var hash = await _hasher.ComputeAsync(absolutePath, ct).ConfigureAwait(false);

        if (_hashStageDependencies.FileHashCache is not null)
        {
            try
            {
                await _hashStageDependencies.FileHashCache.UpsertAsync(
                    absolutePath,
                    hash.FileSize,
                    mtimeUtc,
                    hash.Hex,
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Hash cache upsert failed for {Path}", absolutePath);
            }
        }

        return new HashLookupResult(hash, CacheHit: false);
    }

    private async Task DeleteHashCacheEntryAsync(string path, CancellationToken ct)
    {
        if (_hashStageDependencies.FileHashCache is null)
            return;

        try
        {
            await _hashStageDependencies.FileHashCache.DeleteAsync(Path.GetFullPath(path), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to remove hash cache entry for {Path}", path);
        }
    }

    // =========================================================================
    // Dry-run simulation
    // =========================================================================

}
