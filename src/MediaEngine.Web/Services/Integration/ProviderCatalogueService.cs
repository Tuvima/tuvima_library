using MediaEngine.Domain;
using MediaEngine.Web.Models.ViewDTOs;
using Microsoft.Extensions.Caching.Memory;
using MediaEngine.Contracts.Settings;
using MudBlazor;

namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Caches the provider catalogue fetched from the Engine's <c>GET /providers/catalogue</c>
/// endpoint. This service replaces hardcoded provider display names, accent colours,
/// material icons, and field capability chips across Dashboard files.
///
/// <para>
/// The catalogue is loaded lazily on first use and cached in memory for the session.
/// All lookup methods return safe fallback values when the Engine is unreachable.
/// Static fallback metadata lives here too, so provider identity does not drift when
/// the Engine is unavailable or a static display helper cannot use dependency injection.
/// </para>
/// </summary>
public sealed class ProviderCatalogueService
{
    private readonly IEngineApiClient _api;
    private readonly IMemoryCache _cache;
    private IReadOnlyList<ProviderCatalogueDto>? _catalogue;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private const string CatalogueCacheKey = "provider-catalogue:v1";
    private static readonly IReadOnlyDictionary<Guid, string> ProviderNamesById =
        new Dictionary<Guid, string>
        {
            [WellKnownProviders.LocalProcessor] = "File Scan",
            [WellKnownProviders.LibraryScanner] = "Library Scanner",
            [WellKnownProviders.AppleApi] = "Apple API",
            [WellKnownProviders.Wikidata] = "Wikidata",
            [WellKnownProviders.Wikipedia] = "Wikipedia",
            [WellKnownProviders.OpenLibrary] = "Open Library",
            [WellKnownProviders.MusicBrainz] = "MusicBrainz",
            [WellKnownProviders.Tmdb] = "TMDB",
            [WellKnownProviders.AiProvider] = "Fanart.tv",
            [WellKnownProviders.UserManual] = "Manual Match",
        };

    private static readonly IReadOnlyDictionary<string, ProviderFallback> ProviderFallbacks =
        new Dictionary<string, ProviderFallback>(StringComparer.OrdinalIgnoreCase)
        {
            ["apple_api"] = new("Apple API", "#FF2D55", Icons.Material.Filled.MenuBook),
            ["open_library"] = new("Open Library", "#4CAF50", Icons.Material.Filled.LocalLibrary),
            ["wikidata"] = new("Wikidata", "#339966", Icons.Material.Filled.Collections),
            ["wikidata_reconciliation"] = new("Wikidata", "#339966", Icons.Material.Filled.Collections),
            ["tmdb"] = new("TMDB", "#01B4E4", Icons.Material.Filled.Movie),
            ["comicvine"] = new("Comic Vine", "#04C8FF", Icons.Material.Filled.AutoStories),
            ["musicbrainz"] = new("MusicBrainz", "#BA478F", Icons.Material.Filled.MusicNote),
            ["fanart_tv"] = new("Fanart.tv", "#19C1CC", Icons.Material.Filled.Image),
            ["lrclib"] = new("LRCLIB", "#3BA55D", Icons.Material.Filled.Lyrics),
            ["opensubtitles"] = new("OpenSubtitles", "#0F8BFD", Icons.Material.Filled.Subtitles),
            ["local_filesystem"] = new("Local Filesystem", "#90A4AE", Icons.Material.Filled.FolderOpen),
        };

    private static readonly IReadOnlyDictionary<string, string> MaterialIcons =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MenuBook"] = Icons.Material.Filled.MenuBook,
            ["Headphones"] = Icons.Material.Filled.Headphones,
            ["AutoStories"] = Icons.Material.Filled.AutoStories,
            ["Movie"] = Icons.Material.Filled.Movie,
            ["Tv"] = Icons.Material.Filled.Tv,
            ["MusicNote"] = Icons.Material.Filled.MusicNote,
            ["Description"] = Icons.Material.Filled.Description,
            ["Folder"] = Icons.Material.Filled.Folder,
            ["FolderOpen"] = Icons.Material.Filled.FolderOpen,
            ["Photo"] = Icons.Material.Filled.Photo,
            ["VideoLibrary"] = Icons.Material.Filled.VideoLibrary,
            ["AudioFile"] = Icons.Material.Filled.AudioFile,
            ["Article"] = Icons.Material.Filled.Article,
            ["Book"] = Icons.Material.Filled.Book,
            ["LibraryBooks"] = Icons.Material.Filled.LibraryBooks,
            ["LocalLibrary"] = Icons.Material.Filled.LocalLibrary,
            ["Hearing"] = Icons.Material.Filled.Hearing,
            ["Mic"] = Icons.Material.Filled.Mic,
            ["Album"] = Icons.Material.Filled.Album,
            ["Camera"] = Icons.Material.Filled.Camera,
            ["Image"] = Icons.Material.Filled.Image,
            ["PictureAsPdf"] = Icons.Material.Filled.PictureAsPdf,
            ["Code"] = Icons.Material.Filled.Code,
            ["Science"] = Icons.Material.Filled.Science,
            ["School"] = Icons.Material.Filled.School,
            ["SportsEsports"] = Icons.Material.Filled.SportsEsports,
            ["Newspaper"] = Icons.Material.Filled.Newspaper,
            ["Dashboard"] = Icons.Material.Filled.Dashboard,
            ["Star"] = Icons.Material.Filled.Star,
            ["Lyrics"] = Icons.Material.Filled.Lyrics,
            ["Subtitles"] = Icons.Material.Filled.Subtitles,
            ["Cloud"] = Icons.Material.Filled.Cloud,
            ["Collection"] = Icons.Material.Filled.Collections,
        };

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
    /// Returns the hex accent colour for a provider config or display name.
    /// Falls back to the built-in catalogue when live metadata is not yet loaded.
    /// </summary>
    public string GetAccentColor(string providerName)
    {
        var entry = FindEntry(providerName);
        if (!string.IsNullOrWhiteSpace(entry?.AccentColor)) return entry.AccentColor;
        return GetFallback(providerName).Color;
    }

    /// <summary>
    /// Returns the display name for a provider config or display name.
    /// Falls back to the built-in catalogue when live metadata is not yet loaded.
    /// </summary>
    public string GetDisplayName(string providerName)
    {
        var entry = FindEntry(providerName);
        if (!string.IsNullOrWhiteSpace(entry?.DisplayName)) return entry.DisplayName;
        return GetFallback(providerName).DisplayName;
    }

    /// <summary>Returns the resolved Material icon for a provider config or display name.</summary>
    public string GetMaterialIcon(string providerName)
    {
        var entry = FindEntry(providerName);
        return ResolveMaterialIcon(entry?.MaterialIcon) ?? GetFallback(providerName).Icon;
    }

    /// <summary>Returns the provider accent and resolved Material icon as one presentation value.</summary>
    public (string Color, string Icon) GetAccent(string providerName, string? customIconName = null)
    {
        var icon = ResolveMaterialIcon(customIconName);
        return (GetAccentColor(providerName), icon ?? GetMaterialIcon(providerName));
    }

    /// <summary>Returns whether the provider represents a user-facing metadata source.</summary>
    public static bool IsVisibleProvider(string providerName) =>
        !string.Equals(providerName, "local_filesystem", StringComparison.OrdinalIgnoreCase);

    /// <summary>Formats a provider config key without applying provider-specific branding.</summary>
    public static string FormatProviderName(string key) =>
        string.Join(' ', key.Split('_')
            .Select(word => word.Length > 0 ? char.ToUpperInvariant(word[0]) + word[1..] : word));

    /// <summary>Formats compact activity labels while preserving established provider branding.</summary>
    public static string FormatProviderLabel(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return "-";

        var normalized = provider.Trim().Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();
        var compact = normalized.Replace(" ", "", StringComparison.Ordinal);
        return compact switch
        {
            "tmdb" => "TMDB",
            "wikidata" => "Wikidata",
            "openlibrary" => "Open Library",
            "apple" or "appleapi" or "applebooks" or "applemusic" => "Apple",
            "provider" or "providermatch" => "Retail match",
            _ => MediaEngine.Web.Services.Formatting.DisplayFormat.SplitWords(provider),
        };
    }

    /// <summary>Formats a technical source/provider key or well-known provider ID.</summary>
    public static string FormatSourceName(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return "Unknown";

        if (Guid.TryParse(source, out var providerId))
            return FormatProviderName(providerId);

        return source.ToLowerInvariant() switch
        {
            "user_manual" => "Manual Match",
            "local_processor" or "local_filesystem" => "File Scan",
            "file_metadata" => "File Metadata",
            "wikidata_reconciliation" or "wikidata" => "Wikidata",
            "wikipedia" => "Wikipedia",
            "retail_provider" => "Retail Provider",
            "apple_api" => "Apple API",
            "open_library" => "Open Library",
            "musicbrainz" => "MusicBrainz",
            "tmdb" => "TMDB",
            "fanart_tv" => "Fanart.tv",
            "library_scanner" => "Library Scanner",
            _ => source,
        };
    }

    /// <summary>Formats a well-known provider ID.</summary>
    public static string FormatProviderName(Guid providerId) =>
        ProviderNamesById.TryGetValue(providerId, out var name) ? name : providerId.ToString();

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

    private ProviderCatalogueDto? FindEntry(string providerName)
    {
        if (_catalogue is null) return null;
        return _catalogue.FirstOrDefault(provider =>
            string.Equals(provider.Name, providerName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider.DisplayName, providerName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider.ProviderId, providerName, StringComparison.OrdinalIgnoreCase));
    }

    private static ProviderFallback GetFallback(string providerName)
    {
        if (ProviderFallbacks.TryGetValue(providerName, out var fallback))
            return fallback;

        var normalized = new string(providerName
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        var match = ProviderFallbacks.Values.FirstOrDefault(candidate =>
            string.Equals(
                new string(candidate.DisplayName
                    .Where(char.IsLetterOrDigit)
                    .Select(char.ToLowerInvariant)
                    .ToArray()),
                normalized,
                StringComparison.Ordinal));

        return match ?? new ProviderFallback(
            FormatProviderName(providerName),
            "#90A4AE",
            Icons.Material.Filled.Cloud);
    }

    private static string? ResolveMaterialIcon(string? iconName) =>
        !string.IsNullOrWhiteSpace(iconName) && MaterialIcons.TryGetValue(iconName, out var icon)
            ? icon
            : null;

    private sealed record ProviderFallback(string DisplayName, string Color, string Icon);
}

