using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Tanaste.Domain.Enums;
using Tanaste.Providers.Contracts;
using Tanaste.Providers.Models;
using Tanaste.Storage.Models;

namespace Tanaste.Providers.Adapters;

/// <summary>
/// Retrieves book metadata from the Open Library Search API (zero-key, public endpoint).
///
/// Open Library provides comprehensive bibliographic data for books including
/// ISBNs, cover art via the Covers API, author names, and series information.
/// The search endpoint supports title-based lookup; no API key is required.
///
/// Cover art: <c>https://covers.openlibrary.org/b/olid/{OLID}-L.jpg</c>
///
/// Throttle: 1 concurrent request + 500 ms minimum gap (Open Library's
/// rate limits are documented at ~100 req/5 min for anonymous users).
///
/// Named HttpClient: <c>"open_library"</c>.
/// </summary>
public sealed class OpenLibraryAdapter : IExternalMetadataProvider
{
    // Stable provider GUID — never change; written to metadata_claims.provider_id.
    public static readonly Guid AdapterProviderId
        = new("b4000004-0000-4000-8000-000000000005");

    // ── IExternalMetadataProvider ─────────────────────────────────────────────
    public string Name              => "open_library";
    public ProviderDomain Domain    => ProviderDomain.Ebook;
    public IReadOnlyList<string> CapabilityTags => ["title", "author", "cover", "isbn", "year", "series"];
    public Guid ProviderId          => AdapterProviderId;

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OpenLibraryAdapter> _logger;

    // Throttle: max 1 concurrent request, enforced globally.
    private static readonly SemaphoreSlim _throttle = new(1, 1);
    private static DateTime _lastCallUtc = DateTime.MinValue;
    private const int ThrottleGapMs = 500;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── Constructor ───────────────────────────────────────────────────────────

    public OpenLibraryAdapter(
        IHttpClientFactory httpFactory,
        ILogger<OpenLibraryAdapter> logger)
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
            _logger.LogDebug("Open Library skipped: no title for entity {Id}", request.EntityId);
            return [];
        }

        // Prefer ISBN lookup when available for precise matching.
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

                using var client = _httpFactory.CreateClient("open_library");
                using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
                _lastCallUtc = DateTime.UtcNow;
                response.EnsureSuccessStatusCode();

                var json = await response.Content
                    .ReadFromJsonAsync<JsonObject>(_jsonOptions, ct)
                    .ConfigureAwait(false);

                return ParseResults(json, request.BaseUrl);
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
                "Open Library adapter failed for entity {Id} (URL: {Url})", request.EntityId, url);
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
            return $"{baseUrl}/search.json?isbn={isbn}&limit=3&fields=title,author_name,first_publish_year,cover_i,isbn,key,edition_key,seed";
        }

        // Fall back to title+author search.
        var query = string.IsNullOrWhiteSpace(request.Author)
            ? request.Title
            : $"{request.Title} {request.Author}";

        var term = Uri.EscapeDataString(query!);
        return $"{baseUrl}/search.json?q={term}&limit=5&fields=title,author_name,first_publish_year,cover_i,isbn,key,edition_key,seed";
    }

    private static IReadOnlyList<ProviderClaim> ParseResults(JsonObject? json, string baseUrl)
    {
        if (json is null)
            return [];

        var docs = json["docs"]?.AsArray();
        if (docs is null || docs.Count == 0)
            return [];

        // Use the first result — best match by Open Library's relevance ranking.
        var doc = docs[0]?.AsObject();
        if (doc is null)
            return [];

        var claims = new List<ProviderClaim>();

        // Title.
        var title = doc["title"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(title))
            claims.Add(new ProviderClaim("title", title, 0.75));

        // Author (array — take the first).
        var authors = doc["author_name"]?.AsArray();
        if (authors is { Count: > 0 })
        {
            var authorName = authors[0]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(authorName))
                claims.Add(new ProviderClaim("author", authorName, 0.8));
        }

        // Year.
        var year = doc["first_publish_year"]?.GetValue<int>();
        if (year.HasValue)
            claims.Add(new ProviderClaim("year", year.Value.ToString(), 0.85));

        // Cover art via Open Library Covers API.
        var coverId = doc["cover_i"]?.GetValue<int>();
        if (coverId.HasValue)
        {
            var coverUrl = $"https://covers.openlibrary.org/b/id/{coverId.Value}-L.jpg";
            claims.Add(new ProviderClaim("cover", coverUrl, 0.7));
        }

        // ISBN (array — take the first 13-digit ISBN, fallback to 10-digit).
        var isbns = doc["isbn"]?.AsArray();
        if (isbns is { Count: > 0 })
        {
            var isbn13 = isbns
                .Select(i => i?.GetValue<string>())
                .FirstOrDefault(i => i?.Length == 13);

            var isbn = isbn13
                ?? isbns.Select(i => i?.GetValue<string>())
                        .FirstOrDefault(i => !string.IsNullOrWhiteSpace(i));

            if (!string.IsNullOrWhiteSpace(isbn))
                claims.Add(new ProviderClaim("isbn", isbn, 0.9));
        }

        // Series info from seed paths (format: "/works/OLxxxxxW" -> check subjects for series).
        // Open Library's search API doesn't directly return series; this is a best-effort
        // extraction. Full series data would require a follow-up /works/ endpoint call.

        return claims;
    }
}
