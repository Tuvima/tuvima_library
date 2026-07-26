using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaEngine.Api.Models;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Services;
using static MediaEngine.Api.Services.Collections.CollectionResponseFormatting;

namespace MediaEngine.Api.Services.Collections;

/// <summary>
/// Merges Wikidata-discovered album/episode/issue manifests (<c>child_entities_json</c>)
/// with owned local tracks/episodes, and gap-fills missing album track manifests from the
/// Apple Music retail catalogue. Extracted from <c>CollectionEndpoints</c> — this is the
/// only cluster of former endpoint helpers that performs provider/database I/O, so its
/// dependencies (<see cref="ICanonicalValueRepository"/>, <see cref="AppleRetailClient"/>)
/// are constructor-injected rather than passed per-call.
/// </summary>
public sealed class AlbumTrackManifestService(
    ICanonicalValueRepository canonicalRepo,
    AppleRetailClient appleRetailClient)
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

    public async Task<string?> EnsureAppleAlbumTrackManifestAsync(
        Guid? rootWorkId,
        string? artist,
        string? album,
        string? existingChildEntitiesJson,
        IReadOnlyList<CanonicalValue> rootCanonicalValues,
        CancellationToken ct)
    {
        if (!NeedsAppleAlbumTrackGapFill(existingChildEntitiesJson))
        {
            return existingChildEntitiesJson;
        }

        var collectionId = FirstCanonicalValue(rootCanonicalValues, BridgeIdKeys.AppleMusicCollectionId);
        if (string.IsNullOrWhiteSpace(collectionId))
        {
            collectionId = await appleRetailClient.SearchAlbumAsync(artist, album, "us", "en", ct);
        }

        if (string.IsNullOrWhiteSpace(collectionId))
        {
            return existingChildEntitiesJson;
        }

        var appleTracks = await appleRetailClient.FetchAlbumTracksAsync(collectionId, "us", "en", ct);
        if (appleTracks.Count == 0)
        {
            return existingChildEntitiesJson;
        }

        var appleManifest = BuildAppleAlbumTrackManifest(appleTracks);
        var mergedManifest = MergeTrackManifests(existingChildEntitiesJson, appleManifest);
        if (rootWorkId.HasValue && !string.IsNullOrWhiteSpace(mergedManifest) && !string.Equals(mergedManifest, existingChildEntitiesJson, StringComparison.Ordinal))
        {
            await canonicalRepo.UpsertBatchAsync(
                [
                    new CanonicalValue
                    {
                        EntityId = rootWorkId.Value,
                        Key = MetadataFieldConstants.ChildEntitiesJson,
                        Value = mergedManifest,
                        LastScoredAt = DateTimeOffset.UtcNow,
                        WinningProviderId = WellKnownProviders.AppleApi,
                    },
                    new CanonicalValue
                    {
                        EntityId = rootWorkId.Value,
                        Key = MetadataFieldConstants.TrackCount,
                        Value = CountManifestTracks(mergedManifest).ToString(CultureInfo.InvariantCulture),
                        LastScoredAt = DateTimeOffset.UtcNow,
                        WinningProviderId = WellKnownProviders.AppleApi,
                    },
                ],
                ct);
        }

        return mergedManifest;
    }

    private static bool NeedsAppleAlbumTrackGapFill(string? childEntitiesJson)
    {
        if (string.IsNullOrWhiteSpace(childEntitiesJson))
        {
            return true;
        }

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

    private static string BuildAppleAlbumTrackManifest(IReadOnlyList<JsonNode> tracks)
    {
        var array = new JsonArray();
        var ordinal = 0;
        foreach (var track in tracks)
        {
            ordinal++;
            var title = track["trackName"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var trackNumber = track["trackNumber"]?.GetValue<int?>() ?? ordinal;
            var durationMillis = track["trackTimeMillis"]?.GetValue<long?>();
            var item = new JsonObject
            {
                ["title"] = title,
                ["ordinal"] = trackNumber,
                ["track_number"] = trackNumber,
                ["source"] = "apple_itunes",
            };

            if (track["discNumber"]?.GetValue<int?>() is { } discNumber)
            {
                item["disc_number"] = discNumber;
            }

            if (durationMillis is > 0)
            {
                item["duration_seconds"] = Math.Round(durationMillis.Value / 1000d, 3);
            }

            if (track["trackId"]?.GetValue<long?>() is { } trackId)
            {
                item["apple_music_id"] = trackId.ToString(CultureInfo.InvariantCulture);
            }

            array.Add(item);
        }

        return new JsonObject { ["tracks"] = array }.ToJsonString();
    }

    private static string MergeTrackManifests(string? existingJson, string appleJson)
    {
        var items = ReadTrackManifest(existingJson, defaultSource: "wikidata");
        var appleItems = ReadTrackManifest(appleJson, defaultSource: "apple_itunes");
        if (items.Count == 0)
        {
            items = appleItems;
        }
        else
        {
            foreach (var appleItem in appleItems)
            {
                var existing = !string.IsNullOrWhiteSpace(appleItem.AppleMusicId)
                    ? items.FirstOrDefault(item => string.Equals(item.AppleMusicId, appleItem.AppleMusicId, StringComparison.OrdinalIgnoreCase))
                    : null;
                existing ??= items.FirstOrDefault(item =>
                    string.Equals(
                        BuildTrackMatchKey(item.Title, item.DiscNumber, item.TrackNumber),
                        BuildTrackMatchKey(appleItem.Title, appleItem.DiscNumber, appleItem.TrackNumber),
                        StringComparison.OrdinalIgnoreCase))
                    ?? items.FirstOrDefault(item =>
                        string.Equals(NormalizeTrackTitle(item.Title), NormalizeTrackTitle(appleItem.Title), StringComparison.OrdinalIgnoreCase)
                        && !HasTrackManifestIdentityConflict(item, appleItem));

                if (existing is null)
                {
                    items.Add(appleItem);
                    continue;
                }

                existing.TrackNumber ??= appleItem.TrackNumber;
                existing.Ordinal = existing.Ordinal <= 0 ? appleItem.Ordinal : existing.Ordinal;
                existing.DiscNumber ??= appleItem.DiscNumber;
                existing.DurationSeconds ??= appleItem.DurationSeconds;
                existing.AppleMusicId ??= appleItem.AppleMusicId;
            }
        }

        var array = new JsonArray();
        foreach (var item in items
                     .OrderBy(item => item.DiscNumber ?? 1)
                     .ThenBy(item => item.TrackNumber ?? item.Ordinal)
                     .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase))
        {
            var obj = new JsonObject
            {
                ["title"] = item.Title,
                ["ordinal"] = item.Ordinal,
                ["source"] = item.Source,
            };
            if (item.TrackNumber is { } trackNumber)
            {
                obj["track_number"] = trackNumber;
            }
            if (item.DiscNumber is { } discNumber)
            {
                obj["disc_number"] = discNumber;
            }
            if (item.DurationSeconds is { } duration)
            {
                obj["duration_seconds"] = Math.Round(duration, 3);
            }
            if (!string.IsNullOrWhiteSpace(item.AppleMusicId))
            {
                obj["apple_music_id"] = item.AppleMusicId;
            }

            array.Add(obj);
        }

        return new JsonObject { ["tracks"] = array }.ToJsonString();
    }

    private static bool HasTrackManifestIdentityConflict(
        AlbumTrackManifestItem existing,
        AlbumTrackManifestItem incoming)
    {
        if (existing.DiscNumber is { } existingDisc
            && incoming.DiscNumber is { } incomingDisc
            && existingDisc != incomingDisc)
        {
            return true;
        }

        if (existing.TrackNumber is { } existingTrack
            && incoming.TrackNumber is { } incomingTrack
            && existingTrack != incomingTrack)
        {
            return true;
        }

        if (existing.DurationSeconds is { } existingDuration
            && incoming.DurationSeconds is { } incomingDuration
            && Math.Abs(existingDuration - incomingDuration) > 3)
        {
            return true;
        }

        return false;
    }

    private static List<AlbumTrackManifestItem> ReadTrackManifest(string? json, string defaultSource)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var ordinal = 0;
            var result = new List<AlbumTrackManifestItem>();
            foreach (var track in tracks.EnumerateArray())
            {
                ordinal++;
                var title = ReadJsonString(track, "title", "trackName", "name");
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var trackNumber = ReadJsonInt(track, "track_number", "trackNumber", "number");
                result.Add(new AlbumTrackManifestItem
                {
                    Title = title,
                    Ordinal = ReadJsonInt(track, "ordinal", "position") ?? trackNumber ?? ordinal,
                    TrackNumber = trackNumber,
                    DiscNumber = ReadJsonInt(track, "disc_number", "discNumber"),
                    DurationSeconds = ReadChildDurationSeconds(track),
                    AppleMusicId = ReadJsonString(track, "apple_music_id", "appleMusicId", "trackId"),
                    Source = ReadJsonString(track, "source", "provider") ?? defaultSource,
                });
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int CountManifestTracks(string manifestJson)
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

    private sealed class AlbumTrackManifestItem
    {
        public string Title { get; init; } = string.Empty;
        public int Ordinal { get; set; }
        public int? TrackNumber { get; set; }
        public int? DiscNumber { get; set; }
        public double? DurationSeconds { get; set; }
        public string? AppleMusicId { get; set; }
        public string Source { get; init; } = "provider";
    }
}
