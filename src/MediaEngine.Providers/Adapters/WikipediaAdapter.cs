using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Models;

namespace MediaEngine.Providers.Adapters;

/// <summary>
/// Wikipedia adapter that fetches rich descriptions (2-3 paragraph summaries) from Wikipedia
/// using QID→sitelink resolution via the Wikidata API.
///
/// <para>
/// Two-step resolution:
/// <list type="bullet">
///   <item>Step 1: Resolve the Wikipedia article title for the given QID via the Wikidata sitelinks API.</item>
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
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<WikipediaAdapter> _logger;
    private readonly IProviderResponseCacheRepository? _responseCache;

    private readonly Guid   _providerId;
    private readonly int    _throttleMs;
    private readonly int    _cacheTtlHours;

    private readonly SemaphoreSlim _throttle;
    private DateTime _lastCallUtc = DateTime.MinValue;

    private const string WikidataApiBase    = "https://www.wikidata.org/w/api.php";
    private const string WikipediaApiBase   = "https://{lang}.wikipedia.org/api/rest_v1";
    private const string DefaultProviderId  = "b4000004-d000-4000-8000-000000000005";

    public WikipediaAdapter(
        IHttpClientFactory httpFactory,
        ILogger<WikipediaAdapter> logger,
        IProviderResponseCacheRepository? responseCache = null,
        string providerId     = DefaultProviderId,
        int    throttleMs     = 100,
        int    cacheTtlHours  = 168,
        int    maxConcurrency = 2)
    {
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _httpFactory   = httpFactory;
        _logger        = logger;
        _responseCache = responseCache;
        _providerId    = Guid.Parse(providerId);
        _throttleMs    = throttleMs;
        _cacheTtlHours = cacheTtlHours;
        _throttle      = new SemaphoreSlim(Math.Max(1, maxConcurrency), Math.Max(1, maxConcurrency));
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

            // Step 1: Resolve the Wikipedia article title via Wikidata sitelinks.
            var articleTitle = await GetSitelinkTitleAsync(qid, lang, ct).ConfigureAwait(false);

            // If user's language has no sitelink, try English.
            if (articleTitle is null && !string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("{Provider}: No {Lang}wiki sitelink for {Qid}, trying enwiki",
                    Name, lang, qid);
                articleTitle = await GetSitelinkTitleAsync(qid, "en", ct).ConfigureAwait(false);
                if (articleTitle is not null)
                    lang = "en";
            }

            if (articleTitle is null)
            {
                _logger.LogDebug("{Provider}: No Wikipedia sitelink found for {Qid}", Name, qid);
                return [];
            }

            // Step 2: Fetch the Wikipedia summary extract.
            var extract = await FetchSummaryAsync(articleTitle, lang, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(extract))
            {
                _logger.LogDebug("{Provider}: Empty extract for article '{Title}' ({Lang})",
                    Name, articleTitle, lang);
                return [];
            }

            _logger.LogInformation(
                "{Provider}: Got description for {Qid} ('{Title}', {Lang}), {Len} chars",
                Name, qid, articleTitle, lang, extract.Length);

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

    // ── Private: Sitelink resolution ──────────────────────────────────────────

    /// <summary>
    /// Calls the Wikidata API to retrieve the Wikipedia article title for the given QID
    /// in the specified language.
    /// </summary>
    private async Task<string?> GetSitelinkTitleAsync(
        string qid,
        string lang,
        CancellationToken ct)
    {
        var siteKey = $"{lang}wiki";
        var url     = $"{WikidataApiBase}?action=wbgetentities&ids={Uri.EscapeDataString(qid)}&props=sitelinks&sitelinkfilter={siteKey}&format=json";

        var responseJson = await GetCachedOrFetchAsync(url, ct).ConfigureAwait(false);
        if (responseJson is null)
            return null;

        try
        {
            var root = JsonNode.Parse(responseJson);
            // entities → {qid} → sitelinks → {lang}wiki → title
            return root?["entities"]?[qid]?["sitelinks"]?[siteKey]?["title"]?.GetValue<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Provider}: Failed to parse sitelink response for {Qid}/{Lang}",
                Name, qid, lang);
            return null;
        }
    }

    // ── Private: Wikipedia summary ────────────────────────────────────────────

    /// <summary>
    /// Calls the Wikipedia REST API to fetch the plain-text extract for the given article title.
    /// </summary>
    private async Task<string?> FetchSummaryAsync(
        string articleTitle,
        string lang,
        CancellationToken ct)
    {
        var encodedTitle = Uri.EscapeDataString(articleTitle);
        var base_url     = WikipediaApiBase.Replace("{lang}", lang, StringComparison.Ordinal);
        var url          = $"{base_url}/page/summary/{encodedTitle}";

        var responseJson = await GetCachedOrFetchAsync(url, ct).ConfigureAwait(false);
        if (responseJson is null)
            return null;

        try
        {
            var root = JsonNode.Parse(responseJson);
            return root?["extract"]?.GetValue<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Provider}: Failed to parse summary response for '{Title}'",
                Name, articleTitle);
            return null;
        }
    }

    // ── Private: HTTP GET with throttle + cache ───────────────────────────────

    /// <summary>
    /// Checks the response cache, returning the cached JSON if fresh.
    /// On cache miss, performs a throttled HTTP GET and caches the result.
    /// Returns <c>null</c> on network error or non-2xx status.
    /// </summary>
    private async Task<string?> GetCachedOrFetchAsync(string url, CancellationToken ct)
    {
        var cacheKey = BuildCacheKey(url);

        // Cache hit: return without any network call.
        if (_responseCache is not null)
        {
            var cached = await _responseCache.FindAsync(cacheKey, ct).ConfigureAwait(false);
            if (cached is not null)
            {
                _logger.LogDebug("{Provider}: cache HIT for {Url}", Name, url);
                return cached.ResponseJson;
            }

            // Check for an expired entry with an ETag for conditional revalidation.
            var existingEtag = await _responseCache.FindExpiredEtagAsync(cacheKey, ct).ConfigureAwait(false);
            if (existingEtag is not null)
            {
                var revalidated = await GetWithEtagAsync(url, existingEtag, cacheKey, ct).ConfigureAwait(false);
                if (revalidated is not null)
                    return revalidated;
                // If revalidation returned null (304 handled and refreshed), re-query the cache.
                var refreshed = await _responseCache.FindAsync(cacheKey, ct).ConfigureAwait(false);
                if (refreshed is not null)
                    return refreshed.ResponseJson;
            }
        }

        return await GetAndCacheAsync(url, cacheKey, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs a conditional GET using <c>If-None-Match</c>.
    /// On 304 Not Modified, refreshes the cache TTL and returns <c>null</c> (caller re-reads cache).
    /// On 200, caches the new response body and returns it.
    /// </summary>
    private async Task<string?> GetWithEtagAsync(
        string url,
        string etag,
        string cacheKey,
        CancellationToken ct)
    {
        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ApplyThrottleDelayAsync(ct).ConfigureAwait(false);

            using var client  = _httpFactory.CreateClient("wikipedia_api");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            _lastCallUtc = DateTime.UtcNow;

            if ((int)response.StatusCode == 304)
            {
                // Resource unchanged — just extend the TTL.
                if (_responseCache is not null)
                    await _responseCache.RefreshExpiryAsync(cacheKey, _cacheTtlHours, ct).ConfigureAwait(false);
                return null; // Caller will re-read the cache.
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("{Provider}: GET {Url} returned {StatusCode}",
                    Name, url, (int)response.StatusCode);
                return null;
            }

            var body        = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseEtag = response.Headers.ETag?.Tag;

            if (_responseCache is not null && !string.IsNullOrWhiteSpace(body))
            {
                await _responseCache.UpsertAsync(
                    cacheKey, _providerId.ToString(), ComputeSha256(url),
                    body, responseEtag, _cacheTtlHours, ct).ConfigureAwait(false);
            }

            return body;
        }
        finally
        {
            _throttle.Release();
        }
    }

    /// <summary>
    /// Performs a throttled HTTP GET, caches the result, and returns the response body.
    /// Returns <c>null</c> on error.
    /// </summary>
    private async Task<string?> GetAndCacheAsync(string url, string cacheKey, CancellationToken ct)
    {
        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ApplyThrottleDelayAsync(ct).ConfigureAwait(false);

            using var client   = _httpFactory.CreateClient("wikipedia_api");
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            _lastCallUtc = DateTime.UtcNow;

            _logger.LogDebug("{Provider}: GET {Url} → {StatusCode}",
                Name, url, (int)response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("{Provider}: GET {Url} returned {StatusCode}",
                    Name, url, (int)response.StatusCode);
                return null;
            }

            var body        = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseEtag = response.Headers.ETag?.Tag;

            if (_responseCache is not null && !string.IsNullOrWhiteSpace(body))
            {
                await _responseCache.UpsertAsync(
                    cacheKey, _providerId.ToString(), ComputeSha256(url),
                    body, responseEtag, _cacheTtlHours, ct).ConfigureAwait(false);
            }

            return body;
        }
        finally
        {
            _throttle.Release();
        }
    }

    // ── Private: Throttle delay ───────────────────────────────────────────────

    private async Task ApplyThrottleDelayAsync(CancellationToken ct)
    {
        if (_throttleMs <= 0)
            return;

        var elapsed = (DateTime.UtcNow - _lastCallUtc).TotalMilliseconds;
        if (elapsed < _throttleMs)
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(_throttleMs - elapsed), ct)
                .ConfigureAwait(false);
        }
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
