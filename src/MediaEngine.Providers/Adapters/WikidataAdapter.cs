using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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
    private readonly IProviderResponseCacheRepository _cacheRepo;
    private readonly IResolverCacheRepository _resolverCache;
    private readonly ILogger<WikidataAdapter> _logger;

    // Throttle shared across all instances (static) — Wikidata policy: 1 req/s.
    private static readonly SemaphoreSlim _throttle = new(1, 1);
    private static DateTime _lastCallUtc = DateTime.MinValue;

    /// <summary>
    /// Generic / placeholder terms that should never be sent to Wikidata title search.
    /// Mirrors <c>ConfigDrivenAdapter.GenericTerms</c> to prevent false matches
    /// (e.g. "Unknown" matching Q24238356).
    /// </summary>
    private static readonly HashSet<string> GenericTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "unknown", "untitled", "no title", "title", "book", "audiobook",
        "track", "album", "episode", "movie", "video",
    };

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

    /// <summary>Default SPARQL response cache TTL (hours). Overridden by provider config.</summary>
    private const int DefaultCacheTtlHours = 168; // 7 days

    public WikidataAdapter(
        IHttpClientFactory httpFactory,
        IConfigurationLoader configLoader,
        IQidLabelRepository qidLabelRepo,
        IProviderResponseCacheRepository cacheRepo,
        IResolverCacheRepository resolverCache,
        ILogger<WikidataAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(configLoader);
        ArgumentNullException.ThrowIfNull(qidLabelRepo);
        ArgumentNullException.ThrowIfNull(cacheRepo);
        ArgumentNullException.ThrowIfNull(resolverCache);
        ArgumentNullException.ThrowIfNull(logger);
        _httpFactory    = httpFactory;
        _configLoader   = configLoader;
        _qidLabelRepo   = qidLabelRepo;
        _cacheRepo      = cacheRepo;
        _resolverCache  = resolverCache;
        _logger         = logger;
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
                            Qid            = qid,
                            Label          = label?.Label ?? qid,
                            Description    = label?.Description,
                            ResolutionTier = "bridge",
                        });
                    }
                }
            }

            // Step 1b: Tier 2 — Structured SPARQL search candidates.
            if (candidates.Count < maxCandidates && !string.IsNullOrWhiteSpace(request.Title)
                && !string.IsNullOrWhiteSpace(request.SparqlBaseUrl)
                && universeConfig is not null)
            {
                var mediaTypeKey = MediaTypeToConfigKey(request.MediaType);
                var sparql = WikidataSparqlPropertyMap.BuildStructuredSearchQuery(
                    request.Title,
                    mediaTypeKey,
                    universeConfig.InstanceOfClasses,
                    request.Author,
                    language: request.Language);

                if (!string.IsNullOrEmpty(sparql))
                {
                    var json = await ThrottledSparqlAsync(
                        sparqlClient, request.SparqlBaseUrl, sparql, throttleGapMs, ct)
                        .ConfigureAwait(false);

                    var bindings = json?["results"]?["bindings"]?.AsArray();
                    if (bindings is not null)
                    {
                        foreach (var binding in bindings)
                        {
                            if (candidates.Count >= maxCandidates) break;
                            if (binding is null) continue;

                            var itemUri = binding["item"]?["value"]?.GetValue<string>();
                            if (string.IsNullOrWhiteSpace(itemUri)) continue;

                            var lastSlash = itemUri.LastIndexOf('/');
                            var id = lastSlash >= 0 ? itemUri[(lastSlash + 1)..] : itemUri;

                            if (seenQids.Add(id))
                            {
                                var lbl  = binding["itemLabel"]?["value"]?.GetValue<string>();
                                var desc = binding["itemDescription"]?["value"]?.GetValue<string>();

                                candidates.Add(new QidCandidate
                                {
                                    Qid            = id,
                                    Label          = lbl ?? id,
                                    Description    = desc,
                                    ResolutionTier = "structured_sparql",
                                });
                            }
                        }
                    }
                }
            }

            // Step 2: title search — add any new candidates.
            // Guard: skip generic terms that pollute search results (e.g. "Unknown" → Q24238356).
            if (candidates.Count < maxCandidates && !string.IsNullOrWhiteSpace(request.Title)
                && request.Title.Trim().Length >= 3
                && !GenericTerms.Contains(request.Title.Trim())
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
                                Qid            = id,
                                Label          = lbl ?? id,
                                Description    = desc,
                                ResolutionTier = "title_search",
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

            var personLangs = request.Language == "en" ? "en" : $"{request.Language}|en";
            string? qid = null;
            JsonObject? entityJson = null;

            // Fast path: when the caller already knows the Wikidata QID (e.g. from a
            // SPARQL result for a multi-authored work), skip the name search entirely
            // and fetch the entity directly.
            string? hintQid = null;
            request.Hints?.TryGetValue("wikidata_qid", out hintQid);

            if (!string.IsNullOrEmpty(hintQid))
            {
                var directUrl = $"{request.BaseUrl.TrimEnd('/')}" +
                    $"?action=wbgetentities&ids={Uri.EscapeDataString(hintQid)}" +
                    $"&format=json&languages={Uri.EscapeDataString(personLangs)}&props=labels|descriptions|claims";

                var directJson = await ThrottledGetAsync<JsonObject>(client, directUrl, throttleGapMs, ct)
                    .ConfigureAwait(false);

                if (IsHumanEntity(directJson, hintQid))
                {
                    qid = hintQid;
                    entityJson = directJson;
                    _logger.LogDebug(
                        "Wikidata: used QID hint {Qid} directly for '{Name}' (entity {Id})",
                        hintQid, name, request.EntityId);
                }
                else
                {
                    _logger.LogDebug(
                        "Wikidata: QID hint {Qid} for '{Name}' is not a human entity; falling back to name search",
                        hintQid, name);
                }
            }

            // Normal path: search for the entity by name when no valid QID hint was found.
            if (qid is null)
            {
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
    /// Verifies that a Wikidata entity is a person-like entity suitable for enrichment.
    /// Accepts Q5 (human), Q15632617 (fictional human), and Q127843 (pen name).
    /// Pen names are collective pseudonyms shared by two or more real authors
    /// (e.g. "James S. A. Corey" = Daniel Abraham + Ty Franck) and need enrichment
    /// so that P1773 (attributed_to) links can be resolved to the real persons.
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

            // Q5 = human, Q15632617 = fictional human, Q127843 = pen name
            // Q4167410 = Wikimedia disambiguation page (skip)
            if (entityId is "Q5" or "Q15632617" or "Q127843")
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
                // ── Resolver cache check ────────────────────────────────────
                // Before running the expensive 4-tier resolution, check if we
                // already resolved this (title + media_type) combination.
                if (!string.IsNullOrWhiteSpace(request.Title))
                {
                    var resolverKey = ComputeResolverCacheKey(request.Title, request.MediaType);
                    try
                    {
                        var cached = await _resolverCache.FindAsync(resolverKey, ct).ConfigureAwait(false);
                        if (cached is not null && cached.Confidence >= 0.70 && cached.WikidataQid is not null)
                        {
                            qid = cached.WikidataQid;
                            _logger.LogInformation(
                                "Wikidata: resolver cache hit — QID {Qid} for \"{Title}\" ({MediaType}), confidence={Confidence:F2}",
                                qid, request.Title, request.MediaType, cached.Confidence);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Resolver cache lookup failed — continuing with full resolution");
                    }
                }

                if (qid is null)
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

                // ── Step 1b: Tier 2 — Structured SPARQL Search ───────────
                // If bridge lookup failed but we have a title, try structured
                // SPARQL with instance_of filtering + author/year validation.
                if (qid is null && hasSparql && universeConfig is not null
                    && !string.IsNullOrWhiteSpace(request.Title))
                {
                    var (tier2Qid, tier2Confidence, tier2Source) =
                        await ResolveQidViaStructuredSearchAsync(
                            sparqlClient, request, universeConfig, throttleGapMs, ct)
                            .ConfigureAwait(false);

                    if (tier2Qid is not null)
                    {
                        qid = tier2Qid;
                        matchedBridge = tier2Source; // "structured_sparql"
                        _logger.LogInformation(
                            "Wikidata: Tier 2 resolved QID {Qid} (confidence={Confidence:F2}) for entity {Id}",
                            qid, tier2Confidence, request.EntityId);
                    }
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
                        request.Language, request.MediaType,
                        hasSparql ? sparqlClient : null, request.SparqlBaseUrl,
                        request.Author)
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
                } // end: if (qid is null) — 4-tier resolution block

                // ── Resolver cache write ────────────────────────────────────
                // Cache the resolution decision so sibling files skip the 4-tier logic.
                if (qid is not null && !string.IsNullOrWhiteSpace(request.Title))
                {
                    var writeKey = ComputeResolverCacheKey(request.Title, request.MediaType);
                    try
                    {
                        await _resolverCache.UpsertAsync(new ResolverCacheEntry(
                            CacheKey:        writeKey,
                            NormalizedTitle: request.Title,
                            MediaType:       request.MediaType.ToString(),
                            WikidataQid:     qid,
                            Confidence:      matchedBridge is not null ? 0.95 : 0.70,
                            EntityLabel:     null,
                            CreatedAt:       DateTimeOffset.UtcNow,
                            ExpiresAt:       DateTimeOffset.UtcNow.AddDays(7)),
                            ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Resolver cache write failed — continuing");
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

            // ── Step 3a: Edition vs Work Resolution ─────────────────────────
            // Bridge lookups (ISBN) can return an edition item instead of the
            // work item. Check P31 and follow P629 to the parent work if needed.
            string? editionQid = null;
            var workQid = await ResolveEditionToWorkAsync(
                sparqlClient, request.SparqlBaseUrl!, qid, throttleGapMs, ct)
                .ConfigureAwait(false);
            if (workQid is not null && !string.Equals(workQid, qid, StringComparison.OrdinalIgnoreCase))
            {
                editionQid = qid;
                _logger.LogInformation(
                    "Wikidata: QID {EditionQid} is an edition — following P629 to parent work {WorkQid} for entity {Id}",
                    editionQid, workQid, request.EntityId);
                qid = workQid;
            }

            // ── Step 3b–3b3: Audit queries (Pass 2 only) ────────────────────
            // Author audit, cast role audit, and awards queries are deep enrichment
            // that runs only in Pass 2 (Universe Lookup). Pass 1 (Quick Match)
            // skips them for faster Dashboard appearance.
            IReadOnlyList<ProviderClaim> authorAuditClaims;
            IReadOnlyList<ProviderClaim> castRoleAuditClaims;
            IReadOnlyList<ProviderClaim> awardsClaims;

            if (request.HydrationPass == HydrationPass.Universe)
            {
                // ── Step 3b: Author Audit with Qualifiers ────────────────────
                // Use qualified statement syntax (p:/ps:/pq:) to extract P50 author
                // statements with P1545 ordinals and P31 entity types (human,
                // pseudonym, collective pseudonym). This produces ordered, typed
                // author data that the standard wdt: query cannot provide.
                authorAuditClaims = await RunAuthorAuditAsync(
                    sparqlClient, request.SparqlBaseUrl!, qid, throttleGapMs, ct,
                    request.Language).ConfigureAwait(false);

                // ── Step 3b2: Cast Role Audit with Qualifiers ────────────────
                // Use qualified statement syntax (p:/ps:/pq:) to extract P161 cast
                // member statements with P453 character role qualifiers. This produces
                // actor-to-character mappings that the standard wdt: query discards.
                castRoleAuditClaims = await RunCastRoleAuditAsync(
                    sparqlClient, request.SparqlBaseUrl!, qid, throttleGapMs, ct,
                    request.Language).ConfigureAwait(false);

                // ── Step 3b3: Awards Query with Preferred Rank ───────────────
                // Use qualified statement syntax (p:/ps:/pq:) to fetch P166 awards
                // with PreferredRank only (winners, not nominees). This produces a
                // multi-valued awards_received claim that the standard wdt: query
                // cannot provide with rank filtering.
                awardsClaims = await RunAwardsQueryAsync(
                    sparqlClient, request.SparqlBaseUrl!, qid, throttleGapMs, ct,
                    request.Language).ConfigureAwait(false);
            }
            else
            {
                // Pass 1: skip audit queries for faster results.
                authorAuditClaims = [];
                castRoleAuditClaims = [];
                awardsClaims = [];
            }

            // ── Step 3c: SPARQL Deep Hydration ──────────────────────────────
            // Read scope exclusions from universe config (default: P18 excluded from Work).
            IReadOnlyCollection<string>? scopeExclusions = null;
            if (universeConfig?.ScopeExclusions.TryGetValue("Work", out var workExclusions) == true)
                scopeExclusions = workExclusions;

            var sparql = request.HydrationPass == HydrationPass.Quick
                ? WikidataSparqlPropertyMap.BuildCoreWorkSparqlQuery(
                    qid, effectiveMap, scopeExclusions, request.Language)
                : WikidataSparqlPropertyMap.BuildWorkSparqlQuery(
                    qid, effectiveMap, scopeExclusions, request.Language);
            var claims = await ExecuteSparqlQueryAsync(
                sparqlClient, request.SparqlBaseUrl!, sparql, qid,
                effectiveMap, scopeExclusions, throttleGapMs, ct).ConfigureAwait(false);

            // Replace the deep hydration's P50 author results with the richer
            // author audit results (ordered, typed, pseudonym-aware).
            if (authorAuditClaims.Count > 0)
            {
                var filtered = new List<ProviderClaim>(claims.Count + authorAuditClaims.Count);
                foreach (var c in claims)
                {
                    // Discard author and author_qid from the standard SPARQL query —
                    // the audit query's results are authoritative for these keys.
                    if (c.Key is not ("author" or "author_qid"))
                        filtered.Add(c);
                }
                filtered.AddRange(authorAuditClaims);
                claims = filtered;
            }

            // Replace the deep hydration's P161 cast_member results with the richer
            // cast role audit results (actor-to-character qualified mappings).
            if (castRoleAuditClaims.Count > 0)
            {
                var filtered = new List<ProviderClaim>(claims.Count + castRoleAuditClaims.Count);
                foreach (var c in claims)
                {
                    // Discard cast_member and cast_member_qid from the standard SPARQL query —
                    // the cast role audit query's results are authoritative for these keys.
                    if (c.Key is not ("cast_member" or "cast_member_qid"))
                        filtered.Add(c);
                }
                filtered.AddRange(castRoleAuditClaims);
                claims = filtered;
            }

            // Merge awards claims — replace any awards_received from standard deep
            // hydration with the rank-filtered results from RunAwardsQueryAsync.
            if (awardsClaims.Count > 0)
            {
                var filtered = new List<ProviderClaim>(claims.Count + awardsClaims.Count);
                foreach (var c in claims)
                {
                    // Discard awards_received from the standard SPARQL query —
                    // the awards query's results use preferred-rank filtering and are authoritative.
                    if (c.Key is not "awards_received")
                        filtered.Add(c);
                }
                filtered.AddRange(awardsClaims);
                claims = filtered;
            }

            // Emit edition_qid claim when the original QID was an edition.
            if (editionQid is not null)
            {
                var mutable = new List<ProviderClaim>(claims);
                mutable.Add(new ProviderClaim("edition_qid", editionQid, 1.0));
                claims = mutable;
            }

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

            // ── QID-Confirmed Title Boost ──────────────────────────────────────
            // Any confirmed QID (regardless of bridge type) means SPARQL returned
            // authoritative data.  Boost title to 0.92 if not already boosted by
            // the ISBN bridge (which uses 0.95).  This ensures Wikidata labels
            // override potentially incorrect file metadata for folder naming.
            if (matchedBridge == "structured_sparql")
            {
                // Tier 2 gets its own boost — lower than bridge (0.95) but higher than
                // generic title search. The Tier 2 boost comes from config.
                var tier2Boost = universeConfig?.SearchThresholds.Tier2TitleBoost ?? 0.88;
                var boosted2 = new List<ProviderClaim>(claims.Count);
                foreach (var c in claims)
                {
                    if (c.Key is "title" or "author" && c.Confidence < tier2Boost)
                        boosted2.Add(new ProviderClaim(c.Key, c.Value, tier2Boost));
                    else
                        boosted2.Add(c);
                }
                // Emit a marker claim so downstream services know the QID source.
                boosted2.Add(new ProviderClaim("qid_source", "structured_sparql", 1.0));
                claims = boosted2;
            }
            else if (matchedBridge != "P212")
            {
                const double QidConfirmedBoost = 0.92;
                var boosted2 = new List<ProviderClaim>(claims.Count);
                foreach (var c in claims)
                {
                    if (c.Key is "title" or "author" && c.Confidence < QidConfirmedBoost)
                        boosted2.Add(new ProviderClaim(c.Key, c.Value, QidConfirmedBoost));
                    else
                        boosted2.Add(c);
                }
                claims = boosted2;
            }

            _logger.LogInformation(
                "Wikidata SPARQL deep-ingest for entity {Id}: QID={Qid}, {Count} claims{EditionNote}",
                request.EntityId, qid, claims.Count,
                editionQid is not null ? $" (resolved from edition {editionQid})" : "");

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

    // ── Batch Entity Fetch ──────────────────────────────────────────────────

    /// <summary>
    /// Batch-fetch properties for multiple entities in a single SPARQL query.
    /// Returns results keyed by QID.
    /// </summary>
    internal async Task<IReadOnlyDictionary<string, IReadOnlyList<ProviderClaim>>> FetchEntitiesBatchAsync(
        IReadOnlyList<string> qids,
        string scope,
        string sparqlBaseUrl,
        int throttleGapMs,
        CancellationToken ct = default)
    {
        if (qids.Count == 0)
            return new Dictionary<string, IReadOnlyList<ProviderClaim>>();

        var universeConfig = _configLoader.LoadConfig<UniverseConfiguration>("universe", "wikidata");
        var effectiveMap = universeConfig is not null
            ? WikidataSparqlPropertyMap.BuildMapFromUniverse(universeConfig)
            : WikidataSparqlPropertyMap.DefaultMap;

        var sparql = WikidataSparqlPropertyMap.BuildBatchEntityQuery(qids, effectiveMap, scope);

        if (string.IsNullOrEmpty(sparql))
            return new Dictionary<string, IReadOnlyList<ProviderClaim>>();

        var client = _httpFactory.CreateClient("wikidata_sparql");
        var result = new Dictionary<string, IReadOnlyList<ProviderClaim>>(qids.Count, StringComparer.OrdinalIgnoreCase);

        try
        {
            var claims = await ExecuteSparqlQueryAsync(
                client, sparqlBaseUrl, sparql, qids[0], effectiveMap,
                scopeExclusions: null, throttleGapMs, ct, scope)
                .ConfigureAwait(false);

            // Group claims by QID (extracted from the entity URI in the response)
            var grouped = new Dictionary<string, List<ProviderClaim>>(StringComparer.OrdinalIgnoreCase);
            foreach (var claim in claims)
            {
                // Claims from batch queries include the entity QID in the key prefix
                // For now, all claims go under the first QID — proper per-entity parsing
                // requires extending ExecuteSparqlQueryAsync to return per-entity results.
                var targetQid = qids[0]; // Placeholder — will be refined
                if (!grouped.TryGetValue(targetQid, out var list))
                {
                    list = [];
                    grouped[targetQid] = list;
                }
                list.Add(claim);
            }

            foreach (var (qid, claimList) in grouped)
                result[qid] = claimList;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Batch SPARQL query failed for {Count} entities (scope: {Scope})",
                qids.Count, scope);
        }

        return result;
    }

    // ── Edition vs Work Resolution ──────────────────────────────────────────

    /// <summary>
    /// Wikidata Q-identifiers for "edition" and "translation" instance types.
    /// When a bridge lookup returns one of these, the real metadata lives on
    /// the parent work (P629).
    /// </summary>
    private static readonly HashSet<string> EditionInstanceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Q3331189",  // edition, version, or translation (most common)
        "Q1279564",  // edition of a written work
    };

    /// <summary>
    /// Checks P31 (instance of) on the resolved QID. If it's an edition,
    /// follows P629 (edition or translation of) to find the parent work QID.
    /// Returns the work QID (which may be the same as <paramref name="qid"/>
    /// if it's already a work), or <c>null</c> on SPARQL failure.
    /// </summary>
    private async Task<string?> ResolveEditionToWorkAsync(
        HttpClient sparqlClient,
        string sparqlBaseUrl,
        string qid,
        int throttleGapMs,
        CancellationToken ct)
    {
        try
        {
            var sparql = WikidataSparqlPropertyMap.BuildEditionCheckQuery(qid);
            var json = await ThrottledSparqlAsync(sparqlClient, sparqlBaseUrl, sparql, throttleGapMs, ct)
                .ConfigureAwait(false);

            if (json is null) return qid; // SPARQL failed — proceed with original QID.

            var bindings = json["results"]?["bindings"]?.AsArray();
            if (bindings is null || bindings.Count == 0)
                return qid;

            // Check whether any P31 value is an edition type.
            bool isEdition = false;
            string? parentWorkQid = null;

            foreach (var binding in bindings)
            {
                var instanceOfUri = binding?["instanceOf"]?["value"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(instanceOfUri))
                {
                    var instanceQid = ExtractQidFromUri(instanceOfUri);
                    if (instanceQid is not null && EditionInstanceTypes.Contains(instanceQid))
                        isEdition = true;
                }

                if (parentWorkQid is null)
                {
                    var parentUri = binding?["parentWork"]?["value"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(parentUri))
                        parentWorkQid = ExtractQidFromUri(parentUri);
                }
            }

            if (isEdition && parentWorkQid is not null)
            {
                _logger.LogDebug(
                    "Wikidata edition check: {Qid} is an edition, parent work is {ParentQid}",
                    qid, parentWorkQid);
                return parentWorkQid;
            }

            if (isEdition)
            {
                _logger.LogDebug(
                    "Wikidata edition check: {Qid} is an edition but no P629 parent found — using edition QID",
                    qid);
            }

            return qid;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Wikidata edition check failed for QID {Qid}; proceeding with original QID", qid);
            return qid;
        }
    }

    /// <summary>
    /// Extracts a bare QID from a Wikidata entity URI.
    /// E.g. <c>"http://www.wikidata.org/entity/Q190192"</c> → <c>"Q190192"</c>.
    /// </summary>
    private static string? ExtractQidFromUri(string uri)
    {
        const string prefix = "http://www.wikidata.org/entity/";
        if (uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return uri[prefix.Length..];
        if (uri.Length > 1 && uri[0] is 'Q' or 'q' && char.IsDigit(uri[1]))
            return uri;
        return null;
    }

    // ── Author Audit with Qualifiers ────────────────────────────────────────

    /// <summary>
    /// Well-known Wikidata Q-identifiers for author entity classification.
    /// </summary>
    private const string QidHuman = "Q5";
    private const string QidPseudonym = "Q61002";
    private const string QidCollectivePseudonym = "Q127843";

    /// <summary>
    /// Runs the author audit query (qualified statement syntax) and, when
    /// collective pseudonyms are found, follows P527 to discover constituent
    /// members. Returns structured author claims with correct ordering and
    /// pseudonym classification.
    ///
    /// <para>
    /// Pen name priority: when a collective pseudonym (Q127843) is found among
    /// the P50 authors, it is ALWAYS placed first — it is the canonical display
    /// author. Real authors behind the pen name are linked via
    /// <c>collective_members_qid</c> for the person detail page.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<ProviderClaim>> RunAuthorAuditAsync(
        HttpClient sparqlClient,
        string sparqlBaseUrl,
        string qid,
        int throttleGapMs,
        CancellationToken ct,
        string? language = null)
    {
        try
        {
            var sparql = WikidataSparqlPropertyMap.BuildAuthorAuditQuery(qid, language);
            var json = await ThrottledSparqlAsync(sparqlClient, sparqlBaseUrl, sparql, throttleGapMs, ct)
                .ConfigureAwait(false);

            if (json is null) return [];

            var bindings = json["results"]?["bindings"]?.AsArray();
            if (bindings is null || bindings.Count == 0)
                return [];

            // Parse each author row into a structured record.
            var authors = new List<(string Qid, string Label, int? Ordinal, string? P31Type)>();

            foreach (var binding in bindings)
            {
                var authorUri = binding?["author"]?["value"]?.GetValue<string>();
                var authorLabel = binding?["authorLabel"]?["value"]?.GetValue<string>();
                var ordinalStr = binding?["ordinal"]?["value"]?.GetValue<string>();
                var instanceOfUri = binding?["instanceOf"]?["value"]?.GetValue<string>();

                if (string.IsNullOrWhiteSpace(authorUri) || string.IsNullOrWhiteSpace(authorLabel))
                    continue;

                var authorQid = ExtractQidFromUri(authorUri);
                if (authorQid is null) continue;

                int? ordinal = null;
                if (!string.IsNullOrWhiteSpace(ordinalStr) && int.TryParse(ordinalStr, out var ord))
                    ordinal = ord;

                var p31Qid = !string.IsNullOrWhiteSpace(instanceOfUri)
                    ? ExtractQidFromUri(instanceOfUri)
                    : null;

                // Deduplicate: a single author can appear multiple times due to
                // multiple P31 values. Keep the most informative classification.
                var existing = authors.FindIndex(a =>
                    string.Equals(a.Qid, authorQid, StringComparison.OrdinalIgnoreCase));

                if (existing >= 0)
                {
                    // Prefer pseudonym/collective classification over plain human.
                    var current = authors[existing];
                    if (p31Qid is QidPseudonym or QidCollectivePseudonym
                        && current.P31Type is not QidPseudonym and not QidCollectivePseudonym)
                    {
                        authors[existing] = (current.Qid, current.Label, current.Ordinal, p31Qid);
                    }
                }
                else
                {
                    authors.Add((authorQid, authorLabel, ordinal, p31Qid));
                }
            }

            if (authors.Count == 0)
                return [];

            // Sort: collective pseudonyms first (pen name priority), then by
            // ordinal (nulls last), then by original Wikidata order.
            var sorted = authors
                .Select((a, idx) => (Author: a, OriginalIndex: idx))
                .OrderByDescending(x => x.Author.P31Type is QidCollectivePseudonym ? 1 : 0)
                .ThenByDescending(x => x.Author.P31Type is QidPseudonym ? 1 : 0)
                .ThenBy(x => x.Author.Ordinal ?? int.MaxValue)
                .ThenBy(x => x.OriginalIndex)
                .Select(x => x.Author)
                .ToList();

            _logger.LogDebug(
                "Wikidata author audit for {Qid}: {Count} authors found, order: [{Authors}]",
                qid, sorted.Count,
                string.Join(", ", sorted.Select(a =>
                    $"{a.Label} ({a.Qid}, P31={a.P31Type ?? "?"}, ord={a.Ordinal?.ToString() ?? "null"})")));

            // Build claims.
            var claims = new List<ProviderClaim>();

            // author: pipe-separated labels in sorted order (first = display author).
            var authorLabels = string.Join(
                WikidataSparqlPropertyMap.MultiValueSeparator,
                sorted.Select(a => a.Label));
            claims.Add(new ProviderClaim("author", authorLabels, 0.9));

            // author_qid: pipe-separated QID::Label pairs in sorted order.
            var authorQids = string.Join(
                WikidataSparqlPropertyMap.MultiValueSeparator,
                sorted.Select(a => $"{a.Qid}::{a.Label}"));
            claims.Add(new ProviderClaim("author_qid", authorQids, 0.9));

            // Flag pseudonym entries.
            var pseudonymFlags = sorted
                .Where(a => a.P31Type is QidPseudonym or QidCollectivePseudonym)
                .Select(a => a.Qid)
                .ToList();
            if (pseudonymFlags.Count > 0)
            {
                claims.Add(new ProviderClaim("author_is_pseudonym",
                    string.Join(WikidataSparqlPropertyMap.MultiValueSeparator, pseudonymFlags), 0.9));
            }

            // For collective pseudonyms, discover constituent members.
            foreach (var author in sorted.Where(a => a.P31Type is QidCollectivePseudonym))
            {
                var members = await FetchCollectiveMembersAsync(
                    sparqlClient, sparqlBaseUrl, author.Qid, throttleGapMs, ct, language)
                    .ConfigureAwait(false);

                if (members.Count > 0)
                {
                    var memberPairs = string.Join(
                        WikidataSparqlPropertyMap.MultiValueSeparator,
                        members.Select(m => $"{m.Qid}::{m.Label}"));
                    claims.Add(new ProviderClaim("collective_members_qid", memberPairs, 0.9));

                    _logger.LogDebug(
                        "Wikidata: collective pseudonym {Qid} ({Label}) has {Count} members: [{Members}]",
                        author.Qid, author.Label, members.Count,
                        string.Join(", ", members.Select(m => m.Label)));
                }
            }

            return claims;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Wikidata author audit failed for QID {Qid}; falling back to standard P50 results", qid);
            return [];
        }
    }

    /// <summary>
    /// Fetches the constituent human members of a collective pseudonym via P527.
    /// </summary>
    private async Task<IReadOnlyList<(string Qid, string Label)>> FetchCollectiveMembersAsync(
        HttpClient sparqlClient,
        string sparqlBaseUrl,
        string collectiveQid,
        int throttleGapMs,
        CancellationToken ct,
        string? language = null)
    {
        try
        {
            var sparql = WikidataSparqlPropertyMap.BuildCollectiveMembersQuery(collectiveQid, language);
            var json = await ThrottledSparqlAsync(sparqlClient, sparqlBaseUrl, sparql, throttleGapMs, ct)
                .ConfigureAwait(false);

            if (json is null) return [];

            var bindings = json["results"]?["bindings"]?.AsArray();
            if (bindings is null || bindings.Count == 0)
                return [];

            var members = new List<(string Qid, string Label)>();
            foreach (var binding in bindings)
            {
                var memberUri = binding?["member"]?["value"]?.GetValue<string>();
                var memberLabel = binding?["memberLabel"]?["value"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(memberUri) || string.IsNullOrWhiteSpace(memberLabel))
                    continue;
                var memberQid = ExtractQidFromUri(memberUri);
                if (memberQid is not null)
                    members.Add((memberQid, memberLabel));
            }
            return members;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Wikidata collective members query failed for QID {Qid}", collectiveQid);
            return [];
        }
    }

    // ── Cast Role Audit with Qualifiers ─────────────────────────────────────

    /// <summary>
    /// Runs the cast role query (qualified statement syntax) to extract P161 cast member
    /// statements with P453 character role qualifiers. Returns structured cast claims
    /// mapping actor names/QIDs to their character names/QIDs.
    /// </summary>
    private async Task<IReadOnlyList<ProviderClaim>> RunCastRoleAuditAsync(
        HttpClient sparqlClient,
        string sparqlBaseUrl,
        string qid,
        int throttleGapMs,
        CancellationToken ct,
        string? language = null)
    {
        try
        {
            var sparql = WikidataSparqlPropertyMap.BuildCastRoleQuery(qid, language);
            var json = await ThrottledSparqlAsync(sparqlClient, sparqlBaseUrl, sparql, throttleGapMs, ct)
                .ConfigureAwait(false);

            if (json is null) return [];

            var bindings = json["results"]?["bindings"]?.AsArray();
            if (bindings is null || bindings.Count == 0)
                return [];

            var castEntries = new List<(string ActorQid, string ActorLabel, string? CharacterQid, string? CharacterLabel)>();

            foreach (var binding in bindings)
            {
                var actorUri = binding?["actor"]?["value"]?.GetValue<string>();
                var actorLabel = binding?["actorLabel"]?["value"]?.GetValue<string>();

                if (string.IsNullOrWhiteSpace(actorUri) || string.IsNullOrWhiteSpace(actorLabel))
                    continue;

                var actorQid = ExtractQidFromUri(actorUri);
                if (actorQid is null) continue;

                var characterUri = binding?["character"]?["value"]?.GetValue<string>();
                var characterLabel = binding?["characterLabel"]?["value"]?.GetValue<string>();

                var characterQid = !string.IsNullOrWhiteSpace(characterUri)
                    ? ExtractQidFromUri(characterUri)
                    : null;

                // Deduplicate: keep first occurrence per actor QID.
                if (castEntries.FindIndex(e =>
                        string.Equals(e.ActorQid, actorQid, StringComparison.OrdinalIgnoreCase)) < 0)
                {
                    castEntries.Add((actorQid, actorLabel, characterQid, characterLabel));
                }
            }

            if (castEntries.Count == 0)
                return [];

            _logger.LogDebug(
                "Wikidata cast role audit for {Qid}: {Count} cast entries found",
                qid, castEntries.Count);

            var claims = new List<ProviderClaim>();

            // cast_role: "ActorName::CharacterName|||ActorName2::CharacterName2"
            var castRoleValue = string.Join(
                WikidataSparqlPropertyMap.MultiValueSeparator,
                castEntries.Select(e =>
                    $"{e.ActorLabel}::{(string.IsNullOrWhiteSpace(e.CharacterLabel) ? "Unknown" : e.CharacterLabel)}"));
            claims.Add(new ProviderClaim("cast_role", castRoleValue, 0.9));

            // cast_role_qid: "Q123::Q456|||Q789::Q012"
            var castRoleQidValue = string.Join(
                WikidataSparqlPropertyMap.MultiValueSeparator,
                castEntries.Select(e =>
                    $"{e.ActorQid}::{e.CharacterQid ?? string.Empty}"));
            claims.Add(new ProviderClaim("cast_role_qid", castRoleQidValue, 0.9));

            return claims;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Wikidata cast role audit failed for QID {Qid}; falling back to standard P161 results", qid);
            return [];
        }
    }

    // ── Awards Query with Preferred Rank ────────────────────────────────────

    /// <summary>
    /// Runs the awards query (qualified statement syntax with PreferredRank filter)
    /// and returns a single multi-valued <c>awards_received</c> claim joining all
    /// award names (with year where available) using the <see cref="WikidataSparqlPropertyMap.MultiValueSeparator"/>.
    /// Only preferred-rank statements are fetched — this filters winners from nominees.
    /// Returns an empty list when no awards are found.
    /// </summary>
    private async Task<IReadOnlyList<ProviderClaim>> RunAwardsQueryAsync(
        HttpClient sparqlClient,
        string sparqlBaseUrl,
        string qid,
        int throttleGapMs,
        CancellationToken ct,
        string? language = null)
    {
        try
        {
            var sparql = WikidataSparqlPropertyMap.BuildAwardsQuery(qid, language ?? "en");
            var json = await ThrottledSparqlAsync(sparqlClient, sparqlBaseUrl, sparql, throttleGapMs, ct)
                .ConfigureAwait(false);

            if (json is null) return [];

            var bindings = json["results"]?["bindings"]?.AsArray();
            if (bindings is null || bindings.Count == 0)
                return [];

            var awardEntries = new List<string>();

            foreach (var binding in bindings)
            {
                var awardLabel = binding?["awardLabel"]?["value"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(awardLabel))
                    continue;

                var pointInTime = binding?["pointInTime"]?["value"]?.GetValue<string>();
                string? year = null;
                if (!string.IsNullOrWhiteSpace(pointInTime) && pointInTime.Length >= 4)
                    year = pointInTime.Substring(0, 4);

                var entry = year is not null
                    ? $"{awardLabel} ({year})"
                    : awardLabel;

                awardEntries.Add(entry);
            }

            if (awardEntries.Count == 0)
                return [];

            _logger.LogDebug(
                "Wikidata awards query for {Qid}: {Count} awards found",
                qid, awardEntries.Count);

            var awardsValue = string.Join(WikidataSparqlPropertyMap.MultiValueSeparator, awardEntries);
            return [new ProviderClaim("awards_received", awardsValue, 0.85)];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Wikidata awards query failed for QID {Qid}; skipping awards data", qid);
            return [];
        }
    }

    // ── QID Resolution: Bridge IDs ──────────────────────────────────────────

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

    // ── QID Resolution: Tier 2 Structured SPARQL Search ───────────────────────

    /// <summary>
    /// Tier 2: Structured SPARQL search — searches by label with instance_of filtering
    /// and optional author/year cross-validation. Returns a QID and confidence score.
    /// </summary>
    /// <returns>
    /// A tuple of (QID, confidence, matchSource). All null/0 if no match found.
    /// </returns>
    private async Task<(string? Qid, double Confidence, string? MatchSource)> ResolveQidViaStructuredSearchAsync(
        HttpClient sparqlClient,
        ProviderLookupRequest request,
        UniverseConfiguration universeConfig,
        int throttleGapMs,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.SparqlBaseUrl) ||
            string.IsNullOrWhiteSpace(request.Title))
            return (null, 0, null);

        var mediaTypeKey = MediaTypeToConfigKey(request.MediaType);
        var thresholds = universeConfig.SearchThresholds;

        var sparql = WikidataSparqlPropertyMap.BuildStructuredSearchQuery(
            request.Title,
            mediaTypeKey,
            universeConfig.InstanceOfClasses,
            request.Author,
            year: null, // Year is extracted from results, not used as a filter input
            request.Language);

        if (string.IsNullOrEmpty(sparql))
        {
            _logger.LogDebug(
                "Wikidata Tier 2: no instance_of classes configured for media type {MediaType}",
                mediaTypeKey);
            return (null, 0, null);
        }

        _logger.LogInformation(
            "Wikidata Tier 2: structured SPARQL search for \"{Title}\" (type={MediaType}) for entity {Id}",
            request.Title, mediaTypeKey, request.EntityId);

        var json = await ThrottledSparqlAsync(sparqlClient, request.SparqlBaseUrl, sparql, throttleGapMs, ct)
            .ConfigureAwait(false);
        if (json is null)
            return (null, 0, null);

        var bindings = json["results"]?["bindings"]?.AsArray();
        if (bindings is null || bindings.Count == 0)
        {
            _logger.LogInformation(
                "Wikidata Tier 2: no results for \"{Title}\" with {MediaType} instance_of filter",
                request.Title, mediaTypeKey);
            return (null, 0, null);
        }

        _logger.LogDebug(
            "Wikidata Tier 2: {Count} candidate(s) for \"{Title}\"",
            bindings.Count, request.Title);

        // Score each candidate.
        string? bestQid = null;
        double bestConfidence = 0;

        foreach (var binding in bindings)
        {
            if (binding is null) continue;

            var itemUri = binding["item"]?["value"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(itemUri)) continue;

            var lastSlash = itemUri.LastIndexOf('/');
            var candidateQid = lastSlash >= 0 ? itemUri[(lastSlash + 1)..] : itemUri;

            // Base score for Tier 2 (passed instance_of check).
            double confidence = 0.50;

            // Author match bonus.
            if (!string.IsNullOrWhiteSpace(request.Author))
            {
                var authorLabel = binding["authorLabel"]?["value"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(authorLabel) &&
                    authorLabel.Contains(request.Author, StringComparison.OrdinalIgnoreCase))
                {
                    confidence += thresholds.AuthorMatchBonus;
                }
            }

            // Year match bonus.
            var yearValue = binding["year"]?["value"]?.GetValue<string>();
            // If we have a year from the request, compare; otherwise check if result has year at all.
            if (!string.IsNullOrWhiteSpace(yearValue) && yearValue.Length >= 4)
            {
                confidence += thresholds.YearMatchBonus;
            }

            // Exact label bonus (the SPARQL query already filters by exact label, so this is always true).
            confidence += thresholds.ExactLabelBonus;

            // Single result bonus.
            if (bindings.Count == 1)
                confidence += thresholds.SingleResultBonus;

            _logger.LogDebug(
                "Wikidata Tier 2 candidate: {Qid} confidence={Confidence:F2}",
                candidateQid, confidence);

            if (confidence > bestConfidence)
            {
                bestConfidence = confidence;
                bestQid = candidateQid;
            }
        }

        if (bestQid is null)
            return (null, 0, null);

        // Conservative rule: multiple results without author confirmation → review queue.
        // Return the best candidate but let the caller decide based on confidence thresholds.
        _logger.LogInformation(
            "Wikidata Tier 2: selected {Qid} with confidence {Confidence:F2} for \"{Title}\" (entity {Id})",
            bestQid, bestConfidence, request.Title, request.EntityId);

        return (bestQid, bestConfidence, "structured_sparql");
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

    /// <summary>
    /// Maps a <see cref="MediaType"/> enum value to the key used in the
    /// <c>instance_of_classes</c> configuration dictionary. Handles the
    /// singular/plural mismatch (e.g. <c>Comic</c> → <c>"Comics"</c>).
    /// </summary>
    private static string MediaTypeToConfigKey(MediaType mediaType)
        => mediaType switch
        {
            MediaType.Comic => "Comics",
            _               => mediaType.ToString(),
        };

    /// <summary>Strips the "urn:isbn:" URI prefix from an ISBN if present.</summary>
    private static string? StripIsbnPrefix(string? isbn)
        => isbn?.StartsWith("urn:isbn:", StringComparison.OrdinalIgnoreCase) == true
            ? isbn[9..]
            : isbn;

    /// <summary>
    /// Executes a bridge lookup SPARQL query and returns the QID if found.
    /// </summary>
    private async Task<string?> RunBridgeLookupAsync(
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
        MediaType mediaType = MediaType.Unknown,
        HttpClient? sparqlClient = null,
        string? sparqlBaseUrl = null,
        string? fileAuthor = null)
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

        // ── Principle 3: SPARQL candidate enrichment ─────────────────────────
        // If SPARQL is available and we have candidates, fetch basic properties
        // (author, year, instance_of) for the top candidates and re-score against
        // file metadata for better match quality.
        if (sparqlClient is not null && !string.IsNullOrWhiteSpace(sparqlBaseUrl)
            && results.Count > 1)
        {
            var candidateQids = new List<string>();
            foreach (var r in results)
            {
                var id = r?["id"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(id))
                    candidateQids.Add(id!);
            }

            if (candidateQids.Count > 0)
            {
                var enrichedBest = await ScoreCandidatesViaSparqlAsync(
                    sparqlClient, sparqlBaseUrl, candidateQids.Take(5).ToList(),
                    title, fileAuthor, mediaType, throttleGapMs, ct)
                    .ConfigureAwait(false);

                if (enrichedBest is not null)
                {
                    _logger.LogInformation(
                        "Wikidata Tier 3: SPARQL enrichment overrode description-based pick: {OldQid} → {NewQid}",
                        bestId, enrichedBest);
                    return enrichedBest;
                }
            }
        }

        return string.IsNullOrWhiteSpace(bestId) ? null : bestId;
    }

    /// <summary>
    /// Principle 3: Fetches basic SPARQL properties (author/creator, year, instance_of)
    /// for a list of candidate QIDs and scores each against the file's metadata.
    /// Returns the best-scoring QID, or null if SPARQL enrichment didn't produce a clear winner.
    /// </summary>
    private async Task<string?> ScoreCandidatesViaSparqlAsync(
        HttpClient sparqlClient,
        string sparqlBaseUrl,
        IReadOnlyList<string> candidateQids,
        string fileTitle,
        string? fileAuthor,
        MediaType mediaType,
        int throttleGapMs,
        CancellationToken ct)
    {
        if (candidateQids.Count == 0)
            return null;

        // Build a single batch SPARQL query for all candidates.
        var values = string.Join(" ", candidateQids.Select(q => $"wd:{q}"));
        var sparql = $@"
SELECT ?item ?authorLabel ?date ?instanceOf WHERE {{
  VALUES ?item {{ {values} }}
  OPTIONAL {{ ?item wdt:P50|wdt:P57|wdt:P175 ?author . }}
  OPTIONAL {{ ?item wdt:P577 ?date . }}
  ?item wdt:P31 ?instanceOf .
  SERVICE wikibase:label {{ bd:serviceParam wikibase:language ""en"" }}
}}";

        _logger.LogDebug("Wikidata Tier 3: SPARQL enrichment for {Count} candidates", candidateQids.Count);

        var json = await ThrottledSparqlAsync(sparqlClient, sparqlBaseUrl, sparql, throttleGapMs, ct)
            .ConfigureAwait(false);
        if (json is null)
            return null;

        var bindings = json["results"]?["bindings"]?.AsArray();
        if (bindings is null || bindings.Count == 0)
            return null;

        // Group bindings by QID and score each.
        var candidateData = new Dictionary<string, (string? Author, string? Year, HashSet<string> InstanceOf)>();
        foreach (var binding in bindings)
        {
            if (binding is null) continue;
            var itemUri = binding["item"]?["value"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(itemUri)) continue;

            var lastSlash = itemUri.LastIndexOf('/');
            var qid = lastSlash >= 0 ? itemUri[(lastSlash + 1)..] : itemUri;

            if (!candidateData.TryGetValue(qid, out var data))
                data = (null, null, new HashSet<string>());

            var authorLabel = binding["authorLabel"]?["value"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(authorLabel))
                data.Author = authorLabel;

            var dateVal = binding["date"]?["value"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(dateVal) && dateVal.Length >= 4)
                data.Year = dateVal[..4];

            var instanceOfUri = binding["instanceOf"]?["value"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(instanceOfUri))
            {
                var ioSlash = instanceOfUri.LastIndexOf('/');
                data.InstanceOf.Add(ioSlash >= 0 ? instanceOfUri[(ioSlash + 1)..] : instanceOfUri);
            }

            candidateData[qid] = data;
        }

        if (candidateData.Count == 0)
            return null;

        // Get expected instance_of classes for this media type.
        var expectedClasses = GetExpectedInstanceOfClasses(mediaType);

        // Score each candidate: title similarity 0.50, creator match 0.25, year match 0.15, instance_of 0.10
        string? bestQid = null;
        double bestScore = 0;

        foreach (var (qid, data) in candidateData)
        {
            double score = 0;

            // Instance_of match (0.10 weight).
            if (expectedClasses.Count > 0 && data.InstanceOf.Overlaps(expectedClasses))
                score += 0.10;

            // Creator match (0.25 weight).
            if (!string.IsNullOrWhiteSpace(fileAuthor) && !string.IsNullOrWhiteSpace(data.Author)
                && data.Author.Contains(fileAuthor, StringComparison.OrdinalIgnoreCase))
                score += 0.25;

            // Year presence gives a small boost (0.15 weight — we don't compare since we
            // may not have a year from the file, but having one is a good signal).
            if (!string.IsNullOrWhiteSpace(data.Year))
                score += 0.15;

            // Base title match (already matched by wbsearchentities) gets 0.50.
            score += 0.50;

            _logger.LogDebug(
                "Wikidata Tier 3 SPARQL candidate: {Qid} score={Score:F2} (author={Author}, year={Year}, instanceOf={Types})",
                qid, score, data.Author ?? "?", data.Year ?? "?", string.Join(",", data.InstanceOf));

            if (score > bestScore)
            {
                bestScore = score;
                bestQid = qid;
            }
        }

        return bestQid;
    }

    /// <summary>
    /// Returns the Wikidata instance_of (P31) QIDs expected for a media type.
    /// Used by Tier 3 SPARQL candidate scoring.
    /// </summary>
    private static HashSet<string> GetExpectedInstanceOfClasses(MediaType mediaType) => mediaType switch
    {
        MediaType.Books      => ["Q7725634", "Q571", "Q8261", "Q47461344", "Q277759"],
        MediaType.Audiobooks => ["Q106833962", "Q7725634", "Q571", "Q8261", "Q47461344"],
        MediaType.Movies     => ["Q11424", "Q24869", "Q24862"],
        MediaType.TV         => ["Q5398426", "Q581714", "Q21191270"],
        MediaType.Music      => ["Q482994", "Q134556", "Q208569"],
        MediaType.Comic      => ["Q1004", "Q838795", "Q21198342"],
        MediaType.Podcasts   => ["Q24634210"],
        _                    => [],
    };

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
    ///
    /// Checks the provider response cache before making an HTTP call.
    /// On cache HIT, the HTTP call is skipped entirely. On cache MISS,
    /// the response is stored for reuse. Expired entries with ETags
    /// trigger conditional revalidation (304 Not Modified).
    /// </summary>
    private async Task<JsonObject?> ThrottledSparqlAsync(
        HttpClient sparqlClient,
        string sparqlBaseUrl,
        string sparql,
        int throttleGapMs,
        CancellationToken ct)
    {
        // ── Response cache check ──────────────────────────────────────────────
        var queryHash = ComputeSha256(sparql);
        var cacheKey  = $"wikidata-sparql-{queryHash}";
        var providerConfig = _configLoader.LoadProvider("wikidata");
        var cacheTtlHours  = providerConfig?.CacheTtlHours ?? DefaultCacheTtlHours;

        var cached = await _cacheRepo.FindAsync(cacheKey, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            _logger.LogDebug("Wikidata SPARQL cache HIT for key {CacheKey}", cacheKey);
            return JsonSerializer.Deserialize<JsonObject>(cached.ResponseJson, _jsonOptions);
        }

        // ── Throttled HTTP call ───────────────────────────────────────────────
        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastCallUtc).TotalMilliseconds;
            if (elapsed < throttleGapMs)
                await Task.Delay(TimeSpan.FromMilliseconds(throttleGapMs - elapsed), ct)
                    .ConfigureAwait(false);

            // ETag conditional revalidation for expired entries.
            var existingEtag = await _cacheRepo.FindExpiredEtagAsync(cacheKey, ct)
                .ConfigureAwait(false);

            // Use POST to avoid HTTP 414 (URI Too Long) on large SPARQL queries.
            // Wikidata's SPARQL endpoint supports both GET and POST; POST has no URL length limit.
            using var request = new HttpRequestMessage(HttpMethod.Post, sparqlBaseUrl.TrimEnd('/'));
            request.Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("query", sparql),
            ]);
            request.Headers.Accept.ParseAdd("application/sparql-results+json");

            if (!string.IsNullOrEmpty(existingEtag))
                request.Headers.IfNoneMatch.Add(
                    new System.Net.Http.Headers.EntityTagHeaderValue($"\"{existingEtag}\""));

            var response = await sparqlClient.SendAsync(request, ct).ConfigureAwait(false);
            _lastCallUtc = DateTime.UtcNow;

            // ETag 304: cache is still valid — refresh expiry and use it.
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                _logger.LogDebug(
                    "Wikidata SPARQL 304 Not Modified — refreshing cache for {CacheKey}", cacheKey);
                await _cacheRepo.RefreshExpiryAsync(cacheKey, cacheTtlHours, ct)
                    .ConfigureAwait(false);

                var refreshed = await _cacheRepo.FindAsync(cacheKey, ct).ConfigureAwait(false);
                return refreshed is not null
                    ? JsonSerializer.Deserialize<JsonObject>(refreshed.ResponseJson, _jsonOptions)
                    : null;
            }

            if (!response.IsSuccessStatusCode)
                return null;

            var responseBody = await response.Content.ReadAsStringAsync(ct)
                .ConfigureAwait(false);

            // Cache the response.
            if (!string.IsNullOrEmpty(responseBody))
            {
                var etag = response.Headers.ETag?.Tag?.Trim('"');
                await _cacheRepo.UpsertAsync(
                    cacheKey, AdapterProviderId.ToString(), queryHash,
                    responseBody, etag, cacheTtlHours, ct)
                    .ConfigureAwait(false);
            }

            return JsonSerializer.Deserialize<JsonObject>(responseBody, _jsonOptions);
        }
        finally
        {
            _throttle.Release();
        }
    }

    /// <summary>
    /// Computes a SHA-256 hash of the input string (for cache key dedup).
    /// </summary>
    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Computes the resolver cache key from a title and media type.
    /// Uses SHA-256 of the normalized (lowercased, trimmed) title + media type string.
    /// </summary>
    private static string ComputeResolverCacheKey(string title, MediaType mediaType)
        => ComputeSha256($"{title.Trim().ToLowerInvariant()}|{mediaType}");

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
