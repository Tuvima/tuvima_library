using MediaEngine.Domain;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;

namespace MediaEngine.Providers.Helpers;

/// <summary>
/// Bridge ID ↔ Wikidata P-code mapping, bridge hint extraction, and sentinel handling.
/// Thread-safe singleton — the P-code map is built lazily from provider config.
/// </summary>
public sealed class BridgeIdHelper
{
    private readonly IConfigurationLoader _configLoader;
    private Dictionary<string, string>? _claimKeyToPCode;
    private readonly Lock _mapLock = new();

    public BridgeIdHelper(IConfigurationLoader configLoader)
    {
        _configLoader = configLoader;
    }

    /// <summary>
    /// Returns the Wikidata P-code for a given claim key (e.g. "isbn_13" → "P212"),
    /// or <c>null</c> if no mapping is found.
    /// </summary>
    public string? GetPCode(string claimKey)
    {
        EnsureMap();
        return _claimKeyToPCode!.TryGetValue(claimKey, out var pCode) ? pCode : null;
    }

    /// <summary>
    /// Returns the claim key for a given Wikidata P-code (e.g. "P212" → "isbn_13"),
    /// or the P-code itself if no mapping is found.
    /// </summary>
    public string GetClaimKey(string pCode)
    {
        var reconConfig = _configLoader
            .LoadConfig<ReconciliationProviderConfig>("providers", "wikidata_reconciliation");

        if (reconConfig?.DataExtension?.PropertyLabels is not null &&
            reconConfig.DataExtension.PropertyLabels.TryGetValue(pCode, out var claimKey))
            return claimKey;

        return pCode;
    }

    /// <summary>
    /// Returns true if the claim key represents a bridge identifier that should be
    /// stored in the bridge_ids table for Stage 2 Wikidata resolution.
    /// </summary>
    public static bool IsBridgeId(string claimKey) => claimKey switch
    {
        BridgeIdKeys.Isbn or BridgeIdKeys.Isbn13 or BridgeIdKeys.Isbn10 => true,
        BridgeIdKeys.Asin => true,
        BridgeIdKeys.AppleBooksId => true,
        BridgeIdKeys.TmdbId => true,
        BridgeIdKeys.ImdbId => true,
        BridgeIdKeys.AudibleId => true,
        BridgeIdKeys.GoodreadsId => true,
        BridgeIdKeys.MusicBrainzId => true,
        BridgeIdKeys.ComicVineId => true,
        "gcd_id" => true,

        "apple_music_id" => true,
        BridgeIdKeys.AppleMusicCollectionId => true,
        BridgeIdKeys.AppleArtistId => true,
        _ => false,
    };

    /// <summary>
    /// Adds <c>_title</c> and <c>_author</c> sentinel keys to the bridge dictionary
    /// for text reconciliation fallback.
    /// </summary>
    public static void InjectSentinels(Dictionary<string, string> bridgeDict, string? title, string? author)
    {
        if (!string.IsNullOrWhiteSpace(title))
            bridgeDict["_title"] = title;
        if (!string.IsNullOrWhiteSpace(author))
            bridgeDict["_author"] = author;
    }

    private void EnsureMap()
    {
        if (_claimKeyToPCode is not null) return;

        lock (_mapLock)
        {
            if (_claimKeyToPCode is not null) return;

            var reconConfig = _configLoader
                .LoadConfig<ReconciliationProviderConfig>("providers", "wikidata_reconciliation");

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (reconConfig?.DataExtension?.PropertyLabels is not null)
            {
                foreach (var kvp in reconConfig.DataExtension.PropertyLabels)
                    map[kvp.Value] = kvp.Key;
            }

            // Aliases
            if (!map.ContainsKey(BridgeIdKeys.Isbn) && map.TryGetValue(BridgeIdKeys.Isbn13, out var isbn13PCode))
                map[BridgeIdKeys.Isbn] = isbn13PCode;

            if (!map.ContainsKey(BridgeIdKeys.TmdbId))
            {
                if (map.TryGetValue("tmdb_movie_id", out var tmdbMoviePCode))
                    map[BridgeIdKeys.TmdbId] = tmdbMoviePCode;
                else if (map.TryGetValue("tmdb_tv_id", out var tmdbTvPCode))
                    map[BridgeIdKeys.TmdbId] = tmdbTvPCode;
            }

            if (!map.ContainsKey(BridgeIdKeys.MusicBrainzId))
            {
                if (map.TryGetValue("musicbrainz_release_group_id", out var mbReleaseGroupPCode))
                    map[BridgeIdKeys.MusicBrainzId] = mbReleaseGroupPCode;
                else if (map.TryGetValue("musicbrainz_artist_id", out var mbArtistPCode))
                    map[BridgeIdKeys.MusicBrainzId] = mbArtistPCode;
            }

            if (!map.ContainsKey(BridgeIdKeys.MusicBrainzRecordingId))
                map[BridgeIdKeys.MusicBrainzRecordingId] = "P966";

            if (!map.ContainsKey(BridgeIdKeys.MusicBrainzReleaseGroupId))
                map[BridgeIdKeys.MusicBrainzReleaseGroupId] = "P436";

            _claimKeyToPCode = map;
        }
    }
}
