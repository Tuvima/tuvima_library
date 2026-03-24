using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Models;
using Tuvima.WikidataReconciliation;

namespace MediaEngine.Providers.Adapters;

/// <summary>
/// Wikipedia adapter that fetches rich descriptions (2-3 paragraph summaries) from Wikipedia
/// using QID→sitelink resolution via <see cref="WikidataReconciler.GetWikipediaUrlsAsync"/>,
/// then fetches the plain-text extract from the Wikipedia REST Summary API.
///
/// <para>
/// Two-step resolution:
/// <list type="bullet">
///   <item>Step 1: Resolve the Wikipedia article URL for the given QID via
///   <see cref="WikidataReconciler.GetWikipediaUrlsAsync"/>.</item>
///   <item>Step 2: Fetch the plain-text extract (2-3 paragraphs) from the Wikipedia REST Summary API.</item>
/// </list>
/// </para>
///
/// <para>
/// Language handling: attempts the user's language first (from <see cref="ProviderLookupRequest.Language"/>),
/// then falls back to English ("en") if no sitelink exists for that language.
/// </para>
///
/// <para>
/// This adapter is config-driven via <c>config/providers/wikipedia.json</c> with
/// <c>adapter_type: "coded"</c>. Provider GUID: b4000004-d000-4000-8000-000000000005.
/// </para>
/// </summary>
public sealed class WikipediaAdapter : IExternalMetadataProvider
{
    private readonly WikidataReconciler _reconciler;
    private readonly ILogger<WikipediaAdapter> _logger;
    private readonly IProviderResponseCacheRepository? _responseCache;
    private readonly IHttpClientFactory _httpFactory;

    private readonly Guid _providerId;
    private readonly int  _cacheTtlHours;

    private const string WikipediaRestBase  = "https://{lang}.wikipedia.org/api/rest_v1";
    private const string DefaultProviderId  = "b4000004-d000-4000-8000-000000000005";

    public WikipediaAdapter(
        WikidataReconciler reconciler,
        IHttpClientFactory httpFactory,
        ILogger<WikipediaAdapter> logger,
        IProviderResponseCacheRepository? responseCache = null,
        string providerId    = DefaultProviderId,
        int    cacheTtlHours = 168)
    {
        ArgumentNullException.ThrowIfNull(reconciler);
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _reconciler    = reconciler;
        _httpFactory   = httpFactory;
        _logger        = logger;
        _responseCache = responseCache;
        _providerId    = Guid.Parse(providerId);
        _cacheTtlHours = cacheTtlHours;
    }

    // ── IExternalMetadataProvider ─────────────────────────────────────────────

    public string Name => "wikipedia";

    public ProviderDomain Domain => ProviderDomain.Universal;

    public IReadOnlyList<string> CapabilityTags => ["description"];

    public Guid ProviderId => _providerId;

    /// <summary>Universal provider: handles all media types.</summary>
    public bool CanHandle(MediaType mediaType) => true;

    /// <summary>Handles MediaAsset and Person entity types.</summary>
    public bool CanHandle(EntityType entityType) =>
        entityType is EntityType.MediaAsset or EntityType.Person;

    /// <summary>
    /// Fetches a Wikipedia description for the given entity.
    ///
    /// Requires <see cref="ProviderLookupRequest.PreResolvedQid"/> to be set (populated by Stage 1).
    /// Falls back gracefully — always returns an empty list rather than throwing.
    /// </summary>
    public async Task<IReadOnlyList<ProviderClaim>> FetchAsync(
        ProviderLookupRequest request,
        CancellationToken ct = default)
    {
        if (!CanHandle(request.EntityType))
            return [];

        try
        {
            var qid = request.PreResolvedQid;
            if (string.IsNullOrWhiteSpace(qid))
            {
                _logger.LogDebug("{Provider}: No QID available for entity {EntityId}, skipping",
                    Name, request.EntityId);
                return [];
            }

            var lang = NormalizeLang(request.Language);

            // Step 1: Resolve the Wikipedia article URL via WikidataReconciler.
            // GetWikipediaUrlsAsync returns a QID → Wikipedia URL dictionary for the given language.
            var urls = await _reconciler.GetWikipediaUrlsAsync([qid], lang, ct).ConfigureAwait(false);

            string? articleUrl = null;
            string  resolvedLang = lang;

            if (urls.TryGetValue(qid, out var url) && !string.IsNullOrWhiteSpace(url))
            {
                articleUrl = url;
            }
            else if (!string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase))
            {
                // Fall back to English if no sitelink exists for the requested language.
                _logger.LogDebug("{Provider}: No {Lang}wiki sitelink for {Qid}, trying enwiki",
                    Name, lang, qid);

                var enUrls = await _reconciler.GetWikipediaUrlsAsync([qid], "en", ct).ConfigureAwait(false);
                if (enUrls.TryGetValue(qid, out var enUrl) && !string.IsNullOrWhiteSpace(enUrl))
                {
                    articleUrl   = enUrl;
                    resolvedLang = "en";
                }
            }

            if (articleUrl is null)
            {
                _logger.LogDebug("{Provider}: No Wikipedia sitelink found for {Qid}", Name, qid);
                return [];
            }

            // Extract the article title from the URL for the REST summary endpoint.
            // URL format: https://{lang}.wikipedia.org/wiki/{Title}
            var articleTitle = ExtractTitleFromUrl(articleUrl);
            if (articleTitle is null)
            {
                _logger.LogDebug("{Provider}: Could not extract article title from URL '{Url}'",
                    Name, articleUrl);
                return [];
            }

            // Step 2: Fetch the Wikipedia REST summary extract.
            var extract = await FetchSummaryAsync(articleTitle, resolvedLang, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(extract))
            {
                _logger.LogDebug("{Provider}: Empty extract for article '{Title}' ({Lang})",
                    Name, articleTitle, resolvedLang);
                return [];
            }

            _logger.LogInformation(
                "{Provider}: Got description for {Qid} ('{Title}', {Lang}), {Len} chars",
                Name, qid, articleTitle, resolvedLang, extract.Length);

            return [new ProviderClaim("description", extract, 0.90)];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Provider}: FetchAsync failed for entity {EntityId}",
                Name, request.EntityId);
            return [];
        }
    }

    // ── Private: Wikipedia REST summary ──────────────────────────────────────

    /// <summary>
    /// Calls the Wikipedia REST API to fetch the plain-text extract for the given article title.
    /// Checks and populates the app-level response cache.
    /// </summary>
    private async Task<string?> FetchSummaryAsync(
        string articleTitle,
        string lang,
        CancellationToken ct)
    {
        var encodedTitle = Uri.EscapeDataString(articleTitle);
        var baseUrl      = WikipediaRestBase.Replace("{lang}", lang, StringComparison.Ordinal);
        var url          = $"{baseUrl}/page/summary/{encodedTitle}";
        var cacheKey     = BuildCacheKey(url);

        // Cache hit: return without any network call.
        if (_responseCache is not null)
        {
            var cached = await _responseCache.FindAsync(cacheKey, ct).ConfigureAwait(false);
            if (cached is not null)
            {
                _logger.LogDebug("{Provider}: cache HIT for {Url}", Name, url);
                return ParseExtract(cached.ResponseJson);
            }

            // Check for an expired entry with an ETag for conditional revalidation.
            var existingEtag = await _responseCache.FindExpiredEtagAsync(cacheKey, ct).ConfigureAwait(false);
            if (existingEtag is not null)
            {
                var revalidated = await GetWithEtagAsync(url, existingEtag, cacheKey, ct).ConfigureAwait(false);
                if (revalidated is not null)
                    return ParseExtract(revalidated);
                // 304 handled — TTL was refreshed; re-read the cache.
                var refreshed = await _responseCache.FindAsync(cacheKey, ct).ConfigureAwait(false);
                if (refreshed is not null)
                    return ParseExtract(refreshed.ResponseJson);
            }
        }

        var responseBody = await GetAndCacheAsync(url, cacheKey, ct).ConfigureAwait(false);
        return responseBody is not null ? ParseExtract(responseBody) : null;
    }

    // ── Private: HTTP GET with cache (summary only) ───────────────────────────

    /// <summary>
    /// Performs a conditional GET using <c>If-None-Match</c> for the Wikipedia summary endpoint.
    /// On 304, refreshes the cache TTL and returns <c>null</c> (caller re-reads cache).
    /// On 200, caches the new response body and returns it.
    /// </summary>
    private async Task<string?> GetWithEtagAsync(
        string url,
        string etag,
        string cacheKey,
        CancellationToken ct)
    {
        using var client  = _httpFactory.CreateClient("wikipedia_api");
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        message.Headers.TryAddWithoutValidation("If-None-Match", etag);

        using var response = await client.SendAsync(message, ct).ConfigureAwait(false);

        if ((int)response.StatusCode == 304)
        {
            if (_responseCache is not null)
                await _responseCache.RefreshExpiryAsync(cacheKey, _cacheTtlHours, ct).ConfigureAwait(false);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("{Provider}: GET {Url} returned {StatusCode}",
                Name, url, (int)response.StatusCode);
            return null;
        }

        var body         = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var responseEtag = response.Headers.ETag?.Tag;

        if (_responseCache is not null && !string.IsNullOrWhiteSpace(body))
        {
            await _responseCache.UpsertAsync(
                cacheKey, _providerId.ToString(), ComputeSha256(url),
                body, responseEtag, _cacheTtlHours, ct).ConfigureAwait(false);
        }

        return body;
    }

    /// <summary>
    /// Performs an HTTP GET for the Wikipedia summary endpoint, caches the result,
    /// and returns the response body.  Returns <c>null</c> on error.
    /// </summary>
    private async Task<string?> GetAndCacheAsync(string url, string cacheKey, CancellationToken ct)
    {
        using var client   = _httpFactory.CreateClient("wikipedia_api");
        using var response = await client.GetAsync(url, ct).ConfigureAwait(false);

        _logger.LogDebug("{Provider}: GET {Url} → {StatusCode}",
            Name, url, (int)response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("{Provider}: GET {Url} returned {StatusCode}",
                Name, url, (int)response.StatusCode);
            return null;
        }

        var body         = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var responseEtag = response.Headers.ETag?.Tag;

        if (_responseCache is not null && !string.IsNullOrWhiteSpace(body))
        {
            await _responseCache.UpsertAsync(
                cacheKey, _providerId.ToString(), ComputeSha256(url),
                body, responseEtag, _cacheTtlHours, ct).ConfigureAwait(false);
        }

        return body;
    }

    // ── Private: JSON parsing ─────────────────────────────────────────────────

    private string? ParseExtract(string responseJson)
    {
        try
        {
            var root = JsonNode.Parse(responseJson);
            return root?["extract"]?.GetValue<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Provider}: Failed to parse summary response JSON", Name);
            return null;
        }
    }

    // ── Private: URL title extraction ─────────────────────────────────────────

    /// <summary>
    /// Extracts the article title from a Wikipedia URL of the form
    /// <c>https://{lang}.wikipedia.org/wiki/{Title}</c>.
    /// </summary>
    private static string? ExtractTitleFromUrl(string url)
    {
        const string wikiSegment = "/wiki/";
        var idx = url.IndexOf(wikiSegment, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var title = url[(idx + wikiSegment.Length)..];
        return string.IsNullOrWhiteSpace(title) ? null : Uri.UnescapeDataString(title);
    }

    // ── Private: Cache key + SHA-256 ─────────────────────────────────────────

    private string BuildCacheKey(string input) =>
        $"{_providerId}:{ComputeSha256(input)}";

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ── Private: Language normalisation ──────────────────────────────────────

    private static string NormalizeLang(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
            return "en";

        // BCP-47 codes may include region (e.g. "en-US"); keep only the primary subtag.
        var primary = lang.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
        return primary.ToLowerInvariant();
    }
}
