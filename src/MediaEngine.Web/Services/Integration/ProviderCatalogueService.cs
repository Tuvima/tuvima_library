using MediaEngine.Web.Models;
using MediaEngine.Web.Models.ViewDTOs;
using Microsoft.Extensions.Caching.Memory;

namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Caches the provider catalogue fetched from the Engine's <c>GET /providers/catalogue</c>
/// endpoint. This service replaces hardcoded provider display names, accent colours,
/// material icons, and field capability chips across Dashboard files.
///
/// <para>
/// The catalogue is loaded lazily on first use and cached in memory for the session.
/// All lookup methods return safe fallback values when the Engine is unreachable.
/// Fallback display names and accent colours are sourced from <see cref="ProviderAccentMap"/>
///  -  the single authoritative static map  -  rather than being duplicated here.
/// </para>
/// </summary>
public sealed class ProviderCatalogueService
{
    private readonly IEngineApiClient _api;
    private readonly IMemoryCache _cache;
    private IReadOnlyList<ProviderCatalogueDto>? _catalogue;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private const string CatalogueCacheKey = "provider-catalogue:v1";

    public ProviderCatalogueService(IEngineApiClient api, IMemoryCache cache)
    {
        _api = api;
        _cache = cache;
    }

    // -- Catalogue access ------------------------------------------------------

    /// <summary>Returns the full catalogue, loading it from the Engine on first call.</summary>
    public async Task<IReadOnlyList<ProviderCatalogueDto>> GetCatalogueAsync(
        CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CatalogueCacheKey, out IReadOnlyList<ProviderCatalogueDto>? cached) && cached is not null)
        {
            _catalogue = cached;
            return cached;
        }

        if (_catalogue is not null) return _catalogue;

        await _loadLock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(CatalogueCacheKey, out cached) && cached is not null)
            {
                _catalogue = cached;
                return cached;
            }

            if (_catalogue is not null) return _catalogue;
            _catalogue = await _api.GetProviderCatalogueAsync(ct);
            _cache.Set(
                CatalogueCacheKey,
                _catalogue,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                    Size = Math.Max(1, _catalogue.Count),
                });
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

    // -- Convenience accessors (safe fallbacks when catalogue not loaded) -------

    /// <summary>
    /// Returns the hex accent colour for a provider config name.
    /// Falls back to <see cref="ProviderAccentMap.GetAccent"/> when the catalogue is not yet loaded.
    /// </summary>
    public string GetAccentColor(string providerName)
    {
        var entry = GetByName(providerName);
        if (entry is not null) return entry.AccentColor;
        return ProviderAccentMap.GetAccent(providerName).Color;
    }

    /// <summary>
    /// Returns the display name for a provider config name.
    /// Falls back to <see cref="ProviderAccentMap.GetDisplayName"/> when the catalogue is not yet loaded.
    /// </summary>
    public string GetDisplayName(string providerName)
    {
        var entry = GetByName(providerName);
        if (entry is not null) return entry.DisplayName;
        return ProviderAccentMap.GetDisplayName(providerName);
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

        // Hardcoded fallback for well-known bridge IDs (matching LibraryHelpers.BuildProviderUrl)
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
    public void Invalidate()
    {
        _catalogue = null;
        _cache.Remove(CatalogueCacheKey);
    }

    // -- Private helpers -------------------------------------------------------

    private static string ResolveMediaTypePath(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType)) return "movie";
        return mediaType.Contains("TV", StringComparison.OrdinalIgnoreCase) ? "tv" : "movie";
    }
}

