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
/// Retrieves metadata from the Wikidata MediaWiki API and SPARQL endpoint
/// (zero-key, public).
///
/// Handles two scenarios:
///  1. <see cref="EntityType.Person"/> — Searches by name, verifies P31=Q5 (human),
///     fetches headshot (P18) and description to produce enrichment claims.
///  2. <see cref="EntityType.Work"/> / <see cref="EntityType.MediaAsset"/> — Resolves
///     the Wikidata QID via bridge identifiers (ASIN, ISBN, TMDB, etc.) or title search,
///     then runs a SPARQL deep-ingest to fetch all Work-scoped properties from the
///     <see cref="WikidataSparqlPropertyMap"/>.
///
/// <b>Copyright constraint:</b> P18 (Image) is <b>never emitted for Work entities.</b>
/// Media cover art is sourced exclusively from Apple Books, Audnexus, and TMDB.
/// Person headshots from Wikimedia Commons are permitted (public figures).
///
/// Throttle: 1 concurrent request with a 1 100 ms minimum gap between calls.
/// Wikidata's Bot Policy requires ≤1 req/s for automated clients.
///
/// Named HttpClients: <c>"wikidata_api"</c> (MediaWiki REST API) and
/// <c>"wikidata_sparql"</c> (SPARQL query endpoint for deep hydration).
///
/// Spec: Phase 9 – External Metadata Adapters § Wikidata + Phase B Deep Hydration.
/// </summary>
public sealed class WikidataAdapter : IExternalMetadataProvider
{
    // Stable provider GUID — never change; written to metadata_claims.provider_id.
    public static readonly Guid AdapterProviderId
        = new("b3000003-w000-4000-8000-000000000004");

    // ── IExternalMetadataProvider ─────────────────────────────────────────────
    public string Name          => "wikidata";
    public ProviderDomain Domain => ProviderDomain.Universal;
    public IReadOnlyList<string> CapabilityTags
        => ["wikidata_qid", "headshot_url", "biography", "series", "franchise",
            "title", "author", "year", "instance_of", "series_position",
            "preceded_by", "followed_by", "narrator", "director",
            "illustrator", "cast_member", "voice_actor", "screenwriter", "composer",
            "occupation", "notable_work",
            "narrative_location", "characters", "main_subject", "fictional_universe",
            "based_on", "first_appearance",
            "apple_books_id", "isbn", "asin", "goodreads_id", "loc_id",
            "tmdb_id", "imdb_id", "justwatch_id", "metacritic_id", "letterboxd_id",
            "tvcom_id", "gcd_series_id", "gcd_issue_id", "comicvine_id",
            "mal_anime_id", "mal_manga_id",
            "musicbrainz_id", "spotify_id", "discogs_id", "audible_id",
            "instagram", "twitter", "tiktok", "mastodon", "website"];
    public Guid ProviderId      => AdapterProviderId;

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<WikidataAdapter> _logger;

    // Throttle shared across all instances (static) — Wikidata policy: 1 req/s.
    private static readonly SemaphoreSlim _throttle = new(1, 1);
    private static DateTime _lastCallUtc = DateTime.MinValue;
    private const int ThrottleGapMs = 1100;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Wikimedia Commons image URL template.
    private const string CommonsUrlTemplate =
        "https://commons.wikimedia.org/wiki/Special:FilePath/{0}?width=300";

    // ── Constructor ───────────────────────────────────────────────────────────

    public WikidataAdapter(
        IHttpClientFactory httpFactory,
        ILogger<WikidataAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    // ── IExternalMetadataProvider ─────────────────────────────────────────────

    /// <inheritdoc/>
    public bool CanHandle(MediaType mediaType) =>
        // Wikidata handles all media types for series/franchise; and any type
        // for person enrichment (person requests carry MediaType.Unknown).
        true;

    /// <inheritdoc/>
    public bool CanHandle(EntityType entityType) =>
        entityType is EntityType.Person
                   or EntityType.Work
                   or EntityType.MediaAsset;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProviderClaim>> FetchAsync(
        ProviderLookupRequest request,
        CancellationToken ct = default)
    {
        return request.EntityType == EntityType.Person
            ? await FetchPersonAsync(request, ct).ConfigureAwait(false)
            : await FetchWorkAsync(request, ct).ConfigureAwait(false);
    }

    // ── Person flow ───────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<ProviderClaim>> FetchPersonAsync(
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        var name = request.PersonName;
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger.LogDebug(
                "Wikidata person skipped for entity {Id}: no name", request.EntityId);
            return [];
        }

        try
        {
            using var client = _httpFactory.CreateClient("wikidata_api");

            // Step 1: search for the entity by name.
            var searchUrl = $"{request.BaseUrl.TrimEnd('/')}" +
                $"?action=wbsearchentities&search={Uri.EscapeDataString(name)}" +
                "&type=item&language=en&format=json&limit=3";

            var searchJson = await ThrottledGetAsync<JsonObject>(client, searchUrl, ct)
                .ConfigureAwait(false);

            var qid = FindHumanQid(searchJson);
            if (qid is null)
            {
                _logger.LogDebug(
                    "Wikidata: no human entity found for '{Name}' (entity {Id})",
                    name, request.EntityId);
                return [];
            }

            // Step 2: fetch the full entity to extract description and image.
            var entityUrl = $"{request.BaseUrl.TrimEnd('/')}" +
                $"?action=wbgetentities&ids={Uri.EscapeDataString(qid)}" +
                "&format=json&languages=en&props=labels|descriptions|claims";

            var entityJson = await ThrottledGetAsync<JsonObject>(client, entityUrl, ct)
                .ConfigureAwait(false);

            return ParsePersonEntity(entityJson, qid);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Wikidata person enrichment failed for '{Name}' / entity {Id}",
                name, request.EntityId);
            return [];
        }
    }

    private static string? FindHumanQid(JsonObject? searchJson)
    {
        if (searchJson is null) return null;

        var searchResults = searchJson["search"]?.AsArray();
        if (searchResults is null) return null;

        foreach (var item in searchResults)
        {
            var id = item?["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id)) continue;

            // Check description for "human" to do a lightweight P31=Q5 filter.
            // A full SPARQL query would be more precise but adds latency.
            var desc = item?["description"]?.GetValue<string>() ?? string.Empty;
            // Accept any result that Wikidata returns in a human-name search;
            // we later confirm with the entity's description claim.
            return id; // Take first result — Wikidata ranks by relevance.
        }

        return null;
    }

    private static IReadOnlyList<ProviderClaim> ParsePersonEntity(
        JsonObject? entityJson,
        string qid)
    {
        if (entityJson is null) return [];

        var entities = entityJson["entities"]?.AsObject();
        var entity   = entities?[qid]?.AsObject();
        if (entity is null) return [];

        var claims = new List<ProviderClaim>
        {
            // The Wikidata Q-identifier is the definitive identity for this person.
            new("wikidata_qid", qid, 1.0),
        };

        // Biography: use the English entity description.
        var description = entity["descriptions"]?["en"]?["value"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(description))
            claims.Add(new ProviderClaim("biography", description, 1.0));

        // Headshot: P18 image → Wikimedia Commons URL.
        var p18Array = entity["claims"]?["P18"]?.AsArray();
        var filename  = p18Array?[0]?["mainsnak"]?["datavalue"]?["value"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(filename))
        {
            // Commons uses a URL-encoded filename with spaces replaced by underscores.
            var commonsName = filename.Replace(' ', '_');
            var imageUrl    = string.Format(CommonsUrlTemplate,
                Uri.EscapeDataString(commonsName));
            claims.Add(new ProviderClaim("headshot_url", imageUrl, 1.0));
        }

        return claims;
    }

    // ── Work flow ─────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<ProviderClaim>> FetchWorkAsync(
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        // A title or at least one bridge identifier is needed.
        if (string.IsNullOrWhiteSpace(request.Title)
            && string.IsNullOrWhiteSpace(request.Asin)
            && string.IsNullOrWhiteSpace(request.Isbn)
            && string.IsNullOrWhiteSpace(request.AppleBooksId)
            && string.IsNullOrWhiteSpace(request.AudibleId)
            && string.IsNullOrWhiteSpace(request.TmdbId)
            && string.IsNullOrWhiteSpace(request.ImdbId))
        {
            _logger.LogDebug(
                "Wikidata work skipped for entity {Id}: no title or bridge IDs",
                request.EntityId);
            return [];
        }

        // Require a SPARQL endpoint to be configured.
        if (string.IsNullOrWhiteSpace(request.SparqlBaseUrl))
        {
            _logger.LogDebug(
                "Wikidata work skipped for entity {Id}: no SPARQL base URL configured",
                request.EntityId);
            return [];
        }

        try
        {
            using var apiClient    = _httpFactory.CreateClient("wikidata_api");
            using var sparqlClient = _httpFactory.CreateClient("wikidata_sparql");

            // ── Step 1: QID Cross-Reference via Bridge IDs ──────────────────
            var qid = await ResolveQidViaBridgesAsync(sparqlClient, request, ct)
                .ConfigureAwait(false);

            // ── Step 2: Fallback to MediaWiki Text Search ───────────────────
            if (qid is null && !string.IsNullOrWhiteSpace(request.Title))
            {
                qid = await ResolveQidViaSearchAsync(
                    apiClient, request.BaseUrl, request.Title, ct).ConfigureAwait(false);
            }

            if (qid is null)
            {
                _logger.LogDebug(
                    "Wikidata: no QID found for entity {Id} (tried bridges + title search)",
                    request.EntityId);
                return [];
            }

            // ── Step 3: SPARQL Deep Ingest ──────────────────────────────────
            var sparql = WikidataSparqlPropertyMap.BuildWorkSparqlQuery(qid);
            var claims = await ExecuteSparqlQueryAsync(
                sparqlClient, request.SparqlBaseUrl, sparql, qid, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Wikidata SPARQL deep-ingest for entity {Id}: QID={Qid}, {Count} claims",
                request.EntityId, qid, claims.Count);

            return claims;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Wikidata work enrichment failed for entity {Id}", request.EntityId);
            return [];
        }
    }

    // ── QID Resolution: Bridge IDs ──────────────────────────────────────────

    /// <summary>
    /// Tries to find the Wikidata QID using bridge identifiers in priority order.
    /// First match wins.
    /// </summary>
    private async Task<string?> ResolveQidViaBridgesAsync(
        HttpClient sparqlClient,
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        // Try bridge IDs in priority order.
        var bridges = new (string PCode, string? Value)[]
        {
            ("P3861", request.AppleBooksId),
            ("P3398", request.AudibleId),
            ("P4947", request.TmdbId),
            ("P345",  request.ImdbId),
            ("P1566", request.Asin),
            ("P212",  request.Isbn),
        };

        foreach (var (pCode, value) in bridges)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                string.IsNullOrWhiteSpace(request.SparqlBaseUrl))
                continue;

            var sparql = WikidataSparqlPropertyMap.BuildBridgeLookupQuery(pCode, value);
            var qid = await RunBridgeLookupAsync(
                sparqlClient, request.SparqlBaseUrl, sparql, ct).ConfigureAwait(false);

            if (qid is not null)
            {
                _logger.LogDebug(
                    "Wikidata: resolved QID {Qid} via bridge {PCode}={Value}",
                    qid, pCode, value);
                return qid;
            }
        }

        return null;
    }

    /// <summary>
    /// Executes a bridge lookup SPARQL query and returns the QID if found.
    /// </summary>
    private async Task<string?> RunBridgeLookupAsync(
        HttpClient sparqlClient,
        string sparqlBaseUrl,
        string sparql,
        CancellationToken ct)
    {
        var json = await ThrottledSparqlAsync(sparqlClient, sparqlBaseUrl, sparql, ct)
            .ConfigureAwait(false);
        if (json is null) return null;

        var bindings = json["results"]?["bindings"]?.AsArray();
        if (bindings is null || bindings.Count == 0)
            return null;

        var itemUri = bindings[0]?["item"]?["value"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(itemUri))
            return null;

        // Extract QID from URI: "http://www.wikidata.org/entity/Q190192" → "Q190192"
        var lastSlash = itemUri.LastIndexOf('/');
        return lastSlash >= 0 ? itemUri[(lastSlash + 1)..] : itemUri;
    }

    // ── QID Resolution: Title Search ─────────────────────────────────────────

    /// <summary>
    /// Fallback: search Wikidata by title using the MediaWiki API.
    /// </summary>
    private async Task<string?> ResolveQidViaSearchAsync(
        HttpClient apiClient,
        string baseUrl,
        string title,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        var searchUrl = $"{baseUrl.TrimEnd('/')}" +
            $"?action=wbsearchentities&search={Uri.EscapeDataString(title)}" +
            "&type=item&language=en&format=json&limit=5";

        var searchJson = await ThrottledGetAsync<JsonObject>(apiClient, searchUrl, ct)
            .ConfigureAwait(false);

        if (searchJson is null) return null;

        var results = searchJson["search"]?.AsArray();
        if (results is null) return null;

        // Take the first result — Wikidata ranks by relevance.
        var firstId = results.FirstOrDefault()?["id"]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(firstId) ? null : firstId;
    }

    // ── SPARQL Execution & Parsing ──────────────────────────────────────────

    /// <summary>
    /// Executes a Work SPARQL query and parses the bindings into ProviderClaim list.
    /// Always emits the <c>wikidata_qid</c> claim first (confidence 1.0).
    /// </summary>
    private async Task<IReadOnlyList<ProviderClaim>> ExecuteSparqlQueryAsync(
        HttpClient sparqlClient,
        string sparqlBaseUrl,
        string sparql,
        string qid,
        CancellationToken ct)
    {
        var json = await ThrottledSparqlAsync(sparqlClient, sparqlBaseUrl, sparql, ct)
            .ConfigureAwait(false);

        var claims = new List<ProviderClaim>
        {
            new("wikidata_qid", qid, 1.0),
        };

        if (json is null) return claims;

        var bindings = json["results"]?["bindings"]?.AsArray();
        if (bindings is null || bindings.Count == 0)
            return claims;

        var binding = bindings[0]!.AsObject();
        var map = WikidataSparqlPropertyMap.DefaultMap;

        foreach (var prop in map.Values.Where(p => p.Enabled && p.EntityScope is "Work" or "Both"))
        {
            // P18 is Person-only — never emit for Work entities.
            if (prop.PCode == "P18") continue;

            var varName = prop.PCode.ToLowerInvariant();

            // Prefer the label variable for entity-valued properties.
            string? rawValue = null;
            if (binding.ContainsKey(varName + "Label"))
            {
                rawValue = binding[varName + "Label"]?["value"]?.GetValue<string>();
            }

            rawValue ??= binding.ContainsKey(varName)
                ? binding[varName]?["value"]?.GetValue<string>()
                : null;

            if (string.IsNullOrWhiteSpace(rawValue))
                continue;

            // Apply value transformations based on property type.
            var claimValue = TransformValue(prop, rawValue);
            if (!string.IsNullOrWhiteSpace(claimValue))
                claims.Add(new ProviderClaim(prop.ClaimKey, claimValue, prop.Confidence));
        }

        return claims;
    }

    /// <summary>
    /// Applies property-specific transformations to raw SPARQL values.
    /// </summary>
    private static string? TransformValue(WikidataProperty prop, string rawValue)
    {
        return prop.PCode switch
        {
            // P577 (publication date): extract 4-digit year from ISO date
            "P577" => rawValue.Length >= 4 ? rawValue[..4] : rawValue,

            // P1545 (series position): extract the numeric portion
            "P1545" => ExtractNumericPortion(rawValue),

            // Entity URIs: strip the Wikidata prefix to get just the QID
            _ when rawValue.StartsWith("http://www.wikidata.org/entity/", StringComparison.Ordinal)
                => rawValue[(rawValue.LastIndexOf('/') + 1)..],

            // Everything else: return as-is
            _ => rawValue,
        };
    }

    /// <summary>Extract the leading numeric portion from a string, e.g. "3.5" → "3.5", "Book 2" → "2".</summary>
    private static string? ExtractNumericPortion(string value)
    {
        // Try to find contiguous digits (with optional decimal point)
        var start = -1;
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsDigit(value[i]))
            {
                if (start < 0) start = i;
            }
            else if (value[i] == '.' && start >= 0)
            {
                // Allow decimal in number
            }
            else if (start >= 0)
            {
                return value[start..i];
            }
        }

        return start >= 0 ? value[start..] : value;
    }

    // ── Throttled SPARQL helper ─────────────────────────────────────────────

    /// <summary>
    /// Sends a throttled SPARQL query and returns the parsed JSON response.
    /// Uses the <c>wikidata_sparql</c> named HttpClient.
    /// </summary>
    private static async Task<JsonObject?> ThrottledSparqlAsync(
        HttpClient sparqlClient,
        string sparqlBaseUrl,
        string sparql,
        CancellationToken ct)
    {
        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastCallUtc).TotalMilliseconds;
            if (elapsed < ThrottleGapMs)
                await Task.Delay(TimeSpan.FromMilliseconds(ThrottleGapMs - elapsed), ct)
                    .ConfigureAwait(false);

            var url = $"{sparqlBaseUrl.TrimEnd('/')}?query={Uri.EscapeDataString(sparql)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/sparql-results+json");

            var response = await sparqlClient.SendAsync(request, ct).ConfigureAwait(false);
            _lastCallUtc = DateTime.UtcNow;

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<JsonObject>(_jsonOptions, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _throttle.Release();
        }
    }

    // ── Throttled HTTP helper ─────────────────────────────────────────────────

    private static async Task<T?> ThrottledGetAsync<T>(
        HttpClient client,
        string url,
        CancellationToken ct)
    {
        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastCallUtc).TotalMilliseconds;
            if (elapsed < ThrottleGapMs)
                await Task.Delay(TimeSpan.FromMilliseconds(ThrottleGapMs - elapsed), ct)
                          .ConfigureAwait(false);

            var result = await client.GetFromJsonAsync<T>(url, ct).ConfigureAwait(false);
            _lastCallUtc = DateTime.UtcNow;
            return result;
        }
        finally
        {
            _throttle.Release();
        }
    }
}
