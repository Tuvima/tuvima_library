using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tanaste.Domain.Enums;
using Tanaste.Providers.Contracts;
using Tanaste.Providers.Models;
using Tanaste.Storage.Models;

namespace Tanaste.Providers.Adapters;

/// <summary>
/// Retrieves book metadata from the Google Books API (zero-key, public endpoint).
///
/// Google Books provides high-quality cover art, descriptions, ISBNs, ratings,
/// and publication dates. The Volumes API supports title + author search and
/// ISBN lookup without requiring an API key (anonymous access allows ~1000
/// requests per day, which is sufficient for a single-user library).
///
/// Cover art: <c>volumeInfo.imageLinks.thumbnail</c> — upgraded to zoom=1
/// for larger images by replacing <c>zoom=5</c> or appending <c>&amp;zoom=1</c>.
///
/// Throttle: 1 concurrent request + 200 ms minimum gap.
///
/// Named HttpClient: <c>"google_books"</c>.
/// </summary>
public sealed partial class GoogleBooksAdapter : IExternalMetadataProvider
{
    // Stable provider GUID — never change; written to metadata_claims.provider_id.
    public static readonly Guid AdapterProviderId
        = new("b5000005-0000-4000-8000-000000000006");

    // ── IExternalMetadataProvider ─────────────────────────────────────────────
    public string Name              => "google_books";
    public ProviderDomain Domain    => ProviderDomain.Ebook;
    public IReadOnlyList<string> CapabilityTags =>
        ["title", "author", "cover", "description", "isbn", "year", "rating"];
    public Guid ProviderId          => AdapterProviderId;

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<GoogleBooksAdapter> _logger;

    // Throttle: max 1 concurrent request, enforced globally.
    private static readonly SemaphoreSlim _throttle = new(1, 1);
    private static DateTime _lastCallUtc = DateTime.MinValue;
    private const int ThrottleGapMs = 200;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── Constructor ───────────────────────────────────────────────────────────

    public GoogleBooksAdapter(
        IHttpClientFactory httpFactory,
        ILogger<GoogleBooksAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    // ── IExternalMetadataProvider ─────────────────────────────────────────────

    /// <inheritdoc/>
    public bool CanHandle(MediaType mediaType) => mediaType == MediaType.Epub;

    /// <inheritdoc/>
    public bool CanHandle(EntityType entityType) =>
        entityType == EntityType.Work || entityType == EntityType.MediaAsset;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProviderClaim>> FetchAsync(
        ProviderLookupRequest request,
        CancellationToken ct = default)
    {
        if (request.MediaType != MediaType.Epub)
            return [];

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            _logger.LogDebug("Google Books skipped: no title for entity {Id}", request.EntityId);
            return [];
        }

        var url = BuildSearchUrl(request);

        try
        {
            await _throttle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var now = DateTime.UtcNow;
                var elapsed = (now - _lastCallUtc).TotalMilliseconds;
                if (elapsed < ThrottleGapMs)
                    await Task.Delay(TimeSpan.FromMilliseconds(ThrottleGapMs - elapsed), ct)
                              .ConfigureAwait(false);

                using var client = _httpFactory.CreateClient("google_books");
                using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
                _lastCallUtc = DateTime.UtcNow;
                response.EnsureSuccessStatusCode();

                var json = await response.Content
                    .ReadFromJsonAsync<JsonObject>(_jsonOptions, ct)
                    .ConfigureAwait(false);

                return ParseResults(json);
            }
            finally
            {
                _throttle.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex,
                "Google Books adapter failed for entity {Id} (URL: {Url})", request.EntityId, url);
            return [];
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string BuildSearchUrl(ProviderLookupRequest request)
    {
        var baseUrl = request.BaseUrl.TrimEnd('/');

        // ISBN search gives exact matches.
        if (!string.IsNullOrWhiteSpace(request.Isbn))
        {
            var isbn = Uri.EscapeDataString(request.Isbn);
            return $"{baseUrl}/volumes?q=isbn:{isbn}&maxResults=3";
        }

        // Title + author search.
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Title))
            parts.Add($"intitle:{Uri.EscapeDataString(request.Title)}");
        if (!string.IsNullOrWhiteSpace(request.Author))
            parts.Add($"inauthor:{Uri.EscapeDataString(request.Author)}");

        var query = string.Join("+", parts);
        return $"{baseUrl}/volumes?q={query}&maxResults=5";
    }

    private static IReadOnlyList<ProviderClaim> ParseResults(JsonObject? json)
    {
        if (json is null)
            return [];

        var totalItems = json["totalItems"]?.GetValue<int>() ?? 0;
        if (totalItems == 0)
            return [];

        var items = json["items"]?.AsArray();
        if (items is null || items.Count == 0)
            return [];

        // Use the first result — best match by Google's relevance ranking.
        var volumeInfo = items[0]?["volumeInfo"]?.AsObject();
        if (volumeInfo is null)
            return [];

        var claims = new List<ProviderClaim>();

        // Title.
        var title = volumeInfo["title"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(title))
            claims.Add(new ProviderClaim("title", title, 0.7));

        // Author (array — take the first).
        var authors = volumeInfo["authors"]?.AsArray();
        if (authors is { Count: > 0 })
        {
            var authorName = authors[0]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(authorName))
                claims.Add(new ProviderClaim("author", authorName, 0.75));
        }

        // Year (published date may be "YYYY", "YYYY-MM", or "YYYY-MM-DD").
        var publishedDate = volumeInfo["publishedDate"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(publishedDate) && publishedDate.Length >= 4)
        {
            var yearStr = publishedDate[..4];
            if (int.TryParse(yearStr, out _))
                claims.Add(new ProviderClaim("year", yearStr, 0.8));
        }

        // Description (may contain HTML — strip tags).
        var description = volumeInfo["description"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(description))
        {
            var stripped = HtmlTagRegex().Replace(description, string.Empty).Trim();
            if (stripped.Length > 0)
                claims.Add(new ProviderClaim("description", stripped, 0.8));
        }

        // Cover art — upgrade to larger image.
        var imageLinks = volumeInfo["imageLinks"]?.AsObject();
        if (imageLinks is not null)
        {
            // Prefer thumbnail, then smallThumbnail.
            var coverUrl = imageLinks["thumbnail"]?.GetValue<string>()
                        ?? imageLinks["smallThumbnail"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(coverUrl))
            {
                // Upgrade: replace zoom parameter for larger image.
                var upgraded = UpgradeCoverUrl(coverUrl);
                claims.Add(new ProviderClaim("cover", upgraded, 0.75));
            }
        }

        // ISBN (from industry identifiers array — prefer ISBN_13 over ISBN_10).
        var identifiers = volumeInfo["industryIdentifiers"]?.AsArray();
        if (identifiers is { Count: > 0 })
        {
            string? isbn13 = null;
            string? isbn10 = null;

            foreach (var id in identifiers)
            {
                var idType = id?["type"]?.GetValue<string>();
                var idValue = id?["identifier"]?.GetValue<string>();
                if (string.Equals(idType, "ISBN_13", StringComparison.OrdinalIgnoreCase))
                    isbn13 = idValue;
                else if (string.Equals(idType, "ISBN_10", StringComparison.OrdinalIgnoreCase))
                    isbn10 = idValue;
            }

            var isbn = isbn13 ?? isbn10;
            if (!string.IsNullOrWhiteSpace(isbn))
                claims.Add(new ProviderClaim("isbn", isbn, 0.85));
        }

        // Average rating.
        var averageRating = volumeInfo["averageRating"]?.GetValue<double>();
        if (averageRating.HasValue)
            claims.Add(new ProviderClaim("rating", averageRating.Value.ToString("F2"), 0.7));

        return claims;
    }

    /// <summary>
    /// Upgrades Google Books cover URLs to higher resolution.
    /// Replaces <c>zoom=5</c> (thumbnail) with <c>zoom=1</c> (full size),
    /// or appends <c>&amp;zoom=1</c> if no zoom parameter exists.
    /// Also forces HTTPS.
    /// </summary>
    private static string UpgradeCoverUrl(string url)
    {
        var upgraded = url.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase);

        if (upgraded.Contains("zoom=", StringComparison.OrdinalIgnoreCase))
        {
            upgraded = ZoomRegex().Replace(upgraded, "zoom=1");
        }
        else if (upgraded.Contains('?'))
        {
            upgraded += "&zoom=1";
        }
        else
        {
            upgraded += "?zoom=1";
        }

        return upgraded;
    }

    [GeneratedRegex(@"zoom=\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ZoomRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();
}
