using System.Globalization;
using System.Text.Json;
using MediaEngine.Api.Models;
using MediaEngine.Contracts.Collections;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Services;
using static MediaEngine.Api.Services.Collections.CollectionResponseFormatting;

namespace MediaEngine.Api.Services.Collections;

/// <summary>
/// Merges provider album manifests (<c>child_entities_json</c>) with owned local tracks,
/// and repairs missing or legacy Apple manifests as one provider-scoped track list from the
/// Apple Music retail catalogue. Extracted from <c>CollectionEndpoints</c> — this is the
/// only cluster of former endpoint helpers that performs provider/database I/O, so its
/// dependencies (<see cref="ICanonicalValueRepository"/>, <see cref="AppleRetailClient"/>)
/// are constructor-injected rather than passed per-call.
/// </summary>
public sealed class AlbumTrackManifestService(
    ICanonicalValueRepository canonicalRepo,
    AppleRetailClient appleRetailClient,
    MusicBrainzReleaseClient musicBrainzReleaseClient)
{
    /// <summary>
    /// Merges Wikidata-discovered tracks (from <c>child_entities_json</c>) into the owned-track list,
    /// flagging those without a matching local file as <c>IsOwned = false</c>. Owned tracks are matched
    /// to Wikidata tracks by case-insensitive title.
    /// </summary>
    public static List<CollectionGroupWorkDto> MergeUnownedMusicTracks(
        List<CollectionGroupWorkDto> ownedTracks,
        string? childEntitiesJson,
        string? albumCover)
    {
        if (string.IsNullOrWhiteSpace(childEntitiesJson))
        {
            // No Wikidata data — sort owned by track number and return.
            return SortAlbumTracks(ownedTracks);
        }

        try
        {
            using var doc = JsonDocument.Parse(childEntitiesJson);
            if (!doc.RootElement.TryGetProperty("tracks", out var tracksArr) ||
                tracksArr.ValueKind != JsonValueKind.Array)
            {
                return SortAlbumTracks(ownedTracks);
            }

            // Build a lookup of owned tracks by normalized title for matching.
            var ownedByAppleMusicId = ownedTracks
                .Where(t => !string.IsNullOrWhiteSpace(t.AppleMusicId))
                .GroupBy(t => t.AppleMusicId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var ownedByTitleAndNumber = ownedTracks
                .Where(t => !string.IsNullOrWhiteSpace(t.Title))
                .GroupBy(t => BuildTrackMatchKey(t.Title, t.DiscNumber, ParseNullableInt(t.TrackNumber)))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var ownedByTitle = ownedTracks
                .Where(t => !string.IsNullOrWhiteSpace(t.Title))
                .GroupBy(t => NormalizeTrackTitle(t.Title))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var merged = new List<CollectionGroupWorkDto>();
            var seenOwned = new HashSet<Guid>();
            int manifestOrdinal = 0;

            foreach (var trackEl in tracksArr.EnumerateArray())
            {
                manifestOrdinal++;
                var title = ReadJsonString(trackEl, "title", "trackName", "name");
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var trackNumber = ReadJsonInt(trackEl, "track_number", "trackNumber", "number");
                var discNumber = ReadJsonInt(trackEl, "disc_number", "discNumber");
                var ordinal = ReadJsonInt(trackEl, "ordinal", "position") ?? trackNumber ?? manifestOrdinal;
                var durationSeconds = ReadChildDurationSeconds(trackEl);
                var appleMusicId = ReadJsonString(trackEl, "apple_music_id", "appleMusicId", "trackId");
                var owned = (!string.IsNullOrWhiteSpace(appleMusicId)
                        ? ownedByAppleMusicId.GetValueOrDefault(appleMusicId)
                        : null)
                    ?? ownedByTitleAndNumber.GetValueOrDefault(BuildTrackMatchKey(title, discNumber, trackNumber));
                if (owned is null)
                {
                    var titleMatch = ownedByTitle.GetValueOrDefault(NormalizeTrackTitle(title));
                    if (titleMatch is not null
                        && !HasKnownTrackIdentityConflict(titleMatch, discNumber, trackNumber, durationSeconds))
                    {
                        owned = titleMatch;
                    }
                }
                if (owned is not null)
                {
                    // Owned — keep the local row but normalise the track number from Wikidata.
                    merged.Add(new CollectionGroupWorkDto
                    {
                        WorkId = owned.WorkId,
                        AssetId = owned.AssetId,
                        Title = owned.Title,
                        Ordinal = owned.Ordinal ?? ordinal,
                        Year = owned.Year,
                        Duration = StringHelpers.FirstNonBlank(owned.Duration, FormatAudioDuration(durationSeconds, null)),
                        DurationSeconds = owned.DurationSeconds ?? durationSeconds,
                        CoverUrl = owned.CoverUrl ?? albumCover,
                        BackgroundUrl = owned.BackgroundUrl,
                        BannerUrl = owned.BannerUrl,
                        HeroUrl = owned.HeroUrl,
                        WikidataQid = owned.WikidataQid,
                        TrackNumber = StringHelpers.FirstNonBlank(owned.TrackNumber, (trackNumber ?? ordinal).ToString(CultureInfo.InvariantCulture)),
                        DiscNumber = owned.DiscNumber ?? discNumber,
                        AppleMusicId = StringHelpers.FirstNonBlank(owned.AppleMusicId, appleMusicId),
                        Status = owned.Status,
                        Description = owned.Description,
                        Director = owned.Director,
                        Writer = owned.Writer,
                        ReleaseDate = owned.ReleaseDate,
                        PlaybackSummary = owned.PlaybackSummary,
                        IsOwned = true,
                        Stage1 = owned.Stage1,
                        Stage2 = owned.Stage2,
                        Stage3 = owned.Stage3,
                    });
                    seenOwned.Add(owned.WorkId);
                }
                else
                {
                    // Unowned — synthesize a row from Wikidata data.
                    merged.Add(new CollectionGroupWorkDto
                    {
                        WorkId = Guid.Empty,
                        Title = title,
                        Ordinal = ordinal,
                        TrackNumber = (trackNumber ?? ordinal).ToString(CultureInfo.InvariantCulture),
                        DiscNumber = discNumber,
                        AppleMusicId = appleMusicId,
                        Duration = FormatAudioDuration(durationSeconds, null),
                        DurationSeconds = durationSeconds,
                        CoverUrl = albumCover,
                        Status = "Missing",
                        IsOwned = false,
                    });
                }
            }

            // Append any owned tracks that didn't match a Wikidata title (rare — bonus tracks, mislabeled).
            foreach (var t in ownedTracks)
            {
                if (!seenOwned.Contains(t.WorkId))
                {
                    merged.Add(t);
                }
            }

            return SortAlbumTracks(merged);
        }
        catch (JsonException)
        {
            // Malformed JSON — fall back to owned-only.
            return SortAlbumTracks(ownedTracks);
        }
    }

    /// <summary>
    /// Merges Wikidata-discovered child entities (from <c>child_entities_json</c>)
    /// into <paramref name="sectionMap"/> as unowned rows, deduplicating against
    /// owned rows by case-insensitive title. Supports TV (episodes grouped by
    /// season), music (tracks in flat "_flat" section), and comics (issues).
    ///
    /// Called by <c>system-view-detail</c> after the owned-works reader loop.
    /// </summary>
    public void MergeUnownedChildEntities(
        Dictionary<string, List<CollectionGroupWorkDto>> sectionMap,
        string childEntitiesJson,
        string groupField,
        string? secondaryGroup,
        string? fallbackCover)
    {
        try
        {
            using var doc = JsonDocument.Parse(childEntitiesJson);
            var root = doc.RootElement;

            // Determine which array key to read. Mirrors ReconciliationAdapter conventions.
            // "tracks" for music, "episodes" for TV (flat or grouped), "issues" for comics.
            string[]? arrayKeys = groupField.ToLowerInvariant() switch
            {
                "show_name" => ["episodes", "seasons"],
                "album" => ["tracks"],
                "series" => ["issues"],
                _ => null,
            };

            if (arrayKeys is null)
            {
                return;
            }

            // TV episodes may be nested: root.seasons[].episodes[].
            if (groupField.Equals("show_name", StringComparison.OrdinalIgnoreCase)
                && root.TryGetProperty("seasons", out var seasonsArr)
                && seasonsArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var seasonEl in seasonsArr.EnumerateArray())
                {
                    var seasonNum = seasonEl.TryGetProperty("season_number", out var snEl)
                        && snEl.ValueKind == JsonValueKind.Number
                        ? snEl.GetInt32().ToString()
                        : null;

                    if (!seasonEl.TryGetProperty("episodes", out var epArr)
                        || epArr.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    MergeChildArray(sectionMap, epArr, seasonNum ?? "Unknown",
                        isEpisode: true, fallbackCover);
                }
                return;
            }

            // Flat structure: tracks, issues, or flat episodes.
            foreach (var key in arrayKeys)
            {
                if (root.TryGetProperty(key, out var arr)
                    && arr.ValueKind == JsonValueKind.Array)
                {
                    MergeChildArray(sectionMap, arr, "_flat",
                        isEpisode: key == "episodes", fallbackCover);
                    return;
                }
            }
        }
        catch
        {
            // Malformed JSON — leave sectionMap as-is (owned only).
        }
    }

    /// <summary>
    /// Adds unowned rows from <paramref name="childArray"/> into
    /// <paramref name="sectionMap"/>[<paramref name="sectionKey"/>],
    /// skipping entries whose title already appears as an owned row.
    /// </summary>
    private void MergeChildArray(
        Dictionary<string, List<CollectionGroupWorkDto>> sectionMap,
        JsonElement childArray,
        string sectionKey,
        bool isEpisode,
        string? fallbackCover)
    {
        // Build a set of owned titles in this section for O(1) dedup.
        var ownedTitles = sectionMap.TryGetValue(sectionKey, out var existing)
            ? existing
                .Where(w => w.IsOwned && !string.IsNullOrWhiteSpace(w.Title))
                .Select(w => isEpisode ? w.Title.Trim().ToLowerInvariant() : NormalizeTrackTitle(w.Title))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!sectionMap.ContainsKey(sectionKey))
        {
            sectionMap[sectionKey] = [];
        }

        int wikiOrdinal = 0;
        foreach (var el in childArray.EnumerateArray())
        {
            wikiOrdinal++;
            var title = el.TryGetProperty("title", out var tEl)
                && tEl.ValueKind == JsonValueKind.String
                ? tEl.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            // Skip if an owned row with the same title is already in this section.
            var titleKey = isEpisode ? title.Trim().ToLowerInvariant() : NormalizeTrackTitle(title);
            if (ownedTitles.Contains(titleKey))
            {
                continue;
            }

            var trackNumber = ReadJsonInt(el, "track_number", "trackNumber", "number");
            var discNumber = ReadJsonInt(el, "disc_number", "discNumber");
            var ordinal = ReadJsonInt(el, "ordinal", "position") ?? trackNumber ?? wikiOrdinal;
            var durationSeconds = ReadChildDurationSeconds(el);
            var appleMusicId = ReadJsonString(el, "apple_music_id", "appleMusicId", "trackId");

            var episodeNumStr = isEpisode
                ? (ReadJsonInt(el, "episode_number", "episodeNumber") ?? ordinal).ToString(CultureInfo.InvariantCulture)
                : null;

            sectionMap[sectionKey].Add(new CollectionGroupWorkDto
            {
                WorkId = Guid.Empty,
                Title = title,
                Ordinal = ordinal,
                Episode = episodeNumStr,
                TrackNumber = isEpisode ? null : (trackNumber ?? ordinal).ToString(CultureInfo.InvariantCulture),
                DiscNumber = isEpisode ? null : discNumber,
                AppleMusicId = isEpisode ? null : appleMusicId,
                Duration = FormatAudioDuration(durationSeconds, null),
                DurationSeconds = durationSeconds,
                CoverUrl = fallbackCover,
                Status = "Missing",
                IsOwned = false,
            });
        }
    }

    private static List<CollectionGroupWorkDto> SortAlbumTracks(IEnumerable<CollectionGroupWorkDto> tracks)
        => tracks
            .OrderBy(track => track.DiscNumber ?? 1)
            .ThenBy(track => ParseNullableInt(track.TrackNumber) ?? track.Ordinal ?? int.MaxValue)
            .ThenBy(track => track.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string NormalizeTrackTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().ToLowerInvariant();
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s*[\(\[\{].*?[\)\]\}]\s*", " ");
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\b(remaster(ed)?|remix|mono|stereo|explicit|clean|single version|album version)\b", " ");
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[^\p{L}\p{Nd}]+", " ");
        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string BuildTrackMatchKey(string? title, int? discNumber, int? trackNumber)
        => $"{NormalizeTrackTitle(title)}|{discNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}|{trackNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}";

    private static bool HasKnownTrackIdentityConflict(
        CollectionGroupWorkDto owned,
        int? manifestDiscNumber,
        int? manifestTrackNumber,
        double? manifestDurationSeconds)
    {
        if (owned.DiscNumber is { } ownedDisc
            && manifestDiscNumber is { } manifestDisc
            && ownedDisc != manifestDisc)
        {
            return true;
        }

        var ownedTrackNumber = ParseNullableInt(owned.TrackNumber);
        if (ownedTrackNumber is { } ownedTrack
            && manifestTrackNumber is { } manifestTrack
            && ownedTrack != manifestTrack)
        {
            return true;
        }

        if (owned.DurationSeconds is { } ownedDuration
            && manifestDurationSeconds is { } manifestDuration
            && Math.Abs(ownedDuration - manifestDuration) > 3)
        {
            return true;
        }

        return false;
    }

    public async Task<string?> EnsureAlbumTrackManifestAsync(
        Guid? rootWorkId,
        string? artist,
        string? album,
        string? existingChildEntitiesJson,
        IReadOnlyList<CanonicalValue> rootCanonicalValues,
        CancellationToken ct,
        bool forceProviderRefresh = false)
    {
        var hasCover = rootCanonicalValues.Any(value =>
            (value.Key is MetadataFieldConstants.Cover or MetadataFieldConstants.CoverUrl)
            && !string.IsNullOrWhiteSpace(value.Value));
        var releaseId = rootCanonicalValues.FirstOrDefault(value =>
            string.Equals(
                value.Key,
                BridgeIdKeys.MusicBrainzReleaseId,
                StringComparison.OrdinalIgnoreCase))?.Value;
        var needsManifest = NeedsAlbumTrackGapFill(existingChildEntitiesJson)
            || (!string.IsNullOrWhiteSpace(releaseId)
                && !MusicBrainzAlbumManifestJson.IsCompleteForRelease(
                    existingChildEntitiesJson,
                    releaseId));

        MusicBrainzAlbumRelease? musicBrainzRelease = null;
        if ((forceProviderRefresh || !hasCover || needsManifest) && !string.IsNullOrWhiteSpace(releaseId))
        {
            musicBrainzRelease = await musicBrainzReleaseClient
                .FetchReleaseAsync(releaseId, ct)
                .ConfigureAwait(false);
        }

        if ((forceProviderRefresh || needsManifest) && musicBrainzRelease is not null)
        {
            if (rootWorkId.HasValue)
            {
                var values = BuildManifestValues(
                    rootWorkId.Value,
                    musicBrainzRelease.ManifestJson,
                    musicBrainzRelease.TrackCount,
                    WellKnownProviders.MusicBrainz);
                if (!hasCover && !string.IsNullOrWhiteSpace(musicBrainzRelease.CoverUrl))
                {
                    values.Add(BuildCoverValue(
                        rootWorkId.Value,
                        musicBrainzRelease.CoverUrl,
                        WellKnownProviders.MusicBrainz));
                }

                await canonicalRepo.UpsertBatchAsync(values, ct);
            }

            return musicBrainzRelease.ManifestJson;
        }

        if (!forceProviderRefresh && !needsManifest)
        {
            if (rootWorkId.HasValue)
            {
                var values = new List<CanonicalValue>();
                AddTrackCountRepair(
                    values,
                    rootWorkId.Value,
                    existingChildEntitiesJson!,
                    rootCanonicalValues,
                    MusicBrainzAlbumManifestJson.IsCompleteForRelease(existingChildEntitiesJson)
                        ? WellKnownProviders.MusicBrainz
                        : WellKnownProviders.AppleApi);

                if (!hasCover && !string.IsNullOrWhiteSpace(musicBrainzRelease?.CoverUrl))
                {
                    values.Add(BuildCoverValue(
                        rootWorkId.Value,
                        musicBrainzRelease.CoverUrl,
                        WellKnownProviders.MusicBrainz));
                }

                if (values.Count > 0)
                    await canonicalRepo.UpsertBatchAsync(values, ct);
            }

            if (hasCover
                || !string.IsNullOrWhiteSpace(musicBrainzRelease?.CoverUrl)
                || MusicBrainzAlbumManifestJson.IsCompleteForRelease(existingChildEntitiesJson))
            {
                return existingChildEntitiesJson;
            }
        }

        // Re-resolve incomplete Apple manifests by name instead of trusting an
        // older collection ID. Complete Apple manifests reuse their proven
        // collection identity when only managed cover art needs repair.
        if (forceProviderRefresh
            && !string.IsNullOrWhiteSpace(releaseId)
            && musicBrainzRelease is null)
        {
            return existingChildEntitiesJson;
        }

        var configuredAppleCollectionId = rootCanonicalValues.FirstOrDefault(value =>
            string.Equals(
                value.Key,
                BridgeIdKeys.AppleMusicCollectionId,
                StringComparison.OrdinalIgnoreCase))?.Value;
        var collectionId = forceProviderRefresh
            ? configuredAppleCollectionId
              ?? await appleRetailClient.SearchAlbumAsync(artist, album, "us", "en", ct)
            : needsManifest
                ? await appleRetailClient.SearchAlbumAsync(artist, album, "us", "en", ct)
                : TryReadAppleCollectionId(existingChildEntitiesJson)
                  ?? configuredAppleCollectionId;

        if (string.IsNullOrWhiteSpace(collectionId))
            return existingChildEntitiesJson;

        var appleTracks = await appleRetailClient.FetchAlbumTracksAsync(collectionId, "us", "en", ct);
        if (appleTracks.Count == 0)
        {
            return existingChildEntitiesJson;
        }

        var appleManifest = forceProviderRefresh || needsManifest
            ? AppleAlbumManifestJson.Build(appleTracks, collectionId, album, artist)
            : existingChildEntitiesJson;
        if (rootWorkId.HasValue)
        {
            var values = new List<CanonicalValue>();
            if ((forceProviderRefresh || needsManifest)
                && !string.Equals(appleManifest, existingChildEntitiesJson, StringComparison.Ordinal))
            {
                values.AddRange(BuildManifestValues(
                    rootWorkId.Value,
                    appleManifest!,
                    CountManifestTracks(appleManifest!),
                    WellKnownProviders.AppleApi));
                values.Add(new CanonicalValue
                {
                    EntityId = rootWorkId.Value,
                    Key = BridgeIdKeys.AppleMusicCollectionId,
                    Value = collectionId,
                    LastScoredAt = DateTimeOffset.UtcNow,
                    WinningProviderId = WellKnownProviders.AppleApi,
                });
            }
            else if (appleManifest is not null)
            {
                AddTrackCountRepair(
                    values,
                    rootWorkId.Value,
                    appleManifest,
                    rootCanonicalValues,
                    WellKnownProviders.AppleApi);
            }

            var coverUrl = appleTracks
                .Select(track => RetailRequestBuilder.BuildAppleCoverUrl(
                    track["artworkUrl100"]?.GetValue<string>()))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!hasCover && !string.IsNullOrWhiteSpace(coverUrl))
            {
                values.Add(BuildCoverValue(
                    rootWorkId.Value,
                    coverUrl,
                    WellKnownProviders.AppleApi));
            }

            if (values.Count > 0)
            {
                await canonicalRepo.UpsertBatchAsync(values, ct);
            }
        }

        return appleManifest;
    }

    private static List<CanonicalValue> BuildManifestValues(
        Guid rootWorkId,
        string manifest,
        int trackCount,
        Guid providerId)
        =>
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
        ];

    private static CanonicalValue BuildCoverValue(
        Guid rootWorkId,
        string coverUrl,
        Guid providerId)
        => new()
        {
            EntityId = rootWorkId,
            Key = MetadataFieldConstants.CoverUrl,
            Value = coverUrl,
            LastScoredAt = DateTimeOffset.UtcNow,
            WinningProviderId = providerId,
        };

    private static void AddTrackCountRepair(
        ICollection<CanonicalValue> values,
        Guid rootWorkId,
        string manifest,
        IReadOnlyList<CanonicalValue> rootCanonicalValues,
        Guid providerId)
    {
        var manifestTrackCount = CountManifestTracks(manifest);
        if (manifestTrackCount <= 0)
            return;

        var canonicalTrackCount = rootCanonicalValues.FirstOrDefault(value =>
            string.Equals(
                value.Key,
                MetadataFieldConstants.TrackCount,
                StringComparison.OrdinalIgnoreCase))?.Value;
        if (int.TryParse(
                canonicalTrackCount,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedTrackCount)
            && parsedTrackCount == manifestTrackCount)
        {
            return;
        }

        values.Add(new CanonicalValue
        {
            EntityId = rootWorkId,
            Key = MetadataFieldConstants.TrackCount,
            Value = manifestTrackCount.ToString(CultureInfo.InvariantCulture),
            LastScoredAt = DateTimeOffset.UtcNow,
            WinningProviderId = providerId,
        });
    }

    private static string? TryReadAppleCollectionId(string? childEntitiesJson)
    {
        if (string.IsNullOrWhiteSpace(childEntitiesJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(childEntitiesJson);
            return document.RootElement.TryGetProperty("provider_collection_id", out var collectionId)
                && collectionId.ValueKind == JsonValueKind.String
                ? collectionId.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool NeedsAlbumTrackGapFill(string? childEntitiesJson)
    {
        if (MusicAlbumManifestJson.IsComplete(childEntitiesJson))
            return false;

        if (string.IsNullOrWhiteSpace(childEntitiesJson))
            return true;

        // Older Apple manifests had no top-level collection identity, so even
        // internally complete rows could belong to a compilation or box set.
        if (AppleAlbumManifestJson.ContainsAppleTrackRows(childEntitiesJson))
            return true;

        try
        {
            using var doc = JsonDocument.Parse(childEntitiesJson);
            if (!doc.RootElement.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array || tracks.GetArrayLength() == 0)
            {
                return true;
            }

            return tracks.EnumerateArray().Any(track =>
                string.IsNullOrWhiteSpace(ReadJsonString(track, "title", "trackName", "name"))
                || ReadJsonInt(track, "ordinal", "position") is null
                || ReadJsonInt(track, "track_number", "trackNumber", "number") is null
                || ReadChildDurationSeconds(track) is null);
        }
        catch (JsonException)
        {
            return true;
        }
    }

    internal static int CountManifestTracks(string manifestJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(manifestJson);
            return doc.RootElement.TryGetProperty("tracks", out var tracks) && tracks.ValueKind == JsonValueKind.Array
                ? tracks.GetArrayLength()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
