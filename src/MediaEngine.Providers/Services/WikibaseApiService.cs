using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Provides targeted access to the Wikidata Wikibase REST API (<c>api.php</c>) for two
/// capabilities that the Data Extension API cannot supply:
///
/// <list type="bullet">
///   <item>
///     <term>Qualifier extraction (<c>wbgetclaims</c>)</term>
///     <description>
///       Fetches all statements for a specific property including their qualifiers.
///       Used for actor→character mappings: P161 (cast_member) + qualifier P453 (character).
///     </description>
///   </item>
///   <item>
///     <term>Batch entity fetching (<c>wbgetentities</c>)</term>
///     <description>
///       Retrieves labels, descriptions, and sitelinks for up to 50 entities per call.
///       Useful for bulk fictional-entity and person enrichment.
///     </description>
///   </item>
/// </list>
///
/// <para>
/// This is a <b>service</b>, not a pipeline provider. It does not implement
/// <c>IExternalMetadataProvider</c>. It is consumed directly by other services
/// (HydrationPipelineService, RecursiveFictionalEntityService) for supplementary lookups.
/// </para>
///
/// <para>
/// All methods are safe to call concurrently. A <see cref="SemaphoreSlim"/> limits
/// in-flight HTTP requests; a minimum gap between calls respects Wikidata's rate limits.
/// All exceptions are caught internally — methods return empty results rather than throwing.
/// </para>
/// </summary>
public sealed class WikibaseApiService : IWikibaseApiService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const string WikidataApiBase  = "https://www.wikidata.org/w/api.php";
    private const string HttpClientName   = "wikibase_api";
    private const string ProviderId       = "bc000010-0000-4000-8000-000000000020";
    private const int    DefaultThrottleMs    = 200;
    private const int    DefaultCacheTtlHours = 168; // 7 days
    private const int    DefaultMaxConcurrent = 3;
    private const int    BatchSize            = 50;  // Wikibase hard limit for wbgetentities

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<WikibaseApiService> _logger;
    private readonly IProviderResponseCacheRepository? _responseCache;

    private readonly SemaphoreSlim _throttle;
    private DateTime _lastCallUtc = DateTime.MinValue;
    private readonly int _throttleMs;
    private readonly int _cacheTtlHours;
    private readonly Guid _providerId;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new <see cref="WikibaseApiService"/>.
    /// </summary>
    /// <param name="httpFactory">Factory for the named "wikibase_api" HTTP client.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="responseCache">Optional response cache. When provided, HTTP calls are
    /// cached for <paramref name="cacheTtlHours"/> hours to avoid redundant lookups.</param>
    /// <param name="throttleMs">Minimum milliseconds between API calls (default: 200).</param>
    /// <param name="cacheTtlHours">Cache TTL in hours (default: 168 = 7 days).</param>
    /// <param name="maxConcurrency">Maximum concurrent in-flight requests (default: 3).</param>
    public WikibaseApiService(
        IHttpClientFactory httpFactory,
        ILogger<WikibaseApiService> logger,
        IProviderResponseCacheRepository? responseCache = null,
        int throttleMs     = DefaultThrottleMs,
        int cacheTtlHours  = DefaultCacheTtlHours,
        int maxConcurrency = DefaultMaxConcurrent)
    {
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _httpFactory   = httpFactory;
        _logger        = logger;
        _responseCache = responseCache;
        _throttleMs    = Math.Max(0, throttleMs);
        _cacheTtlHours = Math.Max(1, cacheTtlHours);
        _throttle      = new SemaphoreSlim(Math.Max(1, maxConcurrency), Math.Max(1, maxConcurrency));
        _providerId    = Guid.Parse(ProviderId);
    }

    // ── IWikibaseApiService ───────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<QualifiedStatement>> GetClaimsAsync(
        string entityQid,
        string propertyId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entityQid) || string.IsNullOrWhiteSpace(propertyId))
            return [];

        var url = $"{WikidataApiBase}?action=wbgetclaims" +
                  $"&entity={Uri.EscapeDataString(entityQid)}" +
                  $"&property={Uri.EscapeDataString(propertyId)}" +
                  $"&format=json";

        try
        {
            var responseJson = await GetCachedOrFetchAsync(url, ct).ConfigureAwait(false);
            if (responseJson is null)
                return [];

            return ParseClaims(responseJson, propertyId, entityQid);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WikibaseApiService: GetClaimsAsync failed for {Qid}/{Property}",
                entityQid, propertyId);
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WikibaseEntity>> GetEntitiesBatchAsync(
        IReadOnlyList<string> qids,
        string language = "en",
        CancellationToken ct = default)
    {
        if (qids.Count == 0)
            return [];

        var lang = NormalizeLang(language);
        var results = new List<WikibaseEntity>(qids.Count);

        // Split into batches of BatchSize (Wikibase hard limit).
        var chunks = qids
            .Select((qid, idx) => (qid, idx))
            .GroupBy(x => x.idx / BatchSize)
            .Select(g => g.Select(x => x.qid).ToList())
            .ToList();

        foreach (var chunk in chunks)
        {
            var batchResults = await FetchEntitiesBatchAsync(chunk, lang, ct).ConfigureAwait(false);
            results.AddRange(batchResults);
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<string?> GetSitelinkAsync(
        string entityQid,
        string language = "en",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entityQid))
            return null;

        var lang    = NormalizeLang(language);
        var siteKey = $"{lang}wiki";
        var url     = $"{WikidataApiBase}?action=wbgetentities" +
                      $"&ids={Uri.EscapeDataString(entityQid)}" +
                      $"&props=sitelinks" +
                      $"&sitelinkfilter={Uri.EscapeDataString(siteKey)}" +
                      $"&format=json";

        try
        {
            var responseJson = await GetCachedOrFetchAsync(url, ct).ConfigureAwait(false);
            if (responseJson is null)
                return null;

            var root = JsonNode.Parse(responseJson);
            return root?["entities"]?[entityQid]?["sitelinks"]?[siteKey]?["title"]?.GetValue<string>();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WikibaseApiService: GetSitelinkAsync failed for {Qid}/{Lang}",
                entityQid, language);
            return null;
        }
    }

    // ── Private: Batch entity fetch ───────────────────────────────────────────

    private async Task<IReadOnlyList<WikibaseEntity>> FetchEntitiesBatchAsync(
        IReadOnlyList<string> qids,
        string lang,
        CancellationToken ct)
    {
        var ids = string.Join("|", qids.Select(Uri.EscapeDataString));
        var url = $"{WikidataApiBase}?action=wbgetentities" +
                  $"&ids={ids}" +
                  $"&props=labels|descriptions|sitelinks" +
                  $"&languages={Uri.EscapeDataString(lang)}" +
                  $"&format=json";

        try
        {
            var responseJson = await GetCachedOrFetchAsync(url, ct).ConfigureAwait(false);
            if (responseJson is null)
                return [];

            return ParseEntities(responseJson, lang);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "WikibaseApiService: FetchEntitiesBatchAsync failed for {Count} QIDs ({First}…)",
                qids.Count, qids[0]);
            return [];
        }
    }

    // ── Private: JSON parsing ─────────────────────────────────────────────────

    /// <summary>
    /// Parses a <c>wbgetclaims</c> response into <see cref="QualifiedStatement"/> objects.
    /// Skips novalue and somevalue snaks (no data to extract).
    /// </summary>
    private IReadOnlyList<QualifiedStatement> ParseClaims(
        string responseJson,
        string propertyId,
        string entityQid)
    {
        try
        {
            var root       = JsonNode.Parse(responseJson);
            var claimsNode = root?["claims"]?[propertyId];

            if (claimsNode is not JsonArray statementsArray)
                return [];

            var results = new List<QualifiedStatement>();

            foreach (var statementNode in statementsArray)
            {
                if (statementNode is null)
                    continue;

                // Main snak
                var mainSnak   = statementNode["mainsnak"];
                var snakType   = mainSnak?["snaktype"]?.GetValue<string>() ?? string.Empty;

                // Skip novalue and somevalue — no meaningful data.
                if (!string.Equals(snakType, "value", StringComparison.OrdinalIgnoreCase))
                    continue;

                var datavalue  = mainSnak?["datavalue"];
                var (valueQid, _) = ExtractEntityOrLiteral(datavalue);

                var rank = statementNode["rank"]?.GetValue<string>() ?? "normal";

                // Parse qualifiers
                var qualifiers = ParseQualifiers(statementNode["qualifiers"]);

                results.Add(new QualifiedStatement(
                    ValueQid:   valueQid,
                    ValueLabel: null, // Labels resolved separately via GetEntitiesBatchAsync if needed
                    Rank:       rank,
                    Qualifiers: qualifiers));
            }

            _logger.LogDebug(
                "WikibaseApiService: Parsed {Count} statements for {Qid}/{Property}",
                results.Count, entityQid, propertyId);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "WikibaseApiService: Failed to parse wbgetclaims response for {Qid}/{Property}",
                entityQid, propertyId);
            return [];
        }
    }

    /// <summary>
    /// Parses the qualifiers node of a single statement.
    /// Returns a dictionary mapping property ID → list of qualifier values.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<QualifierValue>> ParseQualifiers(
        JsonNode? qualifiersNode)
    {
        if (qualifiersNode is null)
            return new Dictionary<string, IReadOnlyList<QualifierValue>>();

        var result = new Dictionary<string, IReadOnlyList<QualifierValue>>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in qualifiersNode.AsObject())
        {
            var propId   = prop.Key;
            var snakList = prop.Value as JsonArray;
            if (snakList is null)
                continue;

            var values = new List<QualifierValue>();
            foreach (var snakNode in snakList)
            {
                var snakType  = snakNode?["snaktype"]?.GetValue<string>() ?? string.Empty;
                if (!string.Equals(snakType, "value", StringComparison.OrdinalIgnoreCase))
                    continue;

                var datavalue = snakNode?["datavalue"];
                var (qid, literal) = ExtractEntityOrLiteral(datavalue);
                var dataType  = datavalue?["type"]?.GetValue<string>() ?? "unknown";

                values.Add(new QualifierValue(
                    EntityQid: qid,
                    Label:     qid is not null ? null : literal,
                    DataType:  dataType));
            }

            if (values.Count > 0)
                result[propId] = values;
        }

        return result;
    }

    /// <summary>
    /// Extracts either a QID (for wikibase-entityid) or a literal string (for time/string types)
    /// from a Wikibase datavalue node. Returns (null, null) if extraction fails.
    /// </summary>
    private static (string? EntityQid, string? Literal) ExtractEntityOrLiteral(JsonNode? datavalue)
    {
        if (datavalue is null)
            return (null, null);

        var type = datavalue["type"]?.GetValue<string>();

        return type switch
        {
            "wikibase-entityid" => (datavalue["value"]?["id"]?.GetValue<string>(), null),
            "time"              => (null, datavalue["value"]?["time"]?.GetValue<string>()),
            "string"            => (null, datavalue["value"]?.GetValue<string>()),
            "monolingualtext"   => (null, datavalue["value"]?["text"]?.GetValue<string>()),
            "quantity"          => (null, datavalue["value"]?["amount"]?.GetValue<string>()),
            _                   => (null, null),
        };
    }

    /// <summary>
    /// Parses a <c>wbgetentities</c> response into <see cref="WikibaseEntity"/> objects.
    /// Missing or invalid entities are silently skipped.
    /// </summary>
    private IReadOnlyList<WikibaseEntity> ParseEntities(string responseJson, string lang)
    {
        try
        {
            var root           = JsonNode.Parse(responseJson);
            var entitiesNode   = root?["entities"]?.AsObject();

            if (entitiesNode is null)
                return [];

            var results = new List<WikibaseEntity>();

            foreach (var entry in entitiesNode)
            {
                var qid = entry.Key;
                var entityData = entry.Value;

                if (entityData is null)
                    continue;

                // Skip "missing" entities returned by the API.
                if (entityData["missing"] is not null)
                    continue;

                var label       = entityData["labels"]?[lang]?["value"]?.GetValue<string>();
                var description = entityData["descriptions"]?[lang]?["value"]?.GetValue<string>();

                // Parse sitelinks: site → title
                var sitelinks   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var sitelinksNode = entityData["sitelinks"]?.AsObject();
                if (sitelinksNode is not null)
                {
                    foreach (var sl in sitelinksNode)
                    {
                        var title = sl.Value?["title"]?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(title))
                            sitelinks[sl.Key] = title;
                    }
                }

                results.Add(new WikibaseEntity(
                    Qid:         qid,
                    Label:       label,
                    Description: description,
                    Sitelinks:   sitelinks));
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WikibaseApiService: Failed to parse wbgetentities response");
            return [];
        }
    }

    // ── Private: HTTP GET with throttle + cache ───────────────────────────────

    /// <summary>
    /// Checks the response cache first. On a cache miss, performs a throttled HTTP GET
    /// and caches the result. Returns <c>null</c> on network error or non-2xx response.
    /// </summary>
    private async Task<string?> GetCachedOrFetchAsync(string url, CancellationToken ct)
    {
        var cacheKey = BuildCacheKey(url);

        if (_responseCache is not null)
        {
            // Cache hit: return without any network call.
            var cached = await _responseCache.FindAsync(cacheKey, ct).ConfigureAwait(false);
            if (cached is not null)
            {
                _logger.LogDebug("WikibaseApiService: cache HIT for {Url}", url);
                return cached.ResponseJson;
            }

            // Expired entry with ETag: attempt conditional revalidation.
            var existingEtag = await _responseCache.FindExpiredEtagAsync(cacheKey, ct).ConfigureAwait(false);
            if (existingEtag is not null)
            {
                var revalidated = await GetWithEtagAsync(url, existingEtag, cacheKey, ct).ConfigureAwait(false);
                if (revalidated is not null)
                    return revalidated;

                // 304 Not Modified — refreshed TTL; re-read from cache.
                var refreshed = await _responseCache.FindAsync(cacheKey, ct).ConfigureAwait(false);
                if (refreshed is not null)
                    return refreshed.ResponseJson;
            }
        }

        return await GetAndCacheAsync(url, cacheKey, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs a conditional GET using <c>If-None-Match</c>.
    /// On 304, refreshes the cache TTL and returns <c>null</c> (caller re-reads cache).
    /// On 200, caches the new body and returns it.
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

            using var client  = _httpFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            _lastCallUtc = DateTime.UtcNow;

            if ((int)response.StatusCode == 304)
            {
                if (_responseCache is not null)
                    await _responseCache.RefreshExpiryAsync(cacheKey, _cacheTtlHours, ct).ConfigureAwait(false);
                return null; // Caller will re-read from cache.
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("WikibaseApiService: GET {Url} returned {StatusCode}",
                    url, (int)response.StatusCode);
                return null;
            }

            var body         = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseEtag = response.Headers.ETag?.Tag;

            if (_responseCache is not null && !string.IsNullOrWhiteSpace(body))
            {
                await _responseCache.UpsertAsync(
                    cacheKey, ProviderId, ComputeSha256(url),
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
    /// Returns <c>null</c> on network error or non-2xx response.
    /// </summary>
    private async Task<string?> GetAndCacheAsync(string url, string cacheKey, CancellationToken ct)
    {
        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ApplyThrottleDelayAsync(ct).ConfigureAwait(false);

            using var client   = _httpFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            _lastCallUtc = DateTime.UtcNow;

            _logger.LogDebug("WikibaseApiService: GET {Url} → {StatusCode}",
                url, (int)response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("WikibaseApiService: GET {Url} returned {StatusCode}",
                    url, (int)response.StatusCode);
                return null;
            }

            var body         = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseEtag = response.Headers.ETag?.Tag;

            if (_responseCache is not null && !string.IsNullOrWhiteSpace(body))
            {
                await _responseCache.UpsertAsync(
                    cacheKey, ProviderId, ComputeSha256(url),
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
        $"{ProviderId}:{ComputeSha256(input)}";

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
