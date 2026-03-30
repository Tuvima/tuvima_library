using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Caches the provider catalogue fetched from the Engine's <c>GET /providers/catalogue</c>
/// endpoint. This service replaces hardcoded provider display names, accent colours,
/// material icons, and field capability chips across Dashboard files.
///
/// <para>
/// The catalogue is loaded lazily on first use and cached in memory for the session.
/// All lookup methods return safe fallback values when the Engine is unreachable.
/// </para>
/// </summary>
public sealed class ProviderCatalogueService
{
    private readonly IEngineApiClient _api;
    private IReadOnlyList<ProviderCatalogueDto>? _catalogue;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    // ── Well-known provider GUIDs → display names (fallback when catalogue unavailable) ──────
    private static readonly IReadOnlyDictionary<string, string> FallbackDisplayNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["apple_api"]                  = "Apple API",
            ["apple_podcasts"]             = "Apple Podcasts",
            ["tmdb"]                       = "TMDB",
            ["musicbrainz"]                = "MusicBrainz",
            ["metron"]                     = "Metron",
            ["podcast_index"]              = "Podcast Index",
            ["open_library"]               = "Open Library",
            ["fanart_tv"]                  = "Fanart.tv",
            ["wikidata_reconciliation"]    = "Wikidata",
            ["wikidata"]                   = "Wikidata",
            ["local_filesystem"]           = "Local Filesystem",
        };

    private static readonly IReadOnlyDictionary<string, string> FallbackAccentColors =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["apple_api"]                  = "#FF2D55",
            ["apple_podcasts"]             = "#9B59B6",
            ["tmdb"]                       = "#01B4E4",
            ["musicbrainz"]                = "#BA478F",
            ["metron"]                     = "#E91E63",
            ["podcast_index"]              = "#F7931E",
            ["open_library"]               = "#4CAF50",
            ["fanart_tv"]                  = "#19C1CC",
            ["wikidata_reconciliation"]    = "#339966",
            ["wikidata"]                   = "#339966",
            ["local_filesystem"]           = "#90A4AE",
        };

    public ProviderCatalogueService(IEngineApiClient api)
    {
        _api = api;
    }

    // ── Catalogue access ──────────────────────────────────────────────────────

    /// <summary>Returns the full catalogue, loading it from the Engine on first call.</summary>
    public async Task<IReadOnlyList<ProviderCatalogueDto>> GetCatalogueAsync(
        CancellationToken ct = default)
    {
        if (_catalogue is not null) return _catalogue;

        await _loadLock.WaitAsync(ct);
        try
        {
            if (_catalogue is not null) return _catalogue;
            _catalogue = await _api.GetProviderCatalogueAsync(ct);
        }
        finally
        {
            _loadLock.Release();
        }

        return _catalogue;
    }

    /// <summary>Returns the entry for a provider by config name (e.g. "apple_api"), or null if not found.</summary>
    public ProviderCatalogueDto? GetByName(string providerName)
    {
        if (_catalogue is null) return null;
        return _catalogue.FirstOrDefault(p =>
            string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns the entry for a provider by GUID string, or null if not found.</summary>
    public ProviderCatalogueDto? GetById(string providerId)
    {
        if (_catalogue is null) return null;
        return _catalogue.FirstOrDefault(p =>
            string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
    }

    // ── Convenience accessors (safe fallbacks when catalogue not loaded) ───────

    /// <summary>
    /// Returns the hex accent colour for a provider config name.
    /// Falls back to a hardcoded map when the catalogue is not yet loaded.
    /// </summary>
    public string GetAccentColor(string providerName)
    {
        var entry = GetByName(providerName);
        if (entry is not null) return entry.AccentColor;
        return FallbackAccentColors.TryGetValue(providerName, out var c) ? c : "#90A4AE";
    }

    /// <summary>
    /// Returns the display name for a provider config name.
    /// Falls back to a hardcoded map when the catalogue is not yet loaded.
    /// </summary>
    public string GetDisplayName(string providerName)
    {
        var entry = GetByName(providerName);
        if (entry is not null) return entry.DisplayName;
        if (FallbackDisplayNames.TryGetValue(providerName, out var n)) return n;
        return FormatProviderName(providerName);
    }

    /// <summary>Returns the Material icon name for a provider config name.</summary>
    public string GetMaterialIcon(string providerName)
    {
        var entry = GetByName(providerName);
        return entry?.MaterialIcon ?? "Cloud";
    }

    /// <summary>
    /// Builds an external URL for a given bridge ID key and value using the catalogue's
    /// url templates. Returns null when no template matches.
    /// </summary>
    public (string Label, string Url)? GetExternalUrl(string bridgeKey, string value, string? mediaType = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        // Match bridge key against each provider's external_url_template
        if (_catalogue is not null)
        {
            foreach (var p in _catalogue)
            {
                if (string.IsNullOrEmpty(p.ExternalUrlTemplate)) continue;

                // Check if the template uses this bridge key as a placeholder
                var placeholder = $"{{{bridgeKey}}}";
                if (!p.ExternalUrlTemplate.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
                    continue;

                // For TMDB, resolve media_type placeholder
                var url = p.ExternalUrlTemplate
                    .Replace(placeholder, value)
                    .Replace("{media_type}", ResolveMediaTypePath(mediaType));

                return ($"View on {p.DisplayName}", url);
            }
        }

        // Hardcoded fallback for well-known bridge IDs (matching VaultHelpers.BuildProviderUrl)
        return bridgeKey.ToLowerInvariant() switch
        {
            "tmdb_id" when (mediaType ?? "").Contains("TV", StringComparison.OrdinalIgnoreCase)
                => ("View on TMDB", $"https://www.themoviedb.org/tv/{value}"),
            "tmdb_id"
                => ("View on TMDB", $"https://www.themoviedb.org/movie/{value}"),
            "open_library_id" or "olid"
                => ("View on Open Library", $"https://openlibrary.org/works/{value}"),
            "musicbrainz_id"
                => ("View on MusicBrainz", $"https://musicbrainz.org/release/{value}"),
            "wikidata_qid" when value.StartsWith("Q", StringComparison.OrdinalIgnoreCase)
                => ("View on Wikidata", $"https://www.wikidata.org/wiki/{value}"),
            "imdb_id" when value.StartsWith("tt", StringComparison.OrdinalIgnoreCase)
                => ("View on IMDb", $"https://www.imdb.com/title/{value}"),
            "apple_books_id"
                => ("View on Apple Books", $"https://books.apple.com/book/id{value}"),
            _ => null,
        };
    }

    /// <summary>Returns search chip labels for a provider and media type.</summary>
    public IReadOnlyList<string> GetSearchChips(string providerName, string? mediaType = null)
    {
        var entry = GetByName(providerName);
        if (entry is null) return [];

        if (mediaType is not null && entry.SearchChips.TryGetValue(mediaType, out var chips))
            return chips;

        // Return union of all chips when media type not specified
        return entry.SearchChips.Values.SelectMany(c => c).Distinct().ToList();
    }

    /// <summary>Returns ranking chip labels for a provider and media type.</summary>
    public IReadOnlyList<string> GetRankingChips(string providerName, string? mediaType = null)
    {
        var entry = GetByName(providerName);
        if (entry is null) return [];

        if (mediaType is not null && entry.RankingChips.TryGetValue(mediaType, out var chips))
            return chips;

        return entry.RankingChips.Values.SelectMany(c => c).Distinct().ToList();
    }

    /// <summary>Invalidates the cached catalogue, forcing a reload on the next call.</summary>
    public void Invalidate() => _catalogue = null;

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string FormatProviderName(string key) =>
        string.Join(' ', key.Split('_')
            .Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w));

    private static string ResolveMediaTypePath(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType)) return "movie";
        return mediaType.Contains("TV", StringComparison.OrdinalIgnoreCase) ? "tv" : "movie";
    }
}
