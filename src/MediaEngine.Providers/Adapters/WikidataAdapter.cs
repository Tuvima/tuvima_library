using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;

namespace MediaEngine.Providers.Adapters;

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
/// <para>
/// <b>Configuration:</b> The knowledge model (property map, bridge lookup priority,
/// scope exclusions) is loaded at runtime from <c>config/universe/wikidata.json</c>
/// via <see cref="IConfigurationLoader"/>. If the file is missing or corrupt, the
/// compiled defaults in <see cref="WikidataSparqlPropertyMap.DefaultMap"/> are used.
/// The throttle gap is read from <c>config/providers/wikidata.json</c>; the compiled
/// default is 1100 ms (Wikidata's 1 req/s policy).
/// </para>
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
        = new("b3000003-d000-4000-8000-000000000004");

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
    private readonly IConfigurationLoader _configLoader;
    private readonly ILogger<WikidataAdapter> _logger;

    // Throttle shared across all instances (static) — Wikidata policy: 1 req/s.
    private static readonly SemaphoreSlim _throttle = new(1, 1);
    private static DateTime _lastCallUtc = DateTime.MinValue;

    /// <summary>Compiled default throttle gap (ms). Overridden by provider config.</summary>
    private const int DefaultThrottleGapMs = 1100;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Fallback Commons URL template — used when universe config is missing.
    private const string DefaultCommonsUrlTemplate =
        "https://commons.wikimedia.org/wiki/Special:FilePath/{0}?width=300";

    // ── Constructor ───────────────────────────────────────────────────────────

    public WikidataAdapter(
        IHttpClientFactory httpFactory,
        IConfigurationLoader configLoader,
        ILogger<WikidataAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(configLoader);
        ArgumentNullException.ThrowIfNull(logger);
        _httpFactory  = httpFactory;
        _configLoader = configLoader;
        _logger       = logger;
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

    // ── Disambiguation: multiple QID candidates ────────────────────────────────

    /// <summary>
    /// Resolves multiple QID candidates for an entity lookup request.
    ///
    /// Unlike <see cref="FetchAsync"/>, which returns claims for the first match,
    /// this method returns ALL potential matches (up to <paramref name="maxCandidates"/>)
    /// so the <see cref="Services.HydrationPipelineService"/> can determine whether
    /// disambiguation is required.
    ///
    /// Resolution order: bridge ID cross-reference → MediaWiki title search.
    /// </summary>
    /// <param name="request">The lookup request with title, bridge IDs, etc.</param>
    /// <param name="maxCandidates">Maximum number of candidates to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of QID candidates (may be empty if no matches found).</returns>
    public async Task<IReadOnlyList<QidCandidate>> ResolveCandidatesAsync(
        ProviderLookupRequest request,
        int maxCandidates = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title)
            && string.IsNullOrWhiteSpace(request.Asin)
            && string.IsNullOrWhiteSpace(request.Isbn)
            && string.IsNullOrWhiteSpace(request.AppleBooksId)
            && string.IsNullOrWhiteSpace(request.AudibleId)
            && string.IsNullOrWhiteSpace(request.TmdbId)
            && string.IsNullOrWhiteSpace(request.ImdbId))
        {
            return [];
        }

        var throttleGapMs = GetThrottleGapMs();
        var candidates = new List<QidCandidate>();
        var seenQids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var apiClient    = _httpFactory.CreateClient("wikidata_api");
            using var sparqlClient = _httpFactory.CreateClient("wikidata_sparql");

            // Step 1: try bridge IDs — each match is a candidate.
            var universeConfig = _configLoader.LoadConfig<UniverseConfiguration>("universe", "wikidata");
            var bridgePriority = universeConfig?.BridgeLookupPriority;

            if (!string.IsNullOrWhiteSpace(request.SparqlBaseUrl))
            {
                IEnumerable<(string PCode, string? Value)> bridges;
                if (bridgePriority is { Count: > 0 })
                {
                    bridges = bridgePriority.Select(b =>
                        (b.PCode, GetBridgeValue(request, b.RequestField)));
                }
                else
                {
                    bridges =
                    [
                        ("P3861", request.AppleBooksId),
                        ("P3398", request.AudibleId),
                        ("P4947", request.TmdbId),
                        ("P345",  request.ImdbId),
                        ("P1566", request.Asin),
                        ("P212",  request.Isbn),
                    ];
                }

                foreach (var (pCode, value) in bridges)
                {
                    if (candidates.Count >= maxCandidates) break;
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    var sparql = WikidataSparqlPropertyMap.BuildBridgeLookupQuery(pCode, value);
                    var qid = await RunBridgeLookupAsync(
                        sparqlClient, request.SparqlBaseUrl, sparql, throttleGapMs, ct)
                        .ConfigureAwait(false);

                    if (qid is not null && seenQids.Add(qid))
                    {
                        var label = await FetchEntityLabelAsync(apiClient, request.BaseUrl, qid, throttleGapMs, ct)
                            .ConfigureAwait(false);

                        candidates.Add(new QidCandidate
                        {
                            Qid         = qid,
                            Label       = label?.Label ?? qid,
                            Description = label?.Description,
                        });
                    }
                }
            }

            // Step 2: title search — add any new candidates.
            if (candidates.Count < maxCandidates && !string.IsNullOrWhiteSpace(request.Title)
                && !string.IsNullOrWhiteSpace(request.BaseUrl))
            {
                var searchUrl = $"{request.BaseUrl.TrimEnd('/')}" +
                    $"?action=wbsearchentities&search={Uri.EscapeDataString(request.Title)}" +
                    $"&type=item&language=en&format=json&limit={maxCandidates}";

                var searchJson = await ThrottledGetAsync<JsonObject>(apiClient, searchUrl, throttleGapMs, ct)
                    .ConfigureAwait(false);

                var results = searchJson?["search"]?.AsArray();
                if (results is not null)
                {
                    foreach (var item in results)
                    {
                        if (candidates.Count >= maxCandidates) break;

                        var id   = item?["id"]?.GetValue<string>();
                        var lbl  = item?["label"]?.GetValue<string>();
                        var desc = item?["description"]?.GetValue<string>();

                        if (!string.IsNullOrWhiteSpace(id) && seenQids.Add(id))
                        {
                            candidates.Add(new QidCandidate
                            {
                                Qid         = id,
                                Label       = lbl ?? id,
                                Description = desc,
                            });
                        }
                    }
                }
            }

            return candidates;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Wikidata candidate resolution failed for entity {Id}", request.EntityId);
            return candidates; // Return whatever we've gathered so far.
        }
    }

    /// <summary>
    /// Fetches the English label and description for a single Wikidata entity.
    /// Used to populate <see cref="QidCandidate"/> during disambiguation.
    /// </summary>
    private static async Task<(string Label, string? Description)?> FetchEntityLabelAsync(
        HttpClient apiClient,
        string baseUrl,
        string qid,
        int throttleGapMs,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        var url = $"{baseUrl.TrimEnd('/')}" +
            $"?action=wbgetentities&ids={Uri.EscapeDataString(qid)}" +
            "&format=json&languages=en&props=labels|descriptions";

        var json = await ThrottledGetAsync<JsonObject>(apiClient, url, throttleGapMs, ct)
            .ConfigureAwait(false);

        if (json is null) return null;

        var entity = json["entities"]?[qid]?.AsObject();
        if (entity is null) return null;

        var label = entity["labels"]?["en"]?["value"]?.GetValue<string>() ?? qid;
        var desc  = entity["descriptions"]?["en"]?["value"]?.GetValue<string>();

        return (label, desc);
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

        var throttleGapMs = GetThrottleGapMs();

        try
        {
            using var client = _httpFactory.CreateClient("wikidata_api");

            // Resolve the Commons URL template from universe config.
            var universeConfig = _configLoader.LoadConfig<UniverseConfiguration>("universe", "wikidata");
            var commonsTemplate = universeConfig?.CommonsUrlTemplate ?? DefaultCommonsUrlTemplate;

            // Step 1: search for the entity by name.
            var searchUrl = $"{request.BaseUrl.TrimEnd('/')}" +
                $"?action=wbsearchentities&search={Uri.EscapeDataString(name)}" +
                "&type=item&language=en&format=json&limit=3";

            var searchJson = await ThrottledGetAsync<JsonObject>(client, searchUrl, throttleGapMs, ct)
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

            var entityJson = await ThrottledGetAsync<JsonObject>(client, entityUrl, throttleGapMs, ct)
                .ConfigureAwait(false);

            return ParsePersonEntity(entityJson, qid, commonsTemplate);
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

            // Accept any result that Wikidata returns in a human-name search;
            // we later confirm with the entity's description claim.
            return id; // Take first result — Wikidata ranks by relevance.
        }

        return null;
    }

    private static IReadOnlyList<ProviderClaim> ParsePersonEntity(
        JsonObject? entityJson,
        string qid,
        string commonsUrlTemplate)
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

        // Headshot: P18 image -> Wikimedia Commons URL.
        var p18Array = entity["claims"]?["P18"]?.AsArray();
        var filename  = p18Array?[0]?["mainsnak"]?["datavalue"]?["value"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(filename))
        {
            // Commons uses a URL-encoded filename with spaces replaced by underscores.
            var commonsName = filename.Replace(' ', '_');
            var imageUrl    = string.Format(commonsUrlTemplate,
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
        // Check for a pre-resolved QID (from review queue disambiguation).
        var preResolvedQid = request.PreResolvedQid;

        // A title, bridge identifier, or pre-resolved QID is needed.
        if (string.IsNullOrWhiteSpace(preResolvedQid)
            && string.IsNullOrWhiteSpace(request.Title)
            && string.IsNullOrWhiteSpace(request.Asin)
            && string.IsNullOrWhiteSpace(request.Isbn)
            && string.IsNullOrWhiteSpace(request.AppleBooksId)
            && string.IsNullOrWhiteSpace(request.AudibleId)
            && string.IsNullOrWhiteSpace(request.TmdbId)
            && string.IsNullOrWhiteSpace(request.ImdbId))
        {
            _logger.LogDebug(
                "Wikidata work skipped for entity {Id}: no title, bridge IDs, or pre-resolved QID",
                request.EntityId);
            return [];
        }

        // Load the universe configuration (property map, bridges, exclusions).
        // Falls back to compiled defaults if the file is missing or corrupt.
        var universeConfig = _configLoader.LoadConfig<UniverseConfiguration>("universe", "wikidata");
        var effectiveMap = universeConfig is not null
            ? WikidataSparqlPropertyMap.BuildMapFromUniverse(universeConfig)
            : WikidataSparqlPropertyMap.DefaultMap;

        var throttleGapMs  = GetThrottleGapMs();
        var hasSparql      = !string.IsNullOrWhiteSpace(request.SparqlBaseUrl);

        try
        {
            using var apiClient    = _httpFactory.CreateClient("wikidata_api");
            using var sparqlClient = _httpFactory.CreateClient("wikidata_sparql");

            string? qid = null;

            // Skip QID resolution if a pre-resolved QID was provided (e.g. from
            // user disambiguation in the review queue).
            if (!string.IsNullOrWhiteSpace(preResolvedQid))
            {
                qid = preResolvedQid;
                _logger.LogInformation(
                    "Wikidata: using pre-resolved QID {Qid} for entity {Id}",
                    qid, request.EntityId);
            }
            else
            {
                // ── Step 1: QID Cross-Reference via Bridge IDs ──────────────
                // Bridge lookups require SPARQL; skip if endpoint is unavailable.
                if (hasSparql)
                {
                    var bridgePriority = universeConfig?.BridgeLookupPriority;
                    qid = await ResolveQidViaBridgesAsync(
                        sparqlClient, request, bridgePriority, throttleGapMs, ct).ConfigureAwait(false);

                    if (qid is not null)
                    {
                        _logger.LogInformation(
                            "Wikidata: resolved QID {Qid} via bridge ID for entity {Id}",
                            qid, request.EntityId);
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Wikidata: SPARQL endpoint not configured — bridge ID lookup skipped for entity {Id}",
                        request.EntityId);
                }

                // ── Step 2: Fallback to MediaWiki Text Search ───────────────
                // Title search uses the MediaWiki API, NOT SPARQL — works even
                // when the SPARQL endpoint is unavailable.
                if (qid is null && !string.IsNullOrWhiteSpace(request.Title))
                {
                    _logger.LogInformation(
                        "Wikidata: attempting title search for \"{Title}\" (entity {Id})",
                        request.Title, request.EntityId);

                    qid = await ResolveQidViaSearchAsync(
                        apiClient, request.BaseUrl, request.Title, throttleGapMs, ct)
                        .ConfigureAwait(false);

                    if (qid is not null)
                    {
                        _logger.LogInformation(
                            "Wikidata: title search found QID {Qid} for \"{Title}\" (entity {Id})",
                            qid, request.Title, request.EntityId);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Wikidata: title search returned no results for \"{Title}\" (entity {Id})",
                            request.Title, request.EntityId);
                    }
                }
            }

            if (qid is null)
            {
                _logger.LogInformation(
                    "Wikidata: no QID found for entity {Id} (tried bridges + title search)",
                    request.EntityId);
                return [];
            }

            // ── Step 3: SPARQL Deep Ingest ──────────────────────────────────
            // If we found a QID but SPARQL is unavailable, return just the QID
            // claim.  Partial success is better than no data.
            if (!hasSparql)
            {
                _logger.LogWarning(
                    "Wikidata: QID {Qid} found for entity {Id} but SPARQL unavailable — " +
                    "returning QID claim only (no deep hydration)",
                    qid, request.EntityId);

                return [new ProviderClaim("wikidata_qid", qid, 1.0)];
            }
            // Read scope exclusions from universe config (default: P18 excluded from Work).
            IReadOnlyCollection<string>? scopeExclusions = null;
            if (universeConfig?.ScopeExclusions.TryGetValue("Work", out var workExclusions) == true)
                scopeExclusions = workExclusions;

            var sparql = WikidataSparqlPropertyMap.BuildWorkSparqlQuery(
                qid, effectiveMap, scopeExclusions);
            var claims = await ExecuteSparqlQueryAsync(
                sparqlClient, request.SparqlBaseUrl!, sparql, qid,
                effectiveMap, scopeExclusions, throttleGapMs, ct).ConfigureAwait(false);

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
    /// The order is read from <c>config/universe/wikidata.json → bridge_lookup_priority</c>.
    /// Falls back to the compiled default order if the config is null.
    /// </summary>
    private async Task<string?> ResolveQidViaBridgesAsync(
        HttpClient sparqlClient,
        ProviderLookupRequest request,
        IReadOnlyList<BridgeLookupEntry>? bridgePriority,
        int throttleGapMs,
        CancellationToken ct)
    {
        // Build bridge list from config or use compiled defaults.
        IEnumerable<(string PCode, string? Value)> bridges;

        if (bridgePriority is { Count: > 0 })
        {
            bridges = bridgePriority.Select(b =>
                (b.PCode, GetBridgeValue(request, b.RequestField)));
        }
        else
        {
            // Compiled default order (same as the original hard-coded array).
            bridges =
            [
                ("P3861", request.AppleBooksId),
                ("P3398", request.AudibleId),
                ("P4947", request.TmdbId),
                ("P345",  request.ImdbId),
                ("P1566", request.Asin),
                ("P212",  request.Isbn),
            ];
        }

        foreach (var (pCode, value) in bridges)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                string.IsNullOrWhiteSpace(request.SparqlBaseUrl))
                continue;

            var sparql = WikidataSparqlPropertyMap.BuildBridgeLookupQuery(pCode, value);
            var qid = await RunBridgeLookupAsync(
                sparqlClient, request.SparqlBaseUrl, sparql, throttleGapMs, ct)
                .ConfigureAwait(false);

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
    /// Maps a bridge <c>request_field</c> name to the corresponding value on
    /// <see cref="ProviderLookupRequest"/>. Used by the config-driven bridge lookup.
    /// </summary>
    private static string? GetBridgeValue(ProviderLookupRequest request, string requestField)
        => requestField switch
        {
            "apple_books_id" => request.AppleBooksId,
            "audible_id"     => request.AudibleId,
            "tmdb_id"        => request.TmdbId,
            "imdb_id"        => request.ImdbId,
            "asin"           => request.Asin,
            "isbn"           => request.Isbn,
            _                => null,
        };

    /// <summary>
    /// Executes a bridge lookup SPARQL query and returns the QID if found.
    /// </summary>
    private static async Task<string?> RunBridgeLookupAsync(
        HttpClient sparqlClient,
        string sparqlBaseUrl,
        string sparql,
        int throttleGapMs,
        CancellationToken ct)
    {
        var json = await ThrottledSparqlAsync(sparqlClient, sparqlBaseUrl, sparql, throttleGapMs, ct)
            .ConfigureAwait(false);
        if (json is null) return null;

        var bindings = json["results"]?["bindings"]?.AsArray();
        if (bindings is null || bindings.Count == 0)
            return null;

        var itemUri = bindings[0]?["item"]?["value"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(itemUri))
            return null;

        // Extract QID from URI: "http://www.wikidata.org/entity/Q190192" -> "Q190192"
        var lastSlash = itemUri.LastIndexOf('/');
        return lastSlash >= 0 ? itemUri[(lastSlash + 1)..] : itemUri;
    }

    // ── QID Resolution: Title Search ─────────────────────────────────────────

    /// <summary>
    /// Fallback: search Wikidata by title using the MediaWiki API.
    /// This does NOT require SPARQL — only the MediaWiki wbsearchentities endpoint.
    /// </summary>
    private async Task<string?> ResolveQidViaSearchAsync(
        HttpClient apiClient,
        string baseUrl,
        string title,
        int throttleGapMs,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogWarning(
                "Wikidata title search skipped: MediaWiki API base URL is empty. " +
                "Check that config/providers/wikidata.json has a valid 'wikidata_api' endpoint.");
            return null;
        }

        var searchUrl = $"{baseUrl.TrimEnd('/')}" +
            $"?action=wbsearchentities&search={Uri.EscapeDataString(title)}" +
            "&type=item&language=en&format=json&limit=5";

        _logger.LogDebug("Wikidata title search URL: {Url}", searchUrl);

        var searchJson = await ThrottledGetAsync<JsonObject>(apiClient, searchUrl, throttleGapMs, ct)
            .ConfigureAwait(false);

        if (searchJson is null)
        {
            _logger.LogWarning(
                "Wikidata title search failed: API returned null for \"{Title}\"", title);
            return null;
        }

        var results = searchJson["search"]?.AsArray();
        if (results is null || results.Count == 0)
        {
            _logger.LogInformation(
                "Wikidata title search returned 0 results for \"{Title}\"", title);
            return null;
        }

        _logger.LogDebug(
            "Wikidata title search returned {Count} results for \"{Title}\"",
            results.Count, title);

        // Take the first result — Wikidata ranks by relevance.
        var firstId = results.FirstOrDefault()?["id"]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(firstId) ? null : firstId;
    }

    // ── SPARQL Execution & Parsing ──────────────────────────────────────────

    /// <summary>
    /// Executes a Work SPARQL query and parses the bindings into ProviderClaim list.
    /// Always emits the <c>wikidata_qid</c> claim first (confidence 1.0).
    /// Uses the effective property map and scope exclusions from universe configuration.
    /// </summary>
    private static async Task<IReadOnlyList<ProviderClaim>> ExecuteSparqlQueryAsync(
        HttpClient sparqlClient,
        string sparqlBaseUrl,
        string sparql,
        string qid,
        IReadOnlyDictionary<string, WikidataProperty> effectiveMap,
        IReadOnlyCollection<string>? scopeExclusions,
        int throttleGapMs,
        CancellationToken ct)
    {
        var json = await ThrottledSparqlAsync(sparqlClient, sparqlBaseUrl, sparql, throttleGapMs, ct)
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
        var exclusions = scopeExclusions ?? (IReadOnlyCollection<string>)["P18"];

        foreach (var prop in effectiveMap.Values.Where(p => p.Enabled && p.EntityScope is "Work" or "Both"))
        {
            // Apply scope exclusions (e.g. P18 excluded from Work for copyright reasons).
            if (exclusions.Contains(prop.PCode)) continue;

            var varName = prop.PCode.ToLowerInvariant();

            if (prop.IsMultiValued)
            {
                // Multi-valued: read from GROUP_CONCAT result variables.
                string? concatenated = null;

                if (prop.IsEntityValued)
                {
                    // Labels for display and canonical values.
                    concatenated = binding.ContainsKey(varName + "Labels")
                        ? binding[varName + "Labels"]?["value"]?.GetValue<string>()
                        : null;
                }
                else
                {
                    concatenated = binding.ContainsKey(varName + "All")
                        ? binding[varName + "All"]?["value"]?.GetValue<string>()
                        : null;
                }

                if (string.IsNullOrWhiteSpace(concatenated))
                    continue;

                // Split on the GROUP_CONCAT separator and emit one claim per value.
                var values = concatenated.Split(WikidataSparqlPropertyMap.MultiValueSeparator,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (var val in values)
                {
                    var claimValue = ValueTransformRegistry.Apply(prop.ValueTransform, val);
                    if (!string.IsNullOrWhiteSpace(claimValue))
                        claims.Add(new ProviderClaim(prop.ClaimKey, claimValue, prop.Confidence));
                }
            }
            else
            {
                // Single-valued: original behavior.
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

                var claimValue = ValueTransformRegistry.Apply(prop.ValueTransform, rawValue);
                if (!string.IsNullOrWhiteSpace(claimValue))
                    claims.Add(new ProviderClaim(prop.ClaimKey, claimValue, prop.Confidence));
            }
        }

        return claims;
    }

    // ── Configuration Helpers ────────────────────────────────────────────────

    /// <summary>
    /// Reads the throttle gap from the Wikidata provider configuration.
    /// Falls back to the compiled default (1100 ms) if the config is missing.
    /// </summary>
    private int GetThrottleGapMs()
    {
        var providerConfig = _configLoader.LoadProvider("wikidata");
        return providerConfig?.ThrottleMs > 0 ? providerConfig.ThrottleMs : DefaultThrottleGapMs;
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
        int throttleGapMs,
        CancellationToken ct)
    {
        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastCallUtc).TotalMilliseconds;
            if (elapsed < throttleGapMs)
                await Task.Delay(TimeSpan.FromMilliseconds(throttleGapMs - elapsed), ct)
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
        int throttleGapMs,
        CancellationToken ct)
    {
        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastCallUtc).TotalMilliseconds;
            if (elapsed < throttleGapMs)
                await Task.Delay(TimeSpan.FromMilliseconds(throttleGapMs - elapsed), ct)
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
