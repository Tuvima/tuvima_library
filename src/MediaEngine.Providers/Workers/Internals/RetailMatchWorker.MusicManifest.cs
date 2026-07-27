using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
using MediaEngine.Providers.Services;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Workers;

public sealed partial class RetailMatchWorker
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> MusicAlbumManifestLocks = new();

    private async Task PersistAcceptedAppleAlbumManifestAsync(
        WorkLineage lineage,
        IReadOnlyDictionary<string, string> hints,
        IReadOnlyDictionary<string, string> bridgeIds,
        CancellationToken ct)
    {
        var rootValues = await _canonicalRepo.GetByEntityAsync(lineage.TargetForParentScope, ct)
            .ConfigureAwait(false);
        var collectionId = bridgeIds.GetValueOrDefault(BridgeIdKeys.AppleMusicCollectionId)
            ?? rootValues.FirstOrDefault(value =>
                string.Equals(value.Key, BridgeIdKeys.AppleMusicCollectionId, StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(collectionId))
            return;

        var appleProvider = _providers.FirstOrDefault(provider =>
            string.Equals(provider.Name, "apple_api", StringComparison.OrdinalIgnoreCase));
        if (appleProvider is null)
            return;

        await PersistAppleAlbumManifestAsync(
                lineage,
                collectionId,
                hints.GetValueOrDefault(MetadataFieldConstants.Album),
                GetMusicCreatorHint(hints),
                appleProvider.ProviderId,
                ct)
            .ConfigureAwait(false);
    }

    private async Task PersistAppleAlbumManifestAsync(
        WorkLineage lineage,
        string collectionId,
        string? album,
        string? artist,
        Guid providerId,
        CancellationToken ct,
        IReadOnlyList<JsonNode>? knownTracks = null)
    {
        var rootWorkId = lineage.TargetForParentScope;
        var albumLock = MusicAlbumManifestLocks.GetOrAdd(rootWorkId, static _ => new SemaphoreSlim(1, 1));
        await albumLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var rootValues = await _canonicalRepo.GetByEntityAsync(rootWorkId, ct).ConfigureAwait(false);
            var existingManifest = rootValues.FirstOrDefault(value =>
                string.Equals(value.Key, MetadataFieldConstants.ChildEntitiesJson, StringComparison.OrdinalIgnoreCase))?.Value;
            if (AppleAlbumManifestJson.IsCompleteForCollection(existingManifest, collectionId))
                return;

            var tracks = knownTracks
                ?? await _appleClient.FetchAlbumTracksAsync(collectionId, "us", "en", ct).ConfigureAwait(false);
            if (tracks.Count == 0)
                return;

            var manifest = AppleAlbumManifestJson.Build(tracks, collectionId, album, artist);
            var trackCount = tracks.Count(track => !string.IsNullOrWhiteSpace(track["trackName"]?.GetValue<string>()));
            if (trackCount == 0)
                return;

            await _canonicalRepo.UpsertBatchAsync(
                [
                    new CanonicalValue
                    {
                        EntityId = rootWorkId,
                        Key = MetadataFieldConstants.ChildEntitiesJson,
                        Value = manifest,
                        LastScoredAt = DateTimeOffset.UtcNow,
                        WinningProviderId = providerId,
                    },
                    new CanonicalValue
                    {
                        EntityId = rootWorkId,
                        Key = MetadataFieldConstants.TrackCount,
                        Value = trackCount.ToString(CultureInfo.InvariantCulture),
                        LastScoredAt = DateTimeOffset.UtcNow,
                        WinningProviderId = providerId,
                    },
                    new CanonicalValue
                    {
                        EntityId = rootWorkId,
                        Key = BridgeIdKeys.AppleMusicCollectionId,
                        Value = collectionId,
                        LastScoredAt = DateTimeOffset.UtcNow,
                        WinningProviderId = providerId,
                    },
                ],
                ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Music: persisted {TrackCount}-track Apple album manifest for '{Artist}' / '{Album}' on root work {RootWorkId}",
                trackCount,
                artist ?? "(unknown artist)",
                album ?? "(unknown album)",
                rootWorkId);
        }
        finally
        {
            albumLock.Release();
        }
    }
}
