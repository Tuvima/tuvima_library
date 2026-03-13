using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
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
    private readonly IQidLabelRepository _qidLabelRepo;
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
        IQidLabelRepository qidLabelRepo,
        ILogger<WikidataAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(configLoader);
        ArgumentNullException.ThrowIfNull(qidLabelRepo);
        ArgumentNullException.ThrowIfNull(logger);
        _httpFactory   = httpFactory;
        _configLoader  = configLoader;
        _qidLabelRepo  = qidLabelRepo;
        _logger        = logger;
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
                   or EntityType.MediaAsset
                   or EntityType.Character
                   or EntityType.Location
                   or EntityType.Organization;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProviderClaim>> FetchAsync(
        ProviderLookupRequest request,
        CancellationToken ct = default)
    {
        return request.EntityType switch
        {
            EntityType.Person => await FetchPersonAsync(request, ct).ConfigureAwait(false),
            EntityType.Character or EntityType.Location or EntityType.Organization
                => await FetchFictionalEntityAsync(request, ct).ConfigureAwait(false),
            _ => await FetchWorkAsync(request, ct).ConfigureAwait(false),
        };
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
                    // Compiled default order — ISBN first (definitive match).
                    bridges =
                    [
                        ("P212",  request.Isbn),
                        ("P3861", request.AppleBooksId),
                        ("P3398", request.AudibleId),
                        ("P4947", request.TmdbId),
                        ("P345",  request.ImdbId),
                        ("P1566", request.Asin),
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
                        var label = await FetchEntityLabelAsync(apiClient, request.BaseUrl, qid, throttleGapMs, ct, request.Language)
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
                    $"&type=item&language={Uri.EscapeDataString(request.Language)}&format=json&limit={maxCandidates}";

                var searchJson = await ThrottledGetAsync<JsonObject>(apiClient, searchUrl, throttleGapMs, ct)
                    .ConfigureAwait(false);

                var results = searchJson?["search"]?.AsArray();
                if (results is not null)
                {
                    // Sort label matches before alias matches so that the primary-
                    // language item appears first in the disambiguation list shown
                    // to the user.
                    var sorted = results
                        .Select((item, i) => (item, i))
                        .OrderBy(t => string.Equals(
                            t.item?["match"]?["type"]?.GetValue<string>(),
                            "label",
                            StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ThenBy(t => t.i)
                        .Select(t => t.item);

                    foreach (var item in sorted)
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
    /// Fetches the label and description for a single Wikidata entity in the
    /// preferred language (with English fallback).
    /// Used to populate <see cref="QidCandidate"/> during disambiguation.
    /// </summary>
    private static async Task<(string Label, string? Description)?> FetchEntityLabelAsync(
        HttpClient apiClient,
        string baseUrl,
        string qid,
        int throttleGapMs,
        CancellationToken ct,
        string language = "en")
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        // Request labels in the preferred language with English as fallback.
        var langs = language == "en" ? "en" : $"{language}|en";
        var url = $"{baseUrl.TrimEnd('/')}" +
            $"?action=wbgetentities&ids={Uri.EscapeDataString(qid)}" +
            $"&format=json&languages={Uri.EscapeDataString(langs)}&props=labels|descriptions";

        var json = await ThrottledGetAsync<JsonObject>(apiClient, url, throttleGapMs, ct)
            .ConfigureAwait(false);

        if (json is null) return null;

        var entity = json["entities"]?[qid]?.AsObject();
        if (entity is null) return null;

        // Prefer the preferred language; fall back to English.
        var label = entity["labels"]?[language]?["value"]?.GetValue<string>()
                 ?? entity["labels"]?["en"]?["value"]?.GetValue<string>()
                 ?? qid;
        var desc  = entity["descriptions"]?[language]?["value"]?.GetValue<string>()
                 ?? entity["descriptions"]?["en"]?["value"]?.GetValue<string>();

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
                $"&type=item&language={Uri.EscapeDataString(request.Language)}&format=json&limit=3";

            var searchJson = await ThrottledGetAsync<JsonObject>(client, searchUrl, throttleGapMs, ct)
                .ConfigureAwait(false);

            var candidateQids = ExtractCandidateQids(searchJson);
            if (candidateQids.Count == 0)
            {
                _logger.LogDebug(
                    "Wikidata: no search results for '{Name}' (entity {Id})",
                    name, request.EntityId);
                return [];
            }

            // Step 2: fetch each candidate entity and verify P31=Q5 (instance of human).
            var personLangs = request.Language == "en" ? "en" : $"{request.Language}|en";
            string? qid = null;
            JsonObject? entityJson = null;

            foreach (var candidateQid in candidateQids)
            {
                var entityUrl = $"{request.BaseUrl.TrimEnd('/')}" +
                    $"?action=wbgetentities&ids={Uri.EscapeDataString(candidateQid)}" +
                    $"&format=json&languages={Uri.EscapeDataString(personLangs)}&props=labels|descriptions|claims";

                var candidateJson = await ThrottledGetAsync<JsonObject>(client, entityUrl, throttleGapMs, ct)
                    .ConfigureAwait(false);

                if (IsHumanEntity(candidateJson, candidateQid))
                {
                    qid = candidateQid;
                    entityJson = candidateJson;
                    break;
                }

                _logger.LogDebug(
                    "Wikidata: candidate {Qid} for '{Name}' is not P31=Q5 (human), trying next",
                    candidateQid, name);
            }

            if (qid is null || entityJson is null)
            {
                _logger.LogDebug(
                    "Wikidata: no human entity found for '{Name}' among {Count} candidates (entity {Id})",
                    name, candidateQids.Count, request.EntityId);
                return [];
            }

            var claims = ParsePersonEntity(entityJson, qid, commonsTemplate);

            // Step 3: SPARQL deep-hydration for Person-scoped properties (occupation, social
            // media, pseudonyms, biographical details, etc.).  Only runs when a SPARQL endpoint
            // is configured; gracefully degrades to API-only claims when unavailable.
            if (!string.IsNullOrWhiteSpace(request.SparqlBaseUrl))
            {
                try
                {
                    using var sparqlClient = _httpFactory.CreateClient("wikidata_sparql");
                    var effectiveMap = universeConfig is not null
                        ? WikidataSparqlPropertyMap.BuildMapFromUniverse(universeConfig)
                        : WikidataSparqlPropertyMap.DefaultMap;
                    var sparql = WikidataSparqlPropertyMap.BuildPersonSparqlQuery(
                        qid, effectiveMap, request.Language);

                    if (!string.IsNullOrWhiteSpace(sparql))
                    {
                        // Returns list starting with wikidata_qid — skip it (already present).
                        var sparqlClaims = await ExecuteSparqlQueryAsync(
                            sparqlClient, request.SparqlBaseUrl, sparql, qid,
                            effectiveMap, scopeExclusions: null, throttleGapMs, ct,
                            scope: "Person").ConfigureAwait(false);

                        foreach (var c in sparqlClaims)
                        {
                            if (c.Key is not "wikidata_qid") // avoid duplicate
                                claims.Add(c);
                        }

                        _logger.LogDebug(
                            "Wikidata SPARQL person deep-hydration for {Qid}: {Count} extra claims",
                            qid, sparqlClaims.Count - 1);
                    }
                }
                catch (Exception sparqlEx) when (sparqlEx is not OperationCanceledException)
                {
                    _logger.LogWarning(sparqlEx,
                        "Wikidata person SPARQL step failed for QID {Qid}; using API claims only", qid);
                }
            }

            return claims;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HTTP timeout from the HTTP client — return empty result, do not propagate
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Wikidata person enrichment failed for '{Name}' / entity {Id}",
                name, request.EntityId);
            return [];
        }
    }

    /// <summary>
    /// Extracts all candidate QIDs from a Wikidata search response.
    /// </summary>
    private static List<string> ExtractCandidateQids(JsonObject? searchJson)
    {
        var results = new List<string>();
        if (searchJson is null) return results;

        var searchResults = searchJson["search"]?.AsArray();
        if (searchResults is null) return results;

        foreach (var item in searchResults)
        {
            var id = item?["id"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(id))
                results.Add(id);
        }

        return results;
    }

    /// <summary>
    /// Verifies that a Wikidata entity is a human (P31 contains Q5).
    /// Also accepts Q15632617 (fictional human) and Q5 subclasses commonly
    /// used for pseudonyms and pen names.
    /// </summary>
    private static bool IsHumanEntity(JsonObject? entityJson, string qid)
    {
        if (entityJson is null) return false;

        var entity = entityJson["entities"]?[qid]?.AsObject();
        if (entity is null) return false;

        var p31Array = entity["claims"]?["P31"]?.AsArray();
        if (p31Array is null) return false;

        foreach (var claim in p31Array)
        {
            var entityId = claim?["mainsnak"]?["datavalue"]?["value"]?["id"]?.GetValue<string>();
            if (entityId is null) continue;

            // Q5 = human, Q15632617 = fictional human (pen name entity),
            // Q4167410 = Wikimedia disambiguation page (skip)
            if (entityId is "Q5" or "Q15632617")
                return true;
        }

        return false;
    }

    private static List<ProviderClaim> ParsePersonEntity(
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
            string? matchedBridge = null;

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
                    (qid, matchedBridge) = await ResolveQidViaBridgesAsync(
                        sparqlClient, request, bridgePriority, throttleGapMs, ct).ConfigureAwait(false);

                    if (qid is not null)
                    {
                        _logger.LogInformation(
                            "Wikidata: resolved QID {Qid} via bridge {Bridge} for entity {Id}",
                            qid, matchedBridge, request.EntityId);
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
                        apiClient, request.BaseUrl, request.Title, throttleGapMs, ct,
                        request.Language, request.MediaType)
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
                qid, effectiveMap, scopeExclusions, request.Language);
            var claims = await ExecuteSparqlQueryAsync(
                sparqlClient, request.SparqlBaseUrl!, sparql, qid,
                effectiveMap, scopeExclusions, throttleGapMs, ct).ConfigureAwait(false);

            // ── ISBN Bridge Confidence Boost ────────────────────────────────
            // When the QID was resolved via ISBN (P212), the match is definitive.
            // Boost author and title claims to 0.95 (above OPF's 0.9) so the
            // Wikidata-sourced values override potentially incorrect embedded metadata.
            if (matchedBridge == "P212")
            {
                const double IsbnBridgeBoost = 0.95;
                var boosted = new List<ProviderClaim>(claims.Count + 1);

                foreach (var c in claims)
                {
                    if (c.Key is "author" or "title" && c.Confidence < IsbnBridgeBoost)
                        boosted.Add(new ProviderClaim(c.Key, c.Value, IsbnBridgeBoost));
                    else
                        boosted.Add(c);
                }

                // Emit a marker claim so downstream services know the QID source.
                boosted.Add(new ProviderClaim("qid_source", "isbn_bridge", 1.0));
                claims = boosted;

                _logger.LogInformation(
                    "ISBN bridge match for entity {Id}: boosted author/title to {Confidence}",
                    request.EntityId, IsbnBridgeBoost);
            }

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

    // ── Fictional Entity Enrichment ─────────────────────────────────────────

    /// <summary>
    /// Fetches SPARQL data for a fictional entity (Character, Location, Organization).
    /// The QID is already known (passed via <see cref="ProviderLookupRequest.Hints"/>
    /// with key <c>"wikidata_qid"</c>). Builds the scope-appropriate SPARQL query
    /// and returns claims for all applicable properties.
    /// </summary>
    private async Task<IReadOnlyList<ProviderClaim>> FetchFictionalEntityAsync(
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        // QID must be provided via hints — fictional entities are discovered by QID
        // during work hydration, not by title search.
        var qid = request.Hints?.GetValueOrDefault("wikidata_qid");
        if (string.IsNullOrWhiteSpace(qid))
        {
            _logger.LogDebug(
                "Wikidata fictional entity skipped for entity {Id}: no wikidata_qid hint",
                request.EntityId);
            return [];
        }

        if (string.IsNullOrWhiteSpace(request.SparqlBaseUrl))
        {
            _logger.LogWarning(
                "Wikidata fictional entity skipped for entity {Id}: SPARQL endpoint not configured",
                request.EntityId);
            return [new ProviderClaim("wikidata_qid", qid, 1.0)];
        }

        var universeConfig = _configLoader.LoadConfig<UniverseConfiguration>("universe", "wikidata");
        var effectiveMap = universeConfig is not null
            ? WikidataSparqlPropertyMap.BuildMapFromUniverse(universeConfig)
            : WikidataSparqlPropertyMap.DefaultMap;
        var throttleGapMs = GetThrottleGapMs();

        // Determine the scope name for this entity type.
        var scopeName = request.EntityType switch
        {
            EntityType.Character    => "Character",
            EntityType.Location     => "Location",
            EntityType.Organization => "Organization",
            _ => "Character", // Fallback — should not happen.
        };

        // Read scope exclusions (P18 is excluded for all fictional entity types).
        IReadOnlyCollection<string>? scopeExclusions = null;
        if (universeConfig?.ScopeExclusions.TryGetValue(scopeName, out var exclusions) == true)
            scopeExclusions = exclusions;

        try
        {
            using var sparqlClient = _httpFactory.CreateClient("wikidata_sparql");

            // Build scope-appropriate SPARQL query.
            var sparql = scopeName switch
            {
                "Character"    => WikidataSparqlPropertyMap.BuildCharacterSparqlQuery(qid, effectiveMap, scopeExclusions, request.Language),
                "Location"     => WikidataSparqlPropertyMap.BuildLocationSparqlQuery(qid, effectiveMap, scopeExclusions, request.Language),
                "Organization" => WikidataSparqlPropertyMap.BuildOrganizationSparqlQuery(qid, effectiveMap, scopeExclusions, request.Language),
                _              => WikidataSparqlPropertyMap.BuildCharacterSparqlQuery(qid, effectiveMap, scopeExclusions, request.Language),
            };

            if (string.IsNullOrEmpty(sparql))
            {
                _logger.LogDebug(
                    "Wikidata: no {Scope}-scoped properties enabled for QID {Qid}",
                    scopeName, qid);
                return [new ProviderClaim("wikidata_qid", qid, 1.0)];
            }

            var claims = await ExecuteSparqlQueryAsync(
                sparqlClient, request.SparqlBaseUrl!, sparql, qid,
                effectiveMap, scopeExclusions, throttleGapMs, ct,
                scope: scopeName).ConfigureAwait(false);

            _logger.LogInformation(
                "Wikidata SPARQL {Scope} enrichment for entity {Id}: QID={Qid}, {Count} claims",
                scopeName, request.EntityId, qid, claims.Count);

            return claims;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Wikidata {Scope} enrichment failed for entity {Id} (QID={Qid})",
                scopeName, request.EntityId, qid);
            return [];
        }
    }

    // ── QID Resolution: Bridge IDs ──────────────────────────────────────────

    /// <summary>
    /// Tries to find the Wikidata QID using bridge identifiers in priority order.
    /// The order is read from <c>config/universe/wikidata.json → bridge_lookup_priority</c>.
    /// Falls back to the compiled default order if the config is null.
    /// </summary>
    /// <summary>
    /// Resolves a QID via bridge identifiers. Returns the QID and the P-code
    /// of the bridge that matched (e.g. "P212" for ISBN).
    /// </summary>
    private async Task<(string? Qid, string? MatchedBridge)> ResolveQidViaBridgesAsync(
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
            // Compiled default order — ISBN first (definitive match).
            bridges =
            [
                ("P212",  request.Isbn),
                ("P3861", request.AppleBooksId),
                ("P3398", request.AudibleId),
                ("P4947", request.TmdbId),
                ("P345",  request.ImdbId),
                ("P1566", request.Asin),
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
                return (qid, pCode);
            }
        }

        return (null, null);
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
            // Strip "urn:isbn:" prefix — EPUB embeds ISBNs as URIs but Wikidata stores bare numbers.
            "isbn"           => StripIsbnPrefix(request.Isbn),
            _                => null,
        };

    /// <summary>Strips the "urn:isbn:" URI prefix from an ISBN if present.</summary>
    private static string? StripIsbnPrefix(string? isbn)
        => isbn?.StartsWith("urn:isbn:", StringComparison.OrdinalIgnoreCase) == true
            ? isbn[9..]
            : isbn;

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
        CancellationToken ct,
        string language = "en",
        MediaType mediaType = MediaType.Unknown)
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
            $"&type=item&language={Uri.EscapeDataString(language)}&format=json&limit=5";

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

        // Score each candidate using description-based heuristics:
        // - +2 if description contains affinity keywords for the media type
        //    (e.g. "novel", "book" for Books; "film" for Movies)
        // - -2 if description contains rejection keywords for the media type
        //    (e.g. "film", "calendar year" when looking for a Book)
        // - +1 if match type is "label" (primary label, not just an alias)
        // The highest-scoring candidate wins.
        var affinityKeywords   = GetDescriptionAffinityKeywords(mediaType);
        var rejectionKeywords  = GetDescriptionRejectionKeywords(mediaType);

        string? bestId = null;
        int bestScore  = int.MinValue;

        foreach (var r in results)
        {
            if (r is null) continue;
            var id          = r["id"]?.GetValue<string>();
            var description = (r["description"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant();
            var matchType   = r["match"]?["type"]?.GetValue<string>() ?? string.Empty;

            int score = 0;
            if (affinityKeywords.Any(k => description.Contains(k, StringComparison.Ordinal)))
                score += 2;
            if (rejectionKeywords.Any(k => description.Contains(k, StringComparison.Ordinal)))
                score -= 2;
            if (string.Equals(matchType, "label", StringComparison.OrdinalIgnoreCase))
                score += 1;

            _logger.LogDebug(
                "Wikidata title search candidate: {Id} description=\"{Desc}\" score={Score}",
                id, description.Length > 60 ? description[..60] + "…" : description, score);

            if (score > bestScore)
            {
                bestScore = score;
                bestId    = id;
            }
        }

        if (!string.IsNullOrWhiteSpace(bestId))
        {
            _logger.LogDebug(
                "Wikidata title search: selected {Id} (score={Score}) for \"{Title}\"",
                bestId, bestScore, title);
        }

        return string.IsNullOrWhiteSpace(bestId) ? null : bestId;
    }

    /// <summary>Description substrings that indicate a good match for the given media type.</summary>
    private static string[] GetDescriptionAffinityKeywords(MediaType mediaType) => mediaType switch
    {
        MediaType.Books      => ["novel", "book", "written work", "literary work", "fiction", "nonfiction",
                                 "novella", "short story", "anthology", "comic book", "graphic novel"],
        MediaType.Audiobooks => ["audiobook", "audio book", "novel", "written work"],
        MediaType.Movies     => ["film", "movie", "motion picture", "feature film"],
        MediaType.TV         => ["television", "tv series", "tv show", "animated series"],
        _                    => []
    };

    /// <summary>Description substrings that indicate a poor match for the given media type.</summary>
    private static string[] GetDescriptionRejectionKeywords(MediaType mediaType) => mediaType switch
    {
        MediaType.Books or MediaType.Audiobooks
            => ["calendar year", "leap year", "film", "television series", "tv series",
                "animated series", "video game", "video game series"],
        MediaType.Movies
            => ["novel", "book", "calendar year", "television series", "tv series"],
        _   => []
    };

    // ── SPARQL Execution & Parsing ──────────────────────────────────────────

    /// <summary>
    /// Executes a Work SPARQL query and parses the bindings into ProviderClaim list.
    /// Always emits the <c>wikidata_qid</c> claim first (confidence 1.0).
    /// Uses the effective property map and scope exclusions from universe configuration.
    /// </summary>
    private async Task<IReadOnlyList<ProviderClaim>> ExecuteSparqlQueryAsync(
        HttpClient sparqlClient,
        string sparqlBaseUrl,
        string sparql,
        string qid,
        IReadOnlyDictionary<string, WikidataProperty> effectiveMap,
        IReadOnlyCollection<string>? scopeExclusions,
        int throttleGapMs,
        CancellationToken ct,
        string scope = "Work")
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

        foreach (var prop in effectiveMap.Values.Where(p => p.Enabled && ScopeMatches(p.EntityScope, scope)))
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

                // Emit ONE claim carrying the full GROUP_CONCAT'd value (separator: |||).
                //
                // Previously this code split the concatenated string and emitted one
                // ProviderClaim per individual value.  That caused every character,
                // genre, and series entry to compete as separate claims for the SAME
                // canonical value key — the scoring engine's winner-takes-all logic
                // then discarded all but one (e.g. only 1 of 20 Dune characters
                // survived, and all 5 genre claims conflicted → no genre stored at all).
                //
                // By emitting a SINGLE claim containing the full pipe-separated list,
                // the canonical value stores all values together.  Consumers that need
                // individual items (ExtractFictionalEntityReferences, RelationshipPopulationService)
                // already split on the ||| separator.
                if (prop.IsEntityValued)
                {
                    // Labels claim: full pipe-separated list of human-readable names.
                    claims.Add(new ProviderClaim(prop.ClaimKey, concatenated, prop.Confidence));

                    // QIDs claim: strip entity URI prefixes and emit as pipe-separated QIDs.
                    var urisConcatenated = binding.ContainsKey(varName + "Uris")
                        ? binding[varName + "Uris"]?["value"]?.GetValue<string>()
                        : null;

                    if (!string.IsNullOrWhiteSpace(urisConcatenated))
                    {
                        var strippedQids = string.Join(
                            WikidataSparqlPropertyMap.MultiValueSeparator,
                            urisConcatenated.Split(WikidataSparqlPropertyMap.MultiValueSeparator,
                                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(StripEntityUri)
                                .Where(q => !string.IsNullOrWhiteSpace(q)));

                        if (!string.IsNullOrWhiteSpace(strippedQids))
                            claims.Add(new ProviderClaim(prop.ClaimKey + "_qid", strippedQids, prop.Confidence));
                    }
                }
                else
                {
                    // Non-entity multi-valued: apply transform to each part, then rejoin.
                    var parts = concatenated.Split(WikidataSparqlPropertyMap.MultiValueSeparator,
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var transformed = parts
                        .Select(v => ValueTransformRegistry.Apply(prop.ValueTransform, v))
                        .Where(v => !string.IsNullOrWhiteSpace(v));
                    var joined = string.Join(WikidataSparqlPropertyMap.MultiValueSeparator, transformed);
                    if (!string.IsNullOrWhiteSpace(joined))
                        claims.Add(new ProviderClaim(prop.ClaimKey, joined, prop.Confidence));
                }
            }
            else
            {
                // Single-valued: original behavior.
                string? labelValue = binding.ContainsKey(varName + "Label")
                    ? binding[varName + "Label"]?["value"]?.GetValue<string>()
                    : null;

                string? rawUri = binding.ContainsKey(varName)
                    ? binding[varName]?["value"]?.GetValue<string>()
                    : null;

                // Prefer the human-readable label; fall back to the raw value.
                // For entity-valued properties, strip the URI to a QID when no label exists
                // (avoids storing full Wikidata URIs as canonical values).
                var rawValue = labelValue;
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    rawValue = prop.IsEntityValued && !string.IsNullOrWhiteSpace(rawUri)
                        ? StripEntityUri(rawUri)
                        : rawUri;
                }

                if (string.IsNullOrWhiteSpace(rawValue))
                    continue;

                var claimValue = ValueTransformRegistry.Apply(prop.ValueTransform, rawValue);
                if (!string.IsNullOrWhiteSpace(claimValue))
                    claims.Add(new ProviderClaim(prop.ClaimKey, claimValue, prop.Confidence));

                // For entity-valued single-valued properties, also emit bare QID
                // from the raw URI binding (e.g. based_on_qid).
                if (prop.IsEntityValued && !string.IsNullOrWhiteSpace(rawUri))
                {
                    var bareQid = StripEntityUri(rawUri);
                    if (!string.IsNullOrWhiteSpace(bareQid))
                        claims.Add(new ProviderClaim(prop.ClaimKey + "_qid", bareQid, prop.Confidence));
                }
            }
        }

        // ── Cache QID labels for offline resolution ─────────────────────────
        // Fire-and-forget style: errors here must not break the claim pipeline.
        try
        {
            var labelsToCache = new List<QidLabel>();

            foreach (var claim in claims)
            {
                // Skip non-QID claims and the _qid suffix claims themselves.
                if (!claim.Key.EndsWith("_qid", StringComparison.Ordinal) &&
                    claim.Key != "wikidata_qid")
                    continue;

                // For _qid claims, find the matching label claim.
                var baseKey = claim.Key == "wikidata_qid"
                    ? null // handled separately below
                    : claim.Key[..^4]; // strip "_qid"

                if (claim.Key == "wikidata_qid")
                {
                    // Cache the primary entity QID with its label from a title/name claim.
                    var titleClaim = claims.FirstOrDefault(c => c.Key is "title" or "name");
                    if (titleClaim is not null)
                    {
                        labelsToCache.Add(new QidLabel
                        {
                            Qid         = claim.Value,
                            Label       = titleClaim.Value,
                            EntityType  = scope,
                        });
                    }
                }
                else if (baseKey is not null)
                {
                    // Match _qid claim values with their label counterpart.
                    var labelClaim = claims.FirstOrDefault(c => c.Key == baseKey);
                    if (labelClaim is null) continue;

                    var qids   = claim.Value.Split(WikidataSparqlPropertyMap.MultiValueSeparator,
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var labels = labelClaim.Value.Split(WikidataSparqlPropertyMap.MultiValueSeparator,
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    for (int i = 0; i < Math.Min(qids.Length, labels.Length); i++)
                    {
                        if (!string.IsNullOrWhiteSpace(qids[i]) && !string.IsNullOrWhiteSpace(labels[i]))
                        {
                            labelsToCache.Add(new QidLabel
                            {
                                Qid        = qids[i],
                                Label      = labels[i],
                                EntityType = baseKey,
                            });
                        }
                    }
                }
            }

            if (labelsToCache.Count > 0)
                await _qidLabelRepo.UpsertBatchAsync(labelsToCache, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // QID label caching is best-effort; never fail the pipeline.
            _logger.LogDebug(ex, "Failed to cache QID labels from SPARQL response for {Qid}", qid);
        }

        return claims;
    }

    /// <summary>
    /// Strips a Wikidata entity URI (e.g. <c>http://www.wikidata.org/entity/Q937618</c>)
    /// to a bare QID (e.g. <c>Q937618</c>). Returns <c>null</c> if the value is not
    /// a recognisable entity URI.
    /// </summary>
    /// <summary>
    /// Returns true when the property's declared scope matches the requested scope.
    /// Supports "Both" wildcard and comma-separated scope lists.
    /// </summary>
    private static bool ScopeMatches(string propertyScope, string requestedScope)
    {
        if (propertyScope.Equals("Both", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!propertyScope.Contains(','))
            return propertyScope.Equals(requestedScope, StringComparison.OrdinalIgnoreCase);
        foreach (var seg in propertyScope.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            if (seg.Equals(requestedScope, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string? StripEntityUri(string value)
    {
        var parts = value.Split("::", 2, StringSplitOptions.None);
        var uri = parts[0];

        const string prefix = "http://www.wikidata.org/entity/";
        string qid = uri;

        if (uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            qid = uri[prefix.Length..];
        }
        // Already a bare QID (e.g. "Q937618")?
        else if (!(uri.Length > 1 && uri[0] is 'Q' or 'q' && char.IsDigit(uri[1])))
        {
            return null;
        }

        return parts.Length > 1 ? $"{qid}::{parts[1]}" : qid;
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

            // Use POST to avoid HTTP 414 (URI Too Long) on large SPARQL queries.
            // Wikidata's SPARQL endpoint supports both GET and POST; POST has no URL length limit.
            using var request = new HttpRequestMessage(HttpMethod.Post, sparqlBaseUrl.TrimEnd('/'));
            request.Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("query", sparql),
            ]);
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
