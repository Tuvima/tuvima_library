using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Contracts;
using MediaEngine.Domain.Services;
using MediaEngine.Storage.Models;
using Tuvima.Wikidata;

namespace MediaEngine.Providers.Adapters;

/// <summary>
/// Wikidata adapter using the <see cref="WikidataReconciler"/> from the Tuvima.Wikidata library.
///
/// <para>
/// This adapter replaces the SPARQL-based WikidataAdapter. Instead of custom SPARQL queries
/// it uses <see cref="WikidataReconciler.ReconcileAsync"/> for entity search and
/// <see cref="WikidataReconciler.GetPropertiesAsync"/> for property extension.
/// </para>
///
/// <para>
/// Primary operations:
/// <list type="bullet">
///   <item>Reconcile entity names to Wikidata QIDs via the Wikibase wbsearchentities API.</item>
///   <item>Extend QIDs with structured property values via the Wikibase wbgetentities API.</item>
///   <item>Filter candidates by media type using P31 (instance_of) + P279 (subclass_of) walks.</item>
///   <item>Discover audiobook editions via P747 (has_edition_or_translation) + P31 filtering.</item>
///   <item>Download person headshots from Wikimedia Commons.</item>
/// </list>
/// </para>
///
/// Spec: Phase 2 – ReconciliationAdapter replacing WikidataAdapter.
/// </summary>
public sealed class ReconciliationAdapter : IExternalMetadataProvider
{
    private readonly ReconciliationProviderConfig _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ReconciliationAdapter> _logger;
    private readonly IProviderResponseCacheRepository? _responseCache;
    private readonly IConfigurationLoader? _configLoader;
    private readonly IFuzzyMatchingService _fuzzy;
    private readonly WikidataReconciler? _reconciler;

    // Parsed once at construction.
    private readonly Guid _providerId;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    public ReconciliationAdapter(
        ReconciliationProviderConfig config,
        IHttpClientFactory httpFactory,
        ILogger<ReconciliationAdapter> logger,
        IFuzzyMatchingService fuzzy,
        IProviderResponseCacheRepository? responseCache = null,
        IConfigurationLoader? configLoader = null,
        WikidataReconciler? reconciler = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(fuzzy);

        _config        = config;
        _httpFactory   = httpFactory;
        _logger        = logger;
        _fuzzy         = fuzzy;
        _responseCache = responseCache;
        _configLoader  = configLoader;
        _reconciler    = reconciler;

        _providerId = !string.IsNullOrEmpty(config.ProviderId)
            ? Guid.Parse(config.ProviderId)
            : Guid.NewGuid();
    }

    // ── IExternalMetadataProvider ─────────────────────────────────────────────

    public string Name => _config.Name;

    public ProviderDomain Domain => ProviderDomain.Universal;

    public IReadOnlyList<string> CapabilityTags =>
        _config.DataExtension.PropertyLabels.Values.Distinct().ToList();

    public Guid ProviderId => _providerId;

    /// <summary>Universal provider: handles all media types.</summary>
    public bool CanHandle(MediaType mediaType) => true;

    /// <summary>Handles MediaAsset and Person entity types.</summary>
    public bool CanHandle(EntityType entityType) =>
        entityType is EntityType.MediaAsset or EntityType.Person;

    /// <summary>
    /// Fetches metadata claims by reconciling the entity against Wikidata and
    /// extending the resolved QID with structured property values.
    ///
    /// For Person entities: reconciles by person name with occupation/notable-work constraints.
    /// For MediaAsset entities: reconciles by title with author constraint, then extends.
    ///
    /// All exceptions are caught and an empty list is returned on failure.
    /// </summary>
    public async Task<IReadOnlyList<ProviderClaim>> FetchAsync(
        ProviderLookupRequest request,
        CancellationToken ct = default)
    {
        if (!CanHandle(request.EntityType))
            return [];

        try
        {
            return request.EntityType == EntityType.Person
                ? await FetchPersonAsync(request, ct).ConfigureAwait(false)
                : await FetchWorkAsync(request, ct).ConfigureAwait(false);
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

    /// <summary>
    /// Searches Wikidata via the Reconciliation API and returns multiple result candidates
    /// for user selection in the Needs Review resolution panel.
    /// </summary>
    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        ProviderLookupRequest request,
        int limit = 25,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return [];

        try
        {
            var constraints = BuildTitleSearchConstraints(request);

            // Use the shared ReconcileAsync which applies type filtering via CirrusSearch
            var candidates = await ReconcileAsync(
                request.Title, constraints, ct, request.MediaType).ConfigureAwait(false);

            _logger.LogInformation(
                "{Provider}: SearchAsync '{Query}' ({MediaType}) — {Count} reconciliation candidate(s)",
                Name, request.Title, request.MediaType, candidates.Count);

            if (candidates.Count == 0)
                return [];

            // Optionally filter by media type using P31, with title/author/ISBN hints
            // for composite scoring (boosts candidates with matching metadata).
            if (request.MediaType != MediaType.Unknown)
            {
                var filtered = await FilterByMediaTypeAsync(
                    candidates, request.MediaType, ct,
                    request.Title, request.Author, request.Isbn).ConfigureAwait(false);

                // For audiobooks: if audiobook-specific filtering eliminates everything,
                // fall back to Books classes (an audiobook is a format of a literary work).
                if (filtered.Count == 0 && request.MediaType == MediaType.Audiobooks)
                {
                    _logger.LogDebug("{Provider}: audiobook filter returned 0 results, falling back to Books classes",
                        Name);
                    filtered = await FilterByMediaTypeAsync(
                        candidates, MediaType.Books, ct,
                        request.Title, request.Author, request.Isbn).ConfigureAwait(false);
                }

                // Strict filtering: only return candidates that positively match the
                // expected instance_of classes. If nothing matches, return empty — the
                // user gets 0 results, which is correct (no book match found).
                _logger.LogInformation(
                    "{Provider}: SearchAsync type filter ({MediaType}) — {Kept}/{Total} candidates survived",
                    Name, request.MediaType, filtered.Count, candidates.Count);
                candidates = filtered;
            }

            // Display-friendly titles: the library's MatchedLabel on each ReconciliationResult
            // already contains the alias or sitelink that matched — no additional call needed.

            // For audiobook searches: discover audiobook editions via P747 for work-level results.
            // Edition results go first (they're more specific), work fallbacks come after.
            if (request.MediaType == MediaType.Audiobooks)
            {
                var editionResults = new List<SearchResultItem>();
                var workResults    = new List<SearchResultItem>();

                foreach (var c in candidates.Take(limit))
                {
                    // Try to discover audiobook editions for this candidate.
                    var editions = await DiscoverAudiobookEditionsAsync(c.Id, null, ct)
                        .ConfigureAwait(false);

                    if (editions.Count > 0)
                    {
                        foreach (var ed in editions)
                        {
                            // Build a rich description with audiobook-specific details.
                            var parts = new List<string>();
                            if (!string.IsNullOrEmpty(ed.Narrator))
                                parts.Add($"Narrated by {ed.Narrator}");
                            if (!string.IsNullOrEmpty(ed.Duration))
                                parts.Add($"Duration: {ed.Duration}");
                            if (!string.IsNullOrEmpty(ed.Publisher))
                                parts.Add($"Publisher: {ed.Publisher}");
                            if (!string.IsNullOrEmpty(c.Description))
                                parts.Add(c.Description);

                            var editionDesc = parts.Count > 0
                                ? string.Join(" · ", parts)
                                : c.Description;

                            editionResults.Add(new SearchResultItem(
                                Title:          c.Name,
                                Author:         null,
                                Description:    editionDesc,
                                Year:           null,
                                ThumbnailUrl:   null,
                                ProviderItemId: ed.EditionQid ?? c.Id,
                                Confidence:     c.Score / 100.0,
                                ProviderName:   Name,
                                ResultType:     "audiobook_edition"));
                        }
                    }

                    // Always include the work as a fallback.
                    workResults.Add(new SearchResultItem(
                        Title:          c.Name,
                        Author:         null,
                        Description:    c.Description,
                        Year:           null,
                        ThumbnailUrl:   null,
                        ProviderItemId: c.Id,
                        Confidence:     c.Score / 100.0,
                        ProviderName:   Name,
                        ResultType:     "work"));
                }

                // Editions first, then work fallbacks.
                var combined = editionResults.Concat(workResults).Take(limit).ToList();
                return combined.Count > 0 ? combined : workResults.Take(limit).ToList();
            }

            return candidates
                .Take(limit)
                .Select(c => new SearchResultItem(
                    Title:          c.Name,
                    Author:         null,
                    Description:    c.Description,
                    Year:           null,
                    ThumbnailUrl:   null,
                    ProviderItemId: c.Id,
                    Confidence:     c.Score / 100.0,
                    ProviderName:   Name))
                .ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Provider}: SearchAsync failed", Name);
            return [];
        }
    }

    // ── Public direct-call methods (used by the hydration pipeline) ───────────

    /// <summary>
    /// Reconciles a single query string to Wikidata candidates.
    /// Returns up to <c>config.reconciliation.max_candidates</c> results.
    /// When <paramref name="mediaType"/> is specified, CirrusSearch pre-filters by
    /// the configured <c>instance_of_classes</c> for that media type — the same logic
    /// used by <see cref="SearchAsync"/> for manual searches.
    /// </summary>
    /// <param name="query">The entity name to reconcile (e.g. "Dune", "Frank Herbert").</param>
    /// <param name="propertyConstraints">Optional P-code → value constraints to narrow the search.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="mediaType">Media type for CirrusSearch type pre-filtering (default: Unknown = no filter).</param>
    public async Task<IReadOnlyList<ReconciliationResult>> ReconcileAsync(
        string query,
        Dictionary<string, string>? propertyConstraints = null,
        CancellationToken ct = default,
        MediaType mediaType = MediaType.Unknown,
        List<PropertyConstraint>? multiValueConstraints = null)
    {
        if (_reconciler is null || string.IsNullOrWhiteSpace(query))
            return [];

        var language = _configLoader?.LoadCore().Language.Metadata ?? "en";

        // Build type filter from instance_of_classes config (same as SearchAsync)
        IReadOnlyList<string>? typeQids = null;
        if (mediaType != MediaType.Unknown)
        {
            var mediaTypeKey = mediaType.ToString();
            if (_config.InstanceOfClasses.TryGetValue(mediaTypeKey, out var classes) && classes.Count > 0)
                typeQids = classes;
        }

        // Build property constraints — multi-value constraints (v0.10.0 Values property)
        // take precedence over single-value string pairs for the same property.
        var allConstraints = new List<PropertyConstraint>();
        if (multiValueConstraints is { Count: > 0 })
            allConstraints.AddRange(multiValueConstraints);
        if (propertyConstraints is not null)
        {
            var multiValuePIds = multiValueConstraints?.Select(c => c.PropertyId).ToHashSet() ?? [];
            allConstraints.AddRange(
                propertyConstraints
                    .Where(kvp => !multiValuePIds.Contains(kvp.Key))
                    .Select(kvp => new PropertyConstraint { PropertyId = kvp.Key, Value = kvp.Value }));
        }

        var request = new ReconciliationRequest
        {
            Query = query,
            Limit = _config.Reconciliation.MaxCandidates,
            Language = language,
            DiacriticInsensitive = true,
            Cleaners = QueryCleaners.All(),
            Types = typeQids,
            TypeHierarchyDepth = 1,
            Properties = allConstraints.Count > 0 ? allConstraints : null
        };

        try
        {
            return await _reconciler.ReconcileAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Provider}: ReconcileAsync failed for query '{Query}'", Name, query);
            return [];
        }
    }

    /// <summary>
    /// Reconciles an entity name against Wikidata, searching in multiple languages concurrently.
    /// When <paramref name="fileLanguage"/> differs from the configured metadata language,
    /// uses the library's <c>Languages</c> parameter for concurrent multi-language search
    /// with built-in deduplication.
    /// When <paramref name="mediaType"/> is specified, CirrusSearch pre-filters by
    /// the configured <c>instance_of_classes</c> for that media type.
    /// </summary>
    public async Task<IReadOnlyList<ReconciliationResult>> ReconcileMultiLanguageAsync(
        string query,
        string? fileLanguage,
        Dictionary<string, string>? propertyConstraints = null,
        CancellationToken ct = default,
        MediaType mediaType = MediaType.Unknown)
    {
        if (_reconciler is null || string.IsNullOrWhiteSpace(query))
            return [];

        var metadataLanguage = _configLoader?.LoadCore().Language.Metadata ?? "en";
        var fileLang = fileLanguage?.Split('-', '_')[0].ToLowerInvariant().Trim();
        var metaLang = metadataLanguage.Split('-', '_')[0].ToLowerInvariant().Trim();

        // Build language list: file language first, then metadata language
        var languages = new List<string>();
        if (!string.IsNullOrEmpty(fileLang))
            languages.Add(fileLang);
        if (!string.Equals(fileLang, metaLang, StringComparison.OrdinalIgnoreCase))
            languages.Add(metaLang);

        // Build type filter from instance_of_classes config (same as ReconcileAsync)
        IReadOnlyList<string>? typeQids = null;
        if (mediaType != MediaType.Unknown)
        {
            var mediaTypeKey = mediaType.ToString();
            if (_config.InstanceOfClasses.TryGetValue(mediaTypeKey, out var classes) && classes.Count > 0)
                typeQids = classes;
        }

        var request = new ReconciliationRequest
        {
            Query = query,
            Limit = _config.Reconciliation.MaxCandidates,
            Languages = languages.Count > 1 ? languages : null,
            Language = languages.Count <= 1 ? (fileLang ?? metaLang) : null,
            DiacriticInsensitive = true,
            Types = typeQids,
            TypeHierarchyDepth = 1,
            Properties = propertyConstraints?.Select(kvp =>
                new PropertyConstraint { PropertyId = kvp.Key, Value = kvp.Value }).ToList()
        };

        try
        {
            return await _reconciler.ReconcileAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Provider}: ReconcileMultiLanguageAsync failed for query '{Query}'", Name, query);
            return [];
        }
    }

    /// <summary>
    /// Reconciles multiple entities in parallel using the library's batch method.
    /// </summary>
    /// <param name="requests">List of (QueryId, Query, PropertyConstraints) tuples.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Dictionary keyed by QueryId.</returns>
    public async Task<Dictionary<string, IReadOnlyList<ReconciliationResult>>> ReconcileBatchAsync(
        IReadOnlyList<(string QueryId, string Query, Dictionary<string, string>? PropertyConstraints)> requests,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, IReadOnlyList<ReconciliationResult>>(StringComparer.Ordinal);
        if (requests.Count == 0 || _reconciler is null)
            return result;

        var language = _configLoader?.LoadCore().Language.Metadata ?? "en";
        var libRequests = requests.Select(r => new ReconciliationRequest
        {
            Query = r.Query,
            Limit = _config.Reconciliation.MaxCandidates,
            Language = language,
            DiacriticInsensitive = true,
            Cleaners = QueryCleaners.All(),
            Properties = r.PropertyConstraints?.Select(kvp =>
                new PropertyConstraint { PropertyId = kvp.Key, Value = kvp.Value }).ToList()
        }).ToList();

        try
        {
            var batchResults = await _reconciler.ReconcileBatchAsync(libRequests, ct).ConfigureAwait(false);
            for (int i = 0; i < requests.Count && i < batchResults.Count; i++)
                result[requests[i].QueryId] = batchResults[i];
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Provider}: ReconcileBatchAsync failed", Name);
        }

        return result;
    }

    /// <summary>
    /// Extends a set of QIDs with property values via the Data Extension API.
    /// </summary>
    /// <param name="qids">Wikidata Q-identifiers to extend.</param>
    /// <param name="propertyCodes">P-codes to fetch (e.g. ["P50", "P577"]).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>>>> ExtendAsync(
        IReadOnlyList<string> qids,
        IReadOnlyList<string> propertyCodes,
        CancellationToken ct = default)
    {
        if (qids.Count == 0 || propertyCodes.Count == 0)
            return new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>>>(StringComparer.OrdinalIgnoreCase);

        if (_reconciler is null)
        {
            _logger.LogWarning("{Provider}: WikidataReconciler not available — cannot extend", Name);
            return new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>>>(StringComparer.OrdinalIgnoreCase);
        }

        var language = _configLoader?.LoadCore().Language.Metadata ?? "en";

        // Build a cache key from qids + properties + language.
        var cacheInput = $"extend-direct:{language}:{string.Join(",", qids)}:{string.Join(",", propertyCodes)}";
        var cacheKey = BuildCacheKey(cacheInput);

        // Check cache first.
        if (_responseCache is not null)
        {
            var cached = await _responseCache.FindAsync(cacheKey, ct).ConfigureAwait(false);
            if (cached is not null)
            {
                _logger.LogDebug("{Provider}: extend cache HIT", Name);
                var deserialized = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<WikidataClaim>>>>(cached.ResponseJson, JsonOpts);
                if (deserialized is not null)
                {
                    return deserialized.ToDictionary(
                        kvp => kvp.Key,
                        kvp => (IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>>)kvp.Value.ToDictionary(
                            p => p.Key,
                            p => (IReadOnlyList<WikidataClaim>)p.Value,
                            StringComparer.OrdinalIgnoreCase),
                        StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        try
        {
            var results = await _reconciler.GetPropertiesAsync(
                qids, propertyCodes, language, ct).ConfigureAwait(false);

            // Cache the results.
            if (_responseCache is not null && results.Count > 0)
            {
                var json = JsonSerializer.Serialize(results, JsonOpts);
                var queryHash = ComputeSha256(cacheInput);
                await _responseCache.UpsertAsync(
                    cacheKey, _providerId.ToString(), queryHash,
                    json, null, _config.CacheTtlHours, ct).ConfigureAwait(false);
            }

            return results;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Provider}: GetPropertiesAsync failed for {Count} QIDs",
                Name, qids.Count);
            return new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// Filters reconciliation candidates by media type using P31 (instance_of) lookups.
    /// Walks P279 (subclass_of) up to 3 levels for unknown classes, caching learned mappings.
    /// Candidates with no P31 data that match any expected class are retained.
    /// </summary>
    public async Task<IReadOnlyList<ReconciliationResult>> FilterByMediaTypeAsync(
        IReadOnlyList<ReconciliationResult> candidates,
        MediaType mediaType,
        CancellationToken ct = default,
        string? titleHint = null,
        string? authorHint = null,
        string? isbnHint = null)
    {
        if (candidates.Count == 0)
            return candidates;

        var mediaTypeKey = mediaType.ToString();
        if (!_config.InstanceOfClasses.TryGetValue(mediaTypeKey, out var expectedClasses)
            || expectedClasses.Count == 0)
        {
            _logger.LogDebug("{Provider}: no instance_of classes configured for {MediaType}, skipping filter",
                Name, mediaTypeKey);
            return candidates;
        }

        var expectedSet = new HashSet<string>(expectedClasses, StringComparer.OrdinalIgnoreCase);

        // Build the exclusion set — entity types that should never match for this media type.
        var excludedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_config.ExcludeClasses.TryGetValue(mediaTypeKey, out var excludedClasses)
            && excludedClasses.Count > 0)
        {
            foreach (var qid in excludedClasses)
                excludedSet.Add(qid);
        }

        var qids = candidates.Select(c => c.Id).ToList();

        // ── Wide-net property fetch ─────────────────────────────────────────
        // Fetch P31 (type), P50 (author), P212 (ISBN-13), P957 (ISBN-10),
        // P629 (edition_or_translation_of) in one batched call. These power the
        // three-step scoring: type filter → property validation → weighted scoring.
        // P629 is used to demote translations/editions in favour of original works.
        var fetchProps = new List<string> { "P31", "P50", "P175", "P86", "P676", "P212", "P957", "P629" };
        var propsByQid = await ExtendAsync(qids, fetchProps, ct).ConfigureAwait(false);

        // ── Resolve entity labels for person-property references ────────────
        // GetPropertiesAsync may leave EntityLabel null for entity references,
        // storing only QIDs in RawValue. Batch-resolve labels so the author
        // fuzzy-matching in Step 2 can compare readable names ("Queen", not "Q15862").
        var personPropCodes = new[] { "P50", "P175", "P86", "P676" };
        var personQids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, props) in propsByQid)
        {
            foreach (var pCode in personPropCodes)
            {
                if (props.TryGetValue(pCode, out var claims))
                {
                    foreach (var c in claims)
                    {
                        if (string.IsNullOrWhiteSpace(c.Value?.EntityLabel)
                            && c.Value?.RawValue is string raw && raw.StartsWith('Q'))
                            personQids.Add(raw);
                    }
                }
            }
        }

        var personLabelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (personQids.Count > 0 && _reconciler is not null)
        {
            try
            {
                var language = _configLoader?.LoadCore().Language.Metadata ?? "en";
                var entityInfos = await _reconciler.GetEntitiesAsync(
                    personQids.ToList(), language, ct).ConfigureAwait(false);
                foreach (var (qid, info) in entityInfos)
                {
                    if (!string.IsNullOrWhiteSpace(info.Label))
                        personLabelMap[qid] = info.Label;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to resolve person entity labels for {Count} QIDs", personQids.Count);
            }
        }

        // ── Step 1: Type filter (P31) ───────────────────────────────────────
        var typeFiltered = new List<ReconciliationResult>();
        foreach (var candidate in candidates)
        {
            if (!propsByQid.TryGetValue(candidate.Id, out var props)
                || !props.TryGetValue("P31", out var p31Values)
                || p31Values.Count == 0)
            {
                _logger.LogDebug(
                    "{Provider}: candidate {QID} '{Label}' dropped — no P31 data",
                    Name, candidate.Id, candidate.Name);
                continue;
            }

            // Extract QIDs from P31 claims — check both EntityId and RawValue
            // (some Wikidata API responses put entity references in RawValue).
            var instanceOfQids = p31Values
                .Select(c =>
                    c.Value?.EntityId
                    ?? (c.Value?.RawValue is string raw && raw.StartsWith('Q') ? raw : null))
                .Where(qid => qid is not null)
                .Select(qid => qid!)
                .ToList();

            if (excludedSet.Count > 0 && instanceOfQids.Any(qid => excludedSet.Contains(qid)))
            {
                _logger.LogDebug(
                    "{Provider}: candidate {QID} '{Label}' excluded — P31 in exclude_classes",
                    Name, candidate.Id, candidate.Name);
                continue;
            }

            if (instanceOfQids.Any(qid => expectedSet.Contains(qid)))
            {
                typeFiltered.Add(candidate);
            }
            else
            {
                _logger.LogDebug(
                    "{Provider}: candidate {QID} '{Label}' dropped — P31 [{P31}] not in {MediaType} classes",
                    Name, candidate.Id, candidate.Name,
                    string.Join(", ", instanceOfQids), mediaTypeKey);
            }
        }

        // ── Step 2 & 3: Property validation + weighted scoring ──────────────
        // Score each surviving candidate:
        //   +100 (instant match) if ISBN matches
        //   +50  if title fuzzy-matches the candidate label
        //   +30  if author fuzzy-matches the candidate's P50 author
        var scored = new List<(ReconciliationResult Candidate, double Score)>();
        foreach (var candidate in typeFiltered)
        {
            double score = 0.0;
            propsByQid.TryGetValue(candidate.Id, out var cProps);

            // ISBN match (+100 — instant confirmation)
            if (!string.IsNullOrWhiteSpace(isbnHint) && cProps is not null)
            {
                var candidateIsbns = new List<string>();
                if (cProps.TryGetValue("P212", out var p212))
                    candidateIsbns.AddRange(p212.Where(c => c.Value?.RawValue is not null).Select(c => c.Value!.RawValue!));
                if (cProps.TryGetValue("P957", out var p957))
                    candidateIsbns.AddRange(p957.Where(c => c.Value?.RawValue is not null).Select(c => c.Value!.RawValue!));

                var normalizedHint = isbnHint.Replace("-", "").Replace(" ", "");
                if (candidateIsbns.Any(isbn =>
                    string.Equals(isbn.Replace("-", "").Replace(" ", ""),
                        normalizedHint, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 100.0;
                    _logger.LogDebug(
                        "{Provider}: candidate {QID} '{Label}' — ISBN match (+100)",
                        Name, candidate.Id, candidate.Name);
                }
            }

            // Title match (+50 scaled by similarity)
            if (!string.IsNullOrWhiteSpace(titleHint))
            {
                var titleSimilarity = _fuzzy.ComputeTokenSetRatio(titleHint, candidate.Name);
                score += 50.0 * titleSimilarity;
            }

            // Author/performer match (+30 scaled by best P50 or P175 similarity)
            // Supports multi-author files: "Neil Gaiman & Terry Pratchett" is split
            // and each name matched independently against P50 entries. Score = matched/total.
            if (!string.IsNullOrWhiteSpace(authorHint) && cProps is not null)
            {
                // Split file author into individual names
                var fileAuthors = SplitAuthors(authorHint);

                // Collect all Wikidata author/performer/composer labels.
                // Entity references store the QID in RawValue; the resolved
                // human-readable label lives in EntityLabel (populated by
                // ResolveClaimsEntityLabelsAsync). Use EntityLabel first.
                var wikidataAuthors = new List<string>();
                foreach (var pCode in new[] { "P50", "P175", "P86", "P676" })
                {
                    if (cProps.TryGetValue(pCode, out var pValues))
                    {
                        foreach (var claim in pValues)
                        {
                            var label = claim.Value?.EntityLabel;
                            if (string.IsNullOrWhiteSpace(label)
                                && claim.Value?.RawValue is string rawQid
                                && rawQid.StartsWith('Q'))
                            {
                                personLabelMap.TryGetValue(rawQid, out label);
                            }
                            label ??= claim.Value?.RawValue;
                            if (!string.IsNullOrWhiteSpace(label)
                                && !label.StartsWith('Q'))
                                wikidataAuthors.Add(label);
                        }
                    }
                }

                double bestAuthorMatch = 0.0;

                if (wikidataAuthors.Count > 0)
                {
                    // Multi-author matching: for each file author, find the best
                    // matching Wikidata author. Proportional scoring.
                    int matched = 0;
                    var usedIndices = new HashSet<int>();
                    foreach (var fa in fileAuthors)
                    {
                        double bestSim = 0.0;
                        int bestIdx = -1;
                        for (int i = 0; i < wikidataAuthors.Count; i++)
                        {
                            if (usedIndices.Contains(i)) continue;
                            var sim = _fuzzy.ComputeTokenSetRatio(fa, wikidataAuthors[i]);
                            if (sim > bestSim)
                            {
                                bestSim = sim;
                                bestIdx = i;
                            }
                        }
                        if (bestSim >= 0.70 && bestIdx >= 0)
                        {
                            matched++;
                            usedIndices.Add(bestIdx);
                        }
                    }

                    bestAuthorMatch = (double)matched / Math.Max(fileAuthors.Count, wikidataAuthors.Count);

                    // Also try the original full-string comparison (handles single-author case)
                    foreach (var wdAuthor in wikidataAuthors)
                    {
                        var fullStringSim = _fuzzy.ComputeTokenSetRatio(authorHint, wdAuthor);
                        if (fullStringSim > bestAuthorMatch)
                            bestAuthorMatch = fullStringSim;
                    }
                }

                score += 30.0 * bestAuthorMatch;

                if (bestAuthorMatch < 0.3)
                {
                    score -= 35.0;
                    _logger.LogDebug(
                        "{Provider}: candidate {QID} '{Label}' — author mismatch penalty (-35, best={Best:F2})",
                        Name, candidate.Id, candidate.Name, bestAuthorMatch);
                }
            }

            if (!string.IsNullOrWhiteSpace(authorHint) && cProps is not null
                && !cProps.ContainsKey("P50") && !cProps.ContainsKey("P175")
                && !cProps.ContainsKey("P86") && !cProps.ContainsKey("P676"))
            {
                score -= 40.0;
                _logger.LogDebug(
                    "{Provider}: candidate {QID} '{Label}' — no author properties penalty (-40)",
                    Name, candidate.Id, candidate.Name);
            }

            // Translation/edition penalty (-40 if P629 is present).
            // P629 (edition_or_translation_of) indicates this candidate is a derivative
            // of another work — prefer the original. This breaks ties when the original
            // and its translations both match the query equally.
            if (cProps is not null
                && cProps.TryGetValue("P629", out var p629Values) && p629Values.Count > 0)
            {
                score -= 40.0;
                _logger.LogDebug(
                    "{Provider}: candidate {QID} '{Label}' — translation/edition penalty (-40, P629 present)",
                    Name, candidate.Id, candidate.Name);
            }

            _logger.LogDebug(
                "{Provider}: candidate {QID} '{Label}' — total composite={Score:F1}",
                Name, candidate.Id, candidate.Name, score);

            scored.Add((candidate, score));
        }

        // Rank by composite score (highest first).
        // Persist the composite score into ReconciliationResult.Score so that callers
        // (FetchWorkAsync threshold check, SearchAsync confidence display) use the
        // enriched score rather than the stale API label-only score.
        var maxComposite = scored.Count > 0 ? scored.Max(s => s.Score) : 1.0;
        var normFactor = maxComposite > 0 ? 100.0 / Math.Max(maxComposite, 80.0) : 1.0;

        var result = scored
            .OrderByDescending(s => s.Score)
            .Select(s =>
            {
                var compositeNorm = Math.Min(100.0, s.Score * normFactor);
                // Weighted blend: 85% composite (type-aware), 15% original API score.
                // This ensures type filtering and title/author matching have real influence
                // rather than being overridden by the raw Wikidata label-match score.
                var blended = (compositeNorm * 0.85) + (s.Candidate.Score * 0.15);
                return new ReconciliationResult
                {
                    Id          = s.Candidate.Id,
                    Name        = s.Candidate.Name,
                    Description = s.Candidate.Description,
                    Score       = blended,
                    Match       = s.Candidate.Match,
                    Types       = s.Candidate.Types,
                };
            })
            .ToList();

        if (result.Count > 0)
        {
            var topEntry = scored.OrderByDescending(s => s.Score).First();
            _logger.LogDebug(
                "{Provider}: Scoring top={QID} '{Label}' (composite {Composite:F1}, blended {Blended:F1}) — " +
                "kept {Kept}/{Total} candidates for {MediaType}",
                Name, topEntry.Candidate.Id, topEntry.Candidate.Name,
                topEntry.Score, result[0].Score, result.Count, candidates.Count, mediaTypeKey);
        }
        else
        {
            _logger.LogDebug("{Provider}: FilterByMediaType({MediaType}) kept 0/{Total} candidates",
                Name, mediaTypeKey, candidates.Count);
        }

        return result;
    }

    /// <summary>
    /// Fetch Wikidata properties for a fictional entity (Character, Location, Organization).
    /// Used by Stage 3 Universe Enrichment — the entity QID is already known,
    /// so no reconciliation is needed, only data extension.
    /// </summary>
    /// <param name="qid">The fictional entity's Wikidata QID (e.g. "Q937618" for Paul Atreides).</param>
    /// <param name="entitySubType">
    /// One of <c>"Character"</c>, <c>"Location"</c>, or <c>"Organization"</c>.
    /// Determines which property group to fetch.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Claims extracted from the entity's Wikidata properties.</returns>
    public async Task<IReadOnlyList<ProviderClaim>> LookupFictionalEntityAsync(
        string qid, string entitySubType, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(qid);
        ArgumentException.ThrowIfNullOrWhiteSpace(entitySubType);

        if (_reconciler is null)
        {
            _logger.LogWarning("WikidataReconciler not available — skipping fictional entity lookup for {Qid}", qid);
            return [];
        }

        // Select property group based on entity sub-type.
        var propGroup = entitySubType switch
        {
            "Character" => _config.DataExtension.CharacterProperties,
            "Location" => _config.DataExtension.LocationProperties,
            "Organization" => _config.DataExtension.OrganizationProperties,
            _ => null,
        };

        if (propGroup is null || propGroup.Core.Count == 0)
        {
            _logger.LogDebug("No properties configured for entity sub-type {SubType} — skipping {Qid}", entitySubType, qid);
            return [];
        }

        // Build property list (core + bridges if any).
        var language = _configLoader?.LoadCore().Language.Metadata ?? "en";
        var props = new List<string>(propGroup.Core);
        if (propGroup.Bridges.Count > 0)
            props.AddRange(propGroup.Bridges);
        props.Add($"L{language}");  // Label in metadata language
        props.Add($"D{language}");  // Description in metadata language

        // Fetch properties via wbgetentities.
        var extResult = await ExtendAsync([qid], props, ct);

        if (!extResult.TryGetValue(qid, out var entityProps) || entityProps.Count == 0)
        {
            _logger.LogDebug("No properties returned for fictional entity {Qid} ({SubType})", qid, entitySubType);
            return [];
        }

        // Convert to provider claims using existing helper.
        var claims = ExtensionToClaims(
            qid,
            entityProps,
            _config.DataExtension.PropertyLabels,
            isWork: false,
            castMemberLimit: 0,
            metadataLanguage: language).ToList();

        _logger.LogDebug("Fictional entity {Qid} ({SubType}): {Count} claims extracted", qid, entitySubType, claims.Count);
        return claims;
    }

    /// <summary>
    /// Resolves and downloads a person headshot from Wikimedia Commons to a local folder.
    /// The filename stored in Wikidata P18 is appended to the Commons Special:FilePath URL.
    /// </summary>
    /// <param name="commonsFilename">The filename value from Wikidata P18 (e.g. "Frank_Herbert.jpg").</param>
    /// <param name="personFolderPath">Local directory to write the downloaded image.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The local file path if downloaded successfully, or <c>null</c> on failure.</returns>
    public async Task<string?> ResolveAndDownloadPersonImageAsync(
        string commonsFilename,
        string personFolderPath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(commonsFilename) || string.IsNullOrWhiteSpace(personFolderPath))
            return null;

        try
        {
            var encodedName = Uri.EscapeDataString(commonsFilename.Replace(' ', '_'));
            var url         = _config.Endpoints.CommonsFilePath + encodedName;
            var ext         = Path.GetExtension(commonsFilename).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext))
                ext = ".jpg";

            using var client   = _httpFactory.CreateClient("headshot_download");
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            Directory.CreateDirectory(personFolderPath);
            var destPath = Path.Combine(personFolderPath, $"headshot{ext}");

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var file   = File.OpenWrite(destPath);
            await stream.CopyToAsync(file, ct).ConfigureAwait(false);

            _logger.LogInformation("{Provider}: downloaded headshot to {Path}", Name, destPath);
            return destPath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Provider}: failed to download headshot '{Filename}'",
                Name, commonsFilename);
            return null;
        }
    }

    /// <summary>
    /// Resolves the Wikidata Q-identifier of the audiobook edition of a master work.
    ///
    /// <para>
    /// This is Step 2 of the 3-step audiobook pivot strategy:
    /// <list type="number">
    ///   <item>Step 1 — Match master work: Reconciliation API returns the novel/story QID (e.g. Dune = Q190192).</item>
    ///   <item>Step 2 — Pivot to audio edition: this method queries P747 (has_edition_or_translation) on the
    ///     master work and filters by P31 (instance_of) = audiobook class, returning the edition QID.</item>
    ///   <item>Step 3 — Harvest audiobook ISBN: caller uses the edition QID with Data Extension to get P212.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// When <paramref name="narratorHint"/> is provided and multiple audiobook editions exist,
    /// the edition whose narrator best matches the hint is returned first.
    /// </para>
    /// </summary>
    /// <param name="masterWorkQid">The Wikidata Q-identifier of the master work (novel/story).</param>
    /// <param name="narratorHint">Optional narrator name for edition disambiguation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The audiobook edition QID, or <c>null</c> if no audiobook edition is found in Wikidata.</returns>
    public async Task<string?> ResolveAudiobookEditionQidAsync(
        string masterWorkQid,
        string? narratorHint = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(masterWorkQid) || _reconciler is null)
            return null;

        try
        {
            var audiobookClasses = _config.InstanceOfClasses.TryGetValue("Audiobooks", out var classes)
                ? classes
                : (IReadOnlyList<string>)["Q122731938", "Q106833962"];

            var language = _configLoader?.LoadCore().Language.Metadata ?? "en";
            var editions = await _reconciler.GetEditionsAsync(
                masterWorkQid, audiobookClasses, language, ct).ConfigureAwait(false);

            if (editions.Count == 0)
            {
                _logger.LogDebug("{Provider}: no audiobook editions found for master work {QID}", Name, masterWorkQid);
                return null;
            }

            // If narrator hint provided and multiple editions, rank by narrator match
            if (!string.IsNullOrWhiteSpace(narratorHint) && editions.Count > 1)
            {
                var ranked = editions
                    .Select(e =>
                    {
                        var narrator = GetEditionNarrator(e);
                        var score = _fuzzy.ComputeTokenSetRatio(narratorHint, narrator ?? "");
                        return (Edition: e, Score: score);
                    })
                    .OrderByDescending(x => x.Score)
                    .First();

                _logger.LogInformation(
                    "{Provider}: audiobook pivot — selected edition {QID} for master work {MasterQID} (narrator hint: '{Narrator}', {Count} candidates ranked)",
                    Name, ranked.Edition.EntityId, masterWorkQid, narratorHint, editions.Count);
                return ranked.Edition.EntityId;
            }

            _logger.LogInformation("{Provider}: audiobook pivot — resolved edition {QID} for master work {MasterQID}",
                Name, editions[0].EntityId, masterWorkQid);
            return editions[0].EntityId;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Provider}: ResolveAudiobookEditionQidAsync failed for master work {QID}", Name, masterWorkQid);
            return null;
        }
    }

    private static string? GetEditionNarrator(EditionInfo edition)
    {
        if (!edition.Claims.TryGetValue("P175", out var narratorClaims) || narratorClaims.Count == 0)
            return null;
        return narratorClaims[0].Value?.EntityLabel ?? narratorClaims[0].Value?.RawValue ?? narratorClaims[0].Value?.EntityId;
    }

    private static string? GetEditionClaimValue(EditionInfo edition, string propertyId)
    {
        if (!edition.Claims.TryGetValue(propertyId, out var claims) || claims.Count == 0)
            return null;
        return claims[0].Value?.RawValue;
    }

    private static string? GetEditionClaimLabel(EditionInfo edition, string propertyId)
    {
        if (!edition.Claims.TryGetValue(propertyId, out var claims) || claims.Count == 0)
            return null;
        return claims[0].Value?.EntityLabel ?? claims[0].Value?.RawValue ?? claims[0].Value?.EntityId;
    }

    /// <summary>
    /// Discovers audiobook editions of a work via P747 (has_edition_or_translation)
    /// followed by P31 filtering to retain only audiobook-class items.
    /// When <paramref name="narratorHint"/> is provided and multiple editions exist,
    /// results are ranked by fuzzy narrator match (best match first).
    /// </summary>
    /// <param name="workQid">The Wikidata Q-identifier of the work.</param>
    /// <param name="narratorHint">Optional narrator name for disambiguation ranking.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<AudiobookEditionData>> DiscoverAudiobookEditionsAsync(
        string workQid,
        string? narratorHint = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(workQid) || _reconciler is null)
            return [];

        try
        {
            var audiobookClasses = _config.InstanceOfClasses.TryGetValue("Audiobooks", out var classes)
                ? classes
                : (IReadOnlyList<string>)["Q122731938", "Q106833962"];

            var language = _configLoader?.LoadCore().Language.Metadata ?? "en";
            var editions = await _reconciler.GetEditionsAsync(
                workQid, audiobookClasses, language, ct).ConfigureAwait(false);

            if (editions.Count == 0)
                return [];

            var results = editions.Select(e =>
            {
                var narrator  = GetEditionNarrator(e);
                var duration  = GetEditionClaimValue(e, "P2047");
                var asin      = GetEditionClaimValue(e, "P5749");
                var publisher = GetEditionClaimLabel(e, "P123");
                return new AudiobookEditionData(e.EntityId, e.Label, narrator, duration, asin, publisher);
            }).ToList();

            if (!string.IsNullOrWhiteSpace(narratorHint) && results.Count > 1)
            {
                results = results
                    .OrderByDescending(e => _fuzzy.ComputeTokenSetRatio(narratorHint, e.Narrator ?? ""))
                    .ToList();
            }

            _logger.LogDebug("{Provider}: discovered {Count} audiobook edition(s) for {QID}", Name, results.Count, workQid);
            return results;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Provider}: DiscoverAudiobookEditionsAsync failed for {QID}", Name, workQid);
            return [];
        }
    }

    /// <summary>
    /// Resolves retail bridge IDs to Wikidata edition and/or work QIDs.
    ///
    /// <para>
    /// Tries each bridge ID in priority order using a <c>haswbstatement</c> constraint
    /// in the Reconciliation API. When a match is found, checks P629
    /// (edition_or_translation_of) to determine if it's an edition, and resolves the
    /// parent work QID.
    /// </para>
    ///
    /// <para>
    /// Collects all platform IDs from the entity via Data Extension for storage in the
    /// bridge_ids table. This ensures that retail-sourced bridge IDs are cross-linked
    /// back to any additional Wikidata-known identifiers for the same entity.
    /// </para>
    /// </summary>
    /// <param name="bridgeIds">
    /// Bridge IDs to try, keyed by type (e.g. <c>"isbn" → "978-..."</c>).
    /// Tried in insertion order; resolution stops at the first match.
    /// </param>
    /// <param name="wikidataProperties">
    /// Mapping from bridge ID type to Wikidata property code
    /// (e.g. <c>"isbn" → "P212"</c>).
    /// </param>
    /// <param name="mediaType">Media type used for P31 (instance_of) filtering.</param>
    /// <param name="isEditionAware">
    /// When <c>true</c>, P629 (edition_or_translation_of) is checked to separate
    /// edition QIDs from work QIDs.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="BridgeResolutionResult"/> with the resolved QIDs, extracted claims,
    /// and all collected bridge IDs; or <see cref="BridgeResolutionResult.NotFound"/>
    /// when no match is found.
    /// </returns>
    public async Task<BridgeResolutionResult> ResolveBridgeAsync(
        IReadOnlyDictionary<string, string> bridgeIds,
        IReadOnlyDictionary<string, string> wikidataProperties,
        MediaType mediaType,
        bool isEditionAware,
        CancellationToken ct)
    {
        if (_reconciler is null)
            return BridgeResolutionResult.NotFound;

        if (bridgeIds.Count == 0)
            return BridgeResolutionResult.NotFound;

        var language = _configLoader?.LoadCore().Language.Metadata ?? "en";

        // ── Step 1: Try each bridge ID in insertion order ──────────────────────
        string? resolvedQid = null;
        bool realBridgeIdAttempted = false; // Track whether any non-sentinel lookup was tried

        foreach (var (idType, idValue) in bridgeIds)
        {
            if (string.IsNullOrWhiteSpace(idValue))
                continue;

            if (!wikidataProperties.TryGetValue(idType, out var pCode)
                || string.IsNullOrWhiteSpace(pCode))
            {
                _logger.LogInformation(
                    "{Provider}: ResolveBridgeAsync — no Wikidata property mapping for bridge type '{IdType}', skipping",
                    Name, idType);
                continue;
            }

            realBridgeIdAttempted = true;

            try
            {
                // Use LookupByExternalIdAsync which performs a CirrusSearch with
                // haswbstatement:Pxxxx=value — this finds entities by their external ID
                // properties directly, rather than text-matching on entity labels.
                var lookupResults = await _reconciler.LookupByExternalIdAsync(
                    pCode, idValue, language, ct).ConfigureAwait(false);

                if (lookupResults.Count == 0)
                {
                    _logger.LogInformation(
                        "{Provider}: ResolveBridgeAsync — no candidates for {IdType}={IdValue} (haswbstatement:{PCode}={IdValue2})",
                        Name, idType, idValue, pCode, idValue);
                    continue;
                }

                _logger.LogInformation(
                    "{Provider}: ResolveBridgeAsync — LookupByExternalId returned {Count} candidate(s) for {IdType}={IdValue}",
                    Name, lookupResults.Count, idType, idValue);

                // Accept the first result — haswbstatement is a precise match.
                resolvedQid = lookupResults[0].Id;
                _logger.LogInformation(
                    "{Provider}: ResolveBridgeAsync — resolved {IdType}={IdValue} to QID {QID} " +
                    "via haswbstatement:{PCode}={IdValue2}",
                    Name, idType, idValue, resolvedQid, pCode, idValue);
                break;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "{Provider}: ResolveBridgeAsync — reconciliation failed for {IdType}={IdValue}",
                    Name, idType, idValue);
            }
        }

        if (string.IsNullOrWhiteSpace(resolvedQid))
        {
            // ── Fallback: text-based reconciliation with type filtering ───────
            // Only allowed when real bridge IDs were attempted and failed.
            // If only sentinel keys (_title, _author) were provided (no real bridge
            // IDs from Stage 1), the item should go to review instead.
            if (!realBridgeIdAttempted)
            {
                _logger.LogDebug(
                    "{Provider}: ResolveBridgeAsync — no real bridge IDs were attempted (sentinel-only). " +
                    "Text fallback blocked; item will route to review queue.",
                    Name);
                return BridgeResolutionResult.NotFound;
            }

            var mediaTypeKey = mediaType.ToString();
            var hasTypeClasses = _config.InstanceOfClasses.TryGetValue(mediaTypeKey, out var typeClasses)
                && typeClasses.Count > 0;

            if (bridgeIds.TryGetValue("_title", out var fallbackTitle)
                && !string.IsNullOrWhiteSpace(fallbackTitle))
            {
                bridgeIds.TryGetValue("_author", out var fallbackAuthor);
                var fallbackQuery = string.IsNullOrWhiteSpace(fallbackAuthor)
                    ? fallbackTitle
                    : $"{fallbackTitle} {fallbackAuthor}";

                try
                {
                    // Type-constrained reconciliation (only runs when classes are configured).
                    if (hasTypeClasses)
                    {
                        _logger.LogDebug(
                            "{Provider}: ResolveBridgeAsync — bridge IDs exhausted, attempting text reconciliation " +
                            "for '{Query}' ({MediaType}, types={TypeClasses})",
                            Name, fallbackQuery, mediaType, string.Join(",", typeClasses!));

                        var reconRequest = new ReconciliationRequest
                        {
                            Query = fallbackQuery,
                            Limit = 5,
                            Language = language,
                            DiacriticInsensitive = true,
                            Cleaners = QueryCleaners.All(),
                            Types = typeClasses!.ToList(),
                        };

                        var fallbackCandidates = await _reconciler.ReconcileAsync(reconRequest, ct)
                            .ConfigureAwait(false);

                        // Apply the same P31/exclude_classes filtering used in FetchWorkAsync
                        // to prevent cross-type mismatches (e.g. book → video game).
                        if (fallbackCandidates.Count > 0)
                        {
                            _logger.LogDebug(
                                "{Provider}: ResolveBridgeAsync — text reconciliation returned {Count} candidates " +
                                "for '{Query}': {Candidates}",
                                Name, fallbackCandidates.Count, fallbackQuery,
                                string.Join(", ", fallbackCandidates.Select(c => $"{c.Id} '{c.Name}' score={c.Score}")));

                            var filteredCandidates = await FilterByMediaTypeAsync(
                                fallbackCandidates, mediaType, ct,
                                fallbackTitle, fallbackAuthor, null).ConfigureAwait(false);

                            if (filteredCandidates.Count > 0)
                            {
                                var topCandidate = filteredCandidates[0];
                                if (topCandidate.Score >= _config.Reconciliation.ReviewThreshold)
                                {
                                    resolvedQid = topCandidate.Id;
                                    _logger.LogDebug(
                                        "{Provider}: ResolveBridgeAsync — text reconciliation matched '{Query}' to " +
                                        "{QID} '{Label}' (score={Score}, after P31 filtering)",
                                        Name, fallbackQuery, resolvedQid, topCandidate.Name, topCandidate.Score);
                                }
                                else
                                {
                                    _logger.LogDebug(
                                        "{Provider}: ResolveBridgeAsync — text reconciliation top candidate " +
                                        "'{Label}' ({QID}) score {Score} below threshold {Threshold}",
                                        Name, topCandidate.Name, topCandidate.Id, topCandidate.Score,
                                        _config.Reconciliation.ReviewThreshold);
                                }
                            }
                            else
                            {
                                _logger.LogDebug(
                                    "{Provider}: ResolveBridgeAsync — all {Count} candidates filtered out by P31 for '{Query}'",
                                    Name, fallbackCandidates.Count, fallbackQuery);
                            }
                        }
                        else
                        {
                            _logger.LogDebug(
                                "{Provider}: ResolveBridgeAsync — text reconciliation returned 0 candidates for '{Query}'",
                                Name, fallbackQuery);

                            // When the combined title+author query returns nothing, retry with
                            // title alone. Author names can confuse CirrusSearch — e.g. "Queen"
                            // matches the British monarchy article before the band's songs.
                            if (!string.IsNullOrWhiteSpace(fallbackAuthor))
                            {
                                _logger.LogDebug(
                                    "{Provider}: ResolveBridgeAsync — retrying text reconciliation with title-only '{Title}' (dropping author '{Author}')",
                                    Name, fallbackTitle, fallbackAuthor);

                                var titleOnlyRequest = new ReconciliationRequest
                                {
                                    Query = fallbackTitle,
                                    Limit = 5,
                                    Language = language,
                                    DiacriticInsensitive = true,
                                    Cleaners = QueryCleaners.All(),
                                    Types = typeClasses!.ToList(),
                                };

                                var titleOnlyCandidates = await _reconciler.ReconcileAsync(titleOnlyRequest, ct)
                                    .ConfigureAwait(false);

                                if (titleOnlyCandidates.Count > 0)
                                {
                                    _logger.LogDebug(
                                        "{Provider}: ResolveBridgeAsync — title-only reconciliation returned {Count} candidates for '{Title}': {Candidates}",
                                        Name, titleOnlyCandidates.Count, fallbackTitle,
                                        string.Join(", ", titleOnlyCandidates.Select(c => $"{c.Id} '{c.Name}' score={c.Score}")));

                                    var filteredRetry = await FilterByMediaTypeAsync(
                                        titleOnlyCandidates, mediaType, ct,
                                        fallbackTitle, fallbackAuthor, null).ConfigureAwait(false);

                                    if (filteredRetry.Count > 0)
                                    {
                                        var topRetry = filteredRetry[0];
                                        if (topRetry.Score >= _config.Reconciliation.ReviewThreshold)
                                        {
                                            resolvedQid = topRetry.Id;
                                            _logger.LogDebug(
                                                "{Provider}: ResolveBridgeAsync — title-only reconciliation matched '{Title}' to {QID} '{Label}' (score={Score})",
                                                Name, fallbackTitle, resolvedQid, topRetry.Name, topRetry.Score);
                                        }
                                    }
                                }
                                else
                                {
                                    _logger.LogDebug(
                                        "{Provider}: ResolveBridgeAsync — title-only reconciliation also returned 0 candidates for '{Title}'",
                                        Name, fallbackTitle);
                                }
                            }
                        }
                    }

                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "{Provider}: ResolveBridgeAsync — text reconciliation fallback failed for '{Query}'",
                        Name, fallbackQuery);
                }
            }

            // If no match after type-constrained reconciliation, give up.
            if (string.IsNullOrWhiteSpace(resolvedQid))
                return BridgeResolutionResult.NotFound;
        }

        // ── Step 2: Check P629 to determine if the resolved entity is an edition ──
        bool isEdition = false;
        string? workQid = null;
        string? editionQid = null;

        if (isEditionAware)
        {
            try
            {
                var p629Extensions = await ExtendAsync([resolvedQid], ["P629"], ct).ConfigureAwait(false);
                if (p629Extensions.TryGetValue(resolvedQid, out var p629Props)
                    && p629Props.TryGetValue("P629", out var p629Values)
                    && p629Values.Count > 0)
                {
                    var workEntityId = p629Values
                        .Where(c => c.Value?.EntityId is not null)
                        .Select(c => c.Value!.EntityId!)
                        .FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(workEntityId))
                    {
                        isEdition = true;
                        editionQid = resolvedQid;
                        workQid = workEntityId;

                        _logger.LogDebug(
                            "{Provider}: ResolveBridgeAsync — {QID} is an edition (P629 → work {WorkQID})",
                            Name, resolvedQid, workQid);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "{Provider}: ResolveBridgeAsync — P629 check failed for {QID}, treating as work",
                    Name, resolvedQid);
            }
        }

        if (!isEdition)
        {
            workQid = resolvedQid;
            editionQid = null;
        }

        // ── Step 3: Collect all platform IDs via Data Extension ────────────────
        var bridgePCodes = new List<string>
        {
            "P212",  // ISBN-13
            "P5848", // Apple Books ID (ebook)
            "P5749", // ASIN (Audible / Amazon)
            "P4947", // TMDB movie ID
            "P345",  // IMDb ID
            "P9586", // Apple TV movie ID
            "P9750", // Apple TV show ID
            "P4857", // Apple Music ID
            "P1243", // ISRC (International Standard Recording Code)
            "P5849", // Apple Podcasts ID
            "P434",  // MusicBrainz artist ID
            "P436",  // MusicBrainz release ID
            "P5905", // Comic Vine ID
            "P2969", // Goodreads ID
            "P1085", // LibraryThing ID
        };

        var collectedBridgeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var claims = new List<ProviderClaim>();

        try
        {
            var bridgeExtensions = await ExtendAsync([resolvedQid], bridgePCodes, ct).ConfigureAwait(false);

            if (bridgeExtensions.TryGetValue(resolvedQid, out var resolvedProps))
            {
                // Extract collected bridge IDs into the dedicated dictionary.
                foreach (var pCode in bridgePCodes)
                {
                    if (!resolvedProps.TryGetValue(pCode, out var pValues) || pValues.Count == 0)
                        continue;

                    var firstVal = pValues[0].Value;
                    if (firstVal is null) continue;

                    var rawVal = firstVal.RawValue ?? firstVal.EntityId;
                    if (string.IsNullOrWhiteSpace(rawVal)) continue;

                    var normalized = IdentifierNormalizationService.NormalizeRaw(pCode, rawVal);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        collectedBridgeIds[pCode] = normalized;
                }

                // Convert all properties into ProviderClaims using the standard path.
                claims.AddRange(ExtensionToClaims(
                    resolvedQid,
                    resolvedProps,
                    _config.DataExtension.PropertyLabels,
                    isWork: true,
                    castMemberLimit: _config.Reconciliation.CastMemberLimit,
                    metadataLanguage: language));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "{Provider}: ResolveBridgeAsync — bridge ID collection failed for {QID}",
                Name, resolvedQid);
        }

        // Always emit the wikidata_qid for the work.
        claims.Insert(0, new ProviderClaim(BridgeIdKeys.WikidataQid, workQid!, 1.0));

        // When an edition was found, also emit the edition QID.
        if (isEdition && !string.IsNullOrWhiteSpace(editionQid))
            claims.Add(new ProviderClaim("edition_qid", editionQid, 1.0));

        _logger.LogInformation(
            "{Provider}: ResolveBridgeAsync — resolved QID {QID} (isEdition={IsEdition}, " +
            "workQid={WorkQid}, {ClaimCount} claims, {BridgeCount} bridge IDs collected)",
            Name, resolvedQid, isEdition, workQid, claims.Count, collectedBridgeIds.Count);

        return new BridgeResolutionResult
        {
            Found              = true,
            Qid                = resolvedQid,
            IsEdition          = isEdition,
            WorkQid            = workQid,
            EditionQid         = editionQid,
            Claims             = claims,
            CollectedBridgeIds = collectedBridgeIds,
        };
    }

    // ── Private: FetchWork / FetchPerson ─────────────────────────────────────

    private async Task<IReadOnlyList<ProviderClaim>> FetchWorkAsync(
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        // Use PreResolvedQid if provided — skip reconciliation entirely.
        var qid = request.PreResolvedQid;
        string? reconciliationLabel = null;

        if (string.IsNullOrWhiteSpace(qid))
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return [];

            // The library's Cleaners = QueryCleaners.All() and DiacriticInsensitive = true
            // handle title cleaning and diacritics normalization automatically.
            var searchTitle = request.Title;

            // Guard: do not attempt reconciliation when media type is unknown.
            // Unknown media type means P31 filtering cannot be applied, leading to
            // misidentification (e.g. novels matching video games or sculptures).
            // The item will be routed to the review queue by the caller.
            if (request.MediaType == MediaType.Unknown)
            {
                _logger.LogInformation(
                    "{Provider}: skipping reconciliation for '{Title}' — media type is Unknown, item requires manual classification",
                    Name, request.Title);
                return [];
            }

            // Build author property constraint for better reconciliation scoring.
            // Multi-author files pass all authors via PropertyConstraint.Values for
            // proportional matching (v0.10.0 feature).
            Dictionary<string, string>? constraints = null;
            List<PropertyConstraint>? multiValueConstraints = null;
            if (!string.IsNullOrWhiteSpace(request.Author))
            {
                var authors = SplitAuthors(request.Author);
                if (authors.Count > 1)
                {
                    multiValueConstraints =
                    [
                        new PropertyConstraint { PropertyId = "P50", Values = authors }
                    ];
                }
                else
                {
                    constraints = new Dictionary<string, string> { ["P50"] = request.Author };
                }
            }

            var candidates = await ReconcileAsync(searchTitle, constraints, ct, request.MediaType, multiValueConstraints).ConfigureAwait(false);

            if (candidates.Count == 0)
            {
                _logger.LogDebug("{Provider}: no reconciliation candidates for '{Title}'",
                    Name, request.Title);
                return [];
            }

            // Always apply P31 type filtering (Unknown media type already returned above).
            var filtered = await FilterByMediaTypeAsync(
                    candidates, request.MediaType, ct,
                    request.Title, request.Author, request.Isbn)
                    .ConfigureAwait(false);
            if (filtered.Count == 0)
            {
                // Type-constrained retry: append media type hint to query so that
                // Wikidata surfaces the correct entity type (e.g. "Shogun television series"
                // finds Q56276181 instead of the novel Q131767 dominating the plain search).
                var typeHint = request.MediaType switch
                {
                    MediaType.Books      => "novel book",
                    MediaType.Audiobooks => "audiobook",
                    MediaType.Movies     => "film movie",
                    MediaType.TV         => "television series",
                    MediaType.Music      => "song music",
                    MediaType.Comics     => "comic manga",
                    MediaType.Podcasts   => "podcast",
                    _                    => null
                };

                if (typeHint is not null)
                {
                    _logger.LogDebug(
                        "{Provider}: P31 filter eliminated all {Count} candidates for '{Title}' ({MediaType}), retrying with type hint",
                        Name, candidates.Count, request.Title, request.MediaType);

                    var retryQuery = $"{searchTitle} {typeHint}";
                    var retryCandidates = await ReconcileAsync(retryQuery, null, ct, request.MediaType).ConfigureAwait(false);

                    if (retryCandidates.Count > 0)
                    {
                        filtered = await FilterByMediaTypeAsync(
                            retryCandidates, request.MediaType, ct,
                            request.Title, request.Author, request.Isbn)
                            .ConfigureAwait(false);
                    }
                }

                if (filtered.Count == 0)
                {
                    _logger.LogInformation(
                        "{Provider}: no candidates survived P31 filter for '{Title}' ({MediaType}), sending to review",
                        Name, request.Title, request.MediaType);
                    return [];
                }
            }

            // Accept the top candidate if it meets the auto-accept threshold.
            var top = filtered[0];
            if (top.Score < _config.Reconciliation.ReviewThreshold)
            {
                _logger.LogDebug(
                    "{Provider}: top candidate '{Label}' ({QID}) score {Score} below review threshold",
                    Name, top.Name, top.Id, top.Score);
                return [];
            }

            // Wikidata Reconciliation API is trusted as the sole identity authority.
            // Its matching engine handles aliases, alternate spellings, and language
            // variants (e.g. "1984" → "Nineteen Eighty-Four") internally.
            // A score >= review_threshold is sufficient to accept the candidate.

            qid = top.Id;
            // Use MatchedLabel from the library if available (alias or sitelink that matched).
            reconciliationLabel = top.MatchedLabel ?? top.Name;
        }
        else
        {
            // Manual QID selection: fetch the Wikidata label to use as reconciliation title.
            // Without this, the title claim at ReconciliationTitle confidence (0.98) is never emitted,
            // and the title falls through to Data Extension at lower confidence (0.90).
            try
            {
                var labelLanguage = _configLoader?.LoadCore().Language.Metadata ?? "en";
                var labelProps = await _reconciler!.GetPropertiesAsync(
                    [qid], [$"L{labelLanguage}"], labelLanguage, ct).ConfigureAwait(false);

                if (labelProps.TryGetValue(qid, out var labelData)
                    && labelData.TryGetValue($"L{labelLanguage}", out var labelClaims)
                    && labelClaims.Count > 0
                    && !string.IsNullOrWhiteSpace(labelClaims[0].Value?.RawValue))
                {
                    reconciliationLabel = labelClaims[0].Value!.RawValue;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "{Provider}: Failed to fetch label for pre-resolved QID {Qid}", Name, qid);
            }
        }

        // ── Step 2 & 3: Audiobook Edition Pivot ──────────────────────────────────
        // For audiobooks, the master work QID (e.g. Dune = Q190192) does not carry
        // an audiobook ISBN — only its audiobook edition item (P747 + P31 filter) does.
        // Pivot to the edition QID so that Data Extension returns the audiobook-specific
        // P212 (ISBN-13) and other edition-level bridge IDs (P5749 / ASIN, P6395 / Apple Books ID).
        string masterWorkQid = qid; // preserve the master work QID for claims
        string? audiobookEditionQid = null;

        if (request.MediaType == MediaType.Audiobooks)
        {
            var narratorHint = request.Narrator;
            audiobookEditionQid = await ResolveAudiobookEditionQidAsync(qid, narratorHint, ct)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(audiobookEditionQid))
            {
                _logger.LogInformation(
                    "{Provider}: audiobook 3-step pivot — master work {MasterQID} → edition {EditionQID}; " +
                    "Data Extension will target the edition for audiobook-specific bridge IDs",
                    Name, qid, audiobookEditionQid);
            }
            else
            {
                _logger.LogDebug(
                    "{Provider}: audiobook 3-step pivot — no edition found for master work {MasterQID}; " +
                    "falling back to master work for Data Extension",
                    Name, qid);
            }
        }

        // Extend the resolved QID with work properties.
        var workProps = _config.DataExtension.WorkProperties;
        var language = _configLoader?.LoadCore().Language.Metadata ?? "en";

        var claims = new List<ProviderClaim>
        {
            // Always emit the master work QID as the canonical wikidata_qid.
            // This ensures Hub grouping is based on the creative work, not the edition.
            new(BridgeIdKeys.WikidataQid, masterWorkQid, 1.0)
        };

        // When we pivoted to an audiobook edition, also emit the edition QID as a separate
        // claim so other parts of the pipeline can reference it (e.g. for cover art lookup).
        if (audiobookEditionQid is not null)
            claims.Add(new ProviderClaim("audiobook_edition_qid", audiobookEditionQid, 1.0));

        // Emit the reconciliation match label as a title claim. Wikidata is the authority for
        // canonical data (CLAUDE.md §3.2 Tier C), so the display-language title from Wikidata
        // must beat the file processor's embedded title (which may be in a foreign language).
        // Confidence 0.98 ensures it wins over file processor (1.0 is reserved for user locks).
        if (!string.IsNullOrWhiteSpace(reconciliationLabel))
            claims.Add(new ProviderClaim(MetadataFieldConstants.Title, reconciliationLabel, ClaimConfidence.ReconciliationTitle));

        // extProps holds the master work extension properties — used by pen name detection and
        // edition bridge ID resolution below. Set inside both branches.
        IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>>? extProps = null;

        if (audiobookEditionQid is not null)
        {
            // ── Dual Data Extension for audiobooks ──
            // Audiobook editions on Wikidata often lack P50 (author) and P577 (year) —
            // those live on the master work. Run TWO calls:
            // 1. Master work QID → core properties (author, year, title, genre, series)
            // 2. Edition QID → edition-specific properties (performer, ASIN, duration, ISBN)

            // Master work: core properties + language labels
            var masterProps = workProps.Core.ToList();
            masterProps.Add($"L{language}");
            masterProps.Add($"D{language}");

            var masterExtensions = await ExtendAsync([masterWorkQid], masterProps, ct).ConfigureAwait(false);
            masterExtensions.TryGetValue(masterWorkQid, out extProps);
            if (extProps is not null)
                claims.AddRange(ExtensionToClaims(masterWorkQid, extProps, _config.DataExtension.PropertyLabels, isWork: true, castMemberLimit: _config.Reconciliation.CastMemberLimit, metadataLanguage: language));

            // Edition: edition-specific properties + bridges
            var editionProps = (_config.DataExtension.AudiobookEditionProperties ?? [])
                .Concat(workProps.Bridges)
                .Concat(workProps.Editions)
                .Distinct()
                .ToList();

            if (editionProps.Count > 0)
            {
                var editionExtensions = await ExtendAsync([audiobookEditionQid], editionProps, ct).ConfigureAwait(false);
                if (editionExtensions.TryGetValue(audiobookEditionQid, out var editionEntityProps))
                    claims.AddRange(ExtensionToClaims(audiobookEditionQid, editionEntityProps, _config.DataExtension.PropertyLabels, isWork: true, castMemberLimit: _config.Reconciliation.CastMemberLimit, metadataLanguage: language));
            }

            _logger.LogDebug(
                "{Provider}: dual Data Extension for audiobook — master {MasterQID} ({MasterCount} props) + " +
                "edition {EditionQID} ({EditionCount} props)",
                Name, masterWorkQid, masterProps.Count, audiobookEditionQid, editionProps.Count);
        }
        else
        {
            // ── Standard single Data Extension ──
            var allProps = workProps.Core
                .Concat(workProps.Bridges)
                .Concat(workProps.Editions)
                .Distinct()
                .ToList();

            allProps.Add($"L{language}");
            allProps.Add($"D{language}");

            if (allProps.Count == 0)
                return [new ProviderClaim(BridgeIdKeys.WikidataQid, masterWorkQid, 1.0)];

            _logger.LogInformation(
                "{Provider}: Data Extension for {QID} — requesting {Count} properties: [{Props}]",
                Name, qid, allProps.Count, string.Join(", ", allProps));

            var extensions = await ExtendAsync([qid], allProps, ct).ConfigureAwait(false);
            extensions.TryGetValue(qid, out extProps);

            if (extProps is not null)
            {
                _logger.LogInformation(
                    "{Provider}: Data Extension returned {PropCount} properties for {QID}: [{Keys}]",
                    Name, extProps.Count, qid,
                    string.Join(", ", extProps.Keys));
                claims.AddRange(ExtensionToClaims(qid, extProps, _config.DataExtension.PropertyLabels, isWork: true, castMemberLimit: _config.Reconciliation.CastMemberLimit, metadataLanguage: language));
            }
            else
            {
                _logger.LogWarning(
                    "{Provider}: Data Extension returned NO properties for {QID} (extensions had {Count} entities)",
                    Name, qid, extensions.Count);
            }
        }

        // ── Pen name detection via GetAuthorPseudonymsAsync ──────────────────
        // When Wikidata P50 lists 2+ authors (e.g. Daniel Abraham + Ty Franck for
        // "The Expanse"), use the library's GetAuthorPseudonymsAsync to check if
        // all co-authors share a common pen name, and emit it as the canonical author.
        if (extProps is not null
            && extProps.TryGetValue("P50", out var authorRefs)
            && authorRefs.Count >= 2
            && _reconciler is not null)
        {
            try
            {
                var pseudonyms = await _reconciler.GetAuthorPseudonymsAsync(masterWorkQid, language, ct)
                    .ConfigureAwait(false);

                // Find a shared pseudonym (all co-authors have the same pen name)
                if (pseudonyms.Count >= 2)
                {
                    var allPenNames = pseudonyms.Select(p => p.Pseudonyms).ToList();
                    if (allPenNames.All(p => p.Count > 0))
                    {
                        var sharedPenNames = new HashSet<string>(allPenNames[0], StringComparer.OrdinalIgnoreCase);
                        foreach (var penNames in allPenNames.Skip(1))
                            sharedPenNames.IntersectWith(penNames);

                        if (sharedPenNames.Count > 0)
                        {
                            var penName = sharedPenNames.First();

                            // Re-key existing P50-derived author and author_qid claims
                            for (int i = 0; i < claims.Count; i++)
                            {
                                if (string.Equals(claims[i].Key, "author_qid", StringComparison.OrdinalIgnoreCase))
                                    claims[i] = new ProviderClaim("author_real_name_qid", claims[i].Value, claims[i].Confidence);
                                else if (string.Equals(claims[i].Key, MetadataFieldConstants.Author, StringComparison.OrdinalIgnoreCase))
                                    claims[i] = new ProviderClaim("author_real_name", claims[i].Value, claims[i].Confidence);
                            }

                            claims.Add(new ProviderClaim(MetadataFieldConstants.Author, penName, ClaimConfidence.PenName));
                            claims.Add(new ProviderClaim("author_is_collective_pseudonym", "true", ClaimConfidence.CollectivePseudonym));

                            // Resolve pen name QID
                            string? penNameQid = null;
                            try
                            {
                                var penNameCandidates = await ReconcileAsync(penName, null, ct).ConfigureAwait(false);
                                var authorQids = new HashSet<string>(pseudonyms.Select(p => p.AuthorEntityId), StringComparer.OrdinalIgnoreCase);
                                var bestMatch = penNameCandidates
                                    .Where(c => (c.Match || c.Score >= 80) && !authorQids.Contains(c.Id))
                                    .OrderByDescending(c => c.Score)
                                    .FirstOrDefault();
                                if (bestMatch is not null)
                                    penNameQid = bestMatch.Id;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug("{Provider}: pen name QID lookup failed for \"{PenName}\": {Message}",
                                    Name, penName, ex.Message);
                            }

                            if (penNameQid is not null)
                                claims.Add(new ProviderClaim("author_qid", $"{penNameQid}::{penName}", ClaimConfidence.PenName));

                            var memberClaims = claims
                                .Where(c => string.Equals(c.Key, "author_real_name_qid", StringComparison.OrdinalIgnoreCase)
                                             && !string.IsNullOrWhiteSpace(c.Value))
                                .Select(c => new ProviderClaim("collective_members_qid", c.Value, ClaimConfidence.WikidataProperty))
                                .ToList();
                            claims.AddRange(memberClaims);

                            _logger.LogInformation(
                                "{Provider}: pen name detected for QID {QID} — {AuthorCount} co-authors share pen name \"{PenName}\" (QID: {PenNameQid})",
                                Name, masterWorkQid, pseudonyms.Count, penName, penNameQid ?? "none");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("{Provider}: pen name detection failed for QID {QID}: {Message}",
                    Name, masterWorkQid, ex.Message);
            }
        }

        // ── Pen name preservation via embedded-author mismatch ────────────────
        // Safety net for when P742 data is missing or the pen name detection
        // block above could not resolve a shared pen name. If the request carries
        // an embedded author name (from file metadata / local_filesystem provider)
        // that does NOT fuzzy-match any of the "author" claims emitted so far from
        // Wikidata P50, it is very likely a pen name situation — the file was
        // credited to the pen name but Wikidata lists the real people.
        //
        // In that case we:
        //   1. Re-key the Wikidata P50 real-name "author" claims as "author_real_name"
        //      so they are available for person enrichment but do NOT compete with the
        //      canonical author field in the priority cascade.
        //   2. Emit the embedded author name at Wikidata authority confidence (0.95)
        //      so the credited pen name wins as the canonical author.
        if (!string.IsNullOrWhiteSpace(request.Author) && extProps is not null
            && extProps.TryGetValue("P50", out var p50AuthorRefs)
            && p50AuthorRefs.Count > 0)
        {
            // Collect the author labels that ExtensionToClaims already emitted.
            var wikiAuthorClaims = claims
                .Where(c => string.Equals(c.Key, MetadataFieldConstants.Author, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (wikiAuthorClaims.Count > 0)
            {
                // Check whether the embedded author matches ANY of the Wikidata author labels.
                var embeddedAuthor = request.Author;
                bool embeddedMatchesAnyWikiAuthor = wikiAuthorClaims
                    .Any(c => _fuzzy.ComputeTokenSetRatio(embeddedAuthor, c.Value) >= 0.80);

                if (!embeddedMatchesAnyWikiAuthor)
                {
                    // Check if the pen name detection block already added a pen name author
                    // at confidence 0.95 — if so, no further action is needed.
                    bool penNameAlreadyEmitted = wikiAuthorClaims
                        .Any(c => string.Equals(c.Value, embeddedAuthor, StringComparison.OrdinalIgnoreCase)
                                  || _fuzzy.ComputeTokenSetRatio(embeddedAuthor, c.Value) >= 0.90);

                    if (!penNameAlreadyEmitted)
                    {
                        // Re-key the existing Wikidata P50 real-name "author" and "author_qid"
                        // claims so they don't compete in the canonical field elections.
                        for (int i = 0; i < claims.Count; i++)
                        {
                            if (string.Equals(claims[i].Key, MetadataFieldConstants.Author, StringComparison.OrdinalIgnoreCase))
                            {
                                claims[i] = new ProviderClaim("author_real_name", claims[i].Value, claims[i].Confidence);
                            }
                            else if (string.Equals(claims[i].Key, "author_qid", StringComparison.OrdinalIgnoreCase))
                            {
                                claims[i] = new ProviderClaim("author_real_name_qid", claims[i].Value, claims[i].Confidence);
                            }
                        }

                        // Emit the embedded (credited) author name at high confidence so it
                        // wins as the canonical author value in the priority cascade.
                        claims.Add(new ProviderClaim(MetadataFieldConstants.Author, embeddedAuthor, ClaimConfidence.EmbeddedAuthor));

                        // Resolve the pen name's QID via Reconciliation lookup so person
                        // enrichment creates a Person for the pen name, not the real authors.
                        try
                        {
                            var penNameCandidates = await ReconcileAsync(embeddedAuthor, null, ct).ConfigureAwait(false);
                            var bestMatch = penNameCandidates
                                .Where(c => c.Match || c.Score >= 80)
                                .OrderByDescending(c => c.Score)
                                .FirstOrDefault();

                            if (bestMatch is not null)
                            {
                                claims.Add(new ProviderClaim("author_qid", $"{bestMatch.Id}::{embeddedAuthor}", ClaimConfidence.EmbeddedAuthor));
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug("{Provider}: pen name QID lookup failed for \"{PenName}\": {Message}",
                                Name, embeddedAuthor, ex.Message);
                        }

                        _logger.LogInformation(
                            "{Provider}: embedded author \"{EmbeddedAuthor}\" does not match Wikidata P50 " +
                            "authors for QID {QID} — treating as pen name. Real author claims re-keyed to " +
                            "\"author_real_name\"/\"author_real_name_qid\"; credited pen name emitted as canonical author.",
                            Name, embeddedAuthor, masterWorkQid);
                    }
                }
            }
        }

        // ── Edition bridge ID resolution ─────────────────────────────────────
        // Wikidata stores ISBNs and other bridge IDs on edition items (P747),
        // not on the work itself. If key bridge IDs are still missing after
        // the work/edition fetch, look them up via P747 on the master work.
        //
        // When the audiobook 3-step pivot has already targeted an edition QID,
        // the Data Extension call above directly targeted that edition and should
        // already have returned its bridge IDs (P212, P5749 etc.). In that case,
        // most or all of these properties will already be in `claims` and this block
        // will find nothing missing. It still runs as a safety net for any gaps.
        if (extProps is not null)
        {
            var editionBridgeProps = new[] { "P212", "P957", "P5749", "P6395", "P2969", "P648" }
                .Where(p => _config.DataExtension.PropertyLabels.ContainsKey(p))
                .ToList();

            // When we pivoted to an audiobook edition, extProps is from that edition — it won't
            // have P747 pointing to further sub-editions. For the standard bridge fallback,
            // we need P747 from the master work. Fetch it separately in that case.
            IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>>? editionSourceProps = extProps;
            if (audiobookEditionQid is not null)
            {
                try
                {
                    var masterExtensions = await ExtendAsync([masterWorkQid], ["P747"], ct).ConfigureAwait(false);
                    if (masterExtensions.TryGetValue(masterWorkQid, out var masterProps2))
                        editionSourceProps = masterProps2;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("{Provider}: master work P747 fetch failed for {QID}: {Message}",
                        Name, masterWorkQid, ex.Message);
                }
            }

            if (editionBridgeProps.Count > 0
                && editionSourceProps is not null
                && editionSourceProps.TryGetValue("P747", out var editionRefs)
                && editionRefs.Count > 0)
            {
                // Determine which bridge IDs are still missing from the work/edition-level fetch.
                var emittedKeys = new HashSet<string>(
                    claims.Select(c => c.Key), StringComparer.OrdinalIgnoreCase);

                var missingProps = editionBridgeProps
                    .Where(p => !emittedKeys.Contains(_config.DataExtension.PropertyLabels[p]))
                    .ToList();

                if (missingProps.Count > 0)
                {
                    var editionQids = editionRefs
                        .Where(c => c.Value?.EntityId is not null)
                        .Select(c => c.Value!.EntityId!)
                        .Distinct()
                        .Take(10)
                        .ToList();

                    if (editionQids.Count > 0)
                    {
                        try
                        {
                            // Include P31 in the request to enable media-type filtering.
                            var propsWithP31 = missingProps.Contains("P31")
                                ? missingProps
                                : [.. missingProps, "P31"];

                            var editionDataMap = await ExtendAsync(editionQids, propsWithP31, ct)
                                .ConfigureAwait(false);

                            // Filter editions by media type: audiobooks get audiobook-class
                            // editions only, books get non-audiobook editions.
                            var filteredEditions = FilterEditionsByMediaType(editionDataMap, request.MediaType);

                            foreach (var propCode in missingProps)
                            {
                                var claimKey = _config.DataExtension.PropertyLabels[propCode];

                                foreach (var (_, edProps2) in filteredEditions)
                                {
                                    if (!edProps2.TryGetValue(propCode, out var vals)
                                        || vals.Count == 0)
                                        continue;

                                    var firstVal = vals.FirstOrDefault();
                                    if (firstVal is null) continue;

                                    var strVal = firstVal.Value?.RawValue ?? firstVal.Value?.EntityId;
                                    if (!string.IsNullOrWhiteSpace(strVal))
                                    {
                                        // Normalize bridge ID values for clean storage.
                                        var normalized = IdentifierNormalizationService.NormalizeRaw(propCode, strVal);
                                        if (!string.IsNullOrWhiteSpace(normalized))
                                        {
                                            claims.Add(new ProviderClaim(claimKey, normalized, ClaimConfidence.WikidataProperty));
                                            break;
                                        }
                                    }
                                }
                            }

                            _logger.LogDebug("{Provider}: edition bridge resolution added bridge IDs for {QID}",
                                Name, masterWorkQid);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug("{Provider}: edition bridge ID resolution failed for {QID}: {Message}",
                                Name, masterWorkQid, ex.Message);
                        }
                    }
                }
            }
        }

        _logger.LogInformation(
            "{Provider}: fetched {Count} work claims for {QID} (audiobook edition pivot: {Pivoted})",
            Name, claims.Count, masterWorkQid, audiobookEditionQid is not null);

        // ── Wikipedia description ─────────────────────────────────────────────
        // Fetch a rich Wikipedia description for this work using the resolved QID.
        // Failures never block — an empty list is returned and execution continues.
        var wikiWorkClaims = await FetchWikipediaDescriptionAsync(masterWorkQid, language, ct)
            .ConfigureAwait(false);
        claims.AddRange(wikiWorkClaims);

        // ── Original title (for foreign-language files) ───────────────────────
        // When the file's detected language differs from the configured metadata
        // language, fetch the Wikidata entity label in the file's language and
        // emit it as "original_title". This preserves the native-language title
        // alongside the metadata-language title resolved above.
        if (!string.IsNullOrEmpty(request.FileLanguage) && _reconciler is not null)
        {
            var fileLang = request.FileLanguage.Split('-', '_')[0].ToLowerInvariant().Trim();
            var metaLang = language.Split('-', '_')[0].ToLowerInvariant().Trim();

            if (!string.Equals(fileLang, metaLang, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var fileLangEntities = await _reconciler
                        .GetEntitiesAsync([masterWorkQid], fileLang, ct)
                        .ConfigureAwait(false);

                    if (fileLangEntities.TryGetValue(masterWorkQid, out var fileLangEntity)
                        && !string.IsNullOrWhiteSpace(fileLangEntity.Label))
                    {
                        claims.Add(new ProviderClaim(MetadataFieldConstants.OriginalTitle, fileLangEntity.Label, ClaimConfidence.OriginalTitle));
                        _logger.LogDebug(
                            "{Provider}: original_title '{OriginalTitle}' emitted for {QID} in file language '{Lang}'",
                            Name, fileLangEntity.Label, masterWorkQid, fileLang);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex,
                        "{Provider}: failed to fetch original_title for {QID} in language '{Lang}'",
                        Name, masterWorkQid, fileLang);
                }
            }
        }

        // ── Wikidata aliases as alternate_title claims ────────────────────────
        // Wikidata entities carry aliases — common alternate names for the work
        // (e.g. "Sen to Chihiro no Kamikakushi" is an alias for "Spirited Away",
        // "1984" is an alias for "Nineteen Eighty-Four"). Emitting them as
        // alternate_title claims populates the FTS5 search index so users can find
        // works by any of their known names, including romanized CJK titles.
        //
        // The entity is fetched in the metadata language so aliases reflect the
        // configured display language. Each alias is emitted as a separate claim
        // at confidence 0.85 — lower than the primary title (0.98) so it does not
        // compete as the canonical title but is still indexed for search.
        //
        // Aliases already equal to an emitted title or original_title are skipped
        // to avoid redundant storage.
        if (_reconciler is not null)
        {
            try
            {
                var aliasEntities = await _reconciler
                    .GetEntitiesAsync([masterWorkQid], language, ct)
                    .ConfigureAwait(false);

                if (aliasEntities.TryGetValue(masterWorkQid, out var aliasEntity)
                    && aliasEntity.Aliases is { Count: > 0 })
                {
                    // Collect values already emitted as title or original_title to avoid duplicates.
                    var emittedTitles = claims
                        .Where(c => string.Equals(c.Key, MetadataFieldConstants.Title, StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(c.Key, MetadataFieldConstants.OriginalTitle, StringComparison.OrdinalIgnoreCase))
                        .Select(c => c.Value)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var aliasesEmitted = 0;
                    foreach (var alias in aliasEntity.Aliases)
                    {
                        if (string.IsNullOrWhiteSpace(alias)) continue;
                        if (emittedTitles.Contains(alias)) continue;

                        claims.Add(new ProviderClaim("alternate_title", alias, ClaimConfidence.AlternateTitle));
                        aliasesEmitted++;
                    }

                    if (aliasesEmitted > 0)
                        _logger.LogDebug(
                            "{Provider}: emitted {Count} alias(es) as alternate_title for {QID}",
                            Name, aliasesEmitted, masterWorkQid);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex,
                    "{Provider}: failed to fetch aliases for {QID}",
                    Name, masterWorkQid);
            }
        }

        // ── Child entity discovery ────────────────────────────────────────────
        // After Stage 2 resolves a QID for a TV show, music album, or comic series,
        // discover child entities (episodes, tracks, issues) from Wikidata and store
        // them as claims on the parent entity. This enables the Dashboard to show
        // episode/track/issue listings without additional API calls.
        if (_reconciler is not null
            && request.MediaType is MediaType.TV or MediaType.Music or MediaType.Comics)
        {
            try
            {
                var language2 = _configLoader?.LoadCore().Language.Metadata ?? "en";
                claims.AddRange(
                    await DiscoverChildEntitiesAsync(masterWorkQid, request.MediaType, language2, ct)
                        .ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "{Provider}: child entity discovery failed for {QID} ({MediaType}) — skipping",
                    Name, masterWorkQid, request.MediaType);
            }
        }

        return claims;
    }

    // ── Private: DiscoverChildEntitiesAsync ──────────────────────────────────

    private const int MaxChildEntities = 500;
    private const int MaxTvSeasons     = 20;

    /// <summary>
    /// Discovers child entities (TV episodes, music tracks, comic issues) for a parent QID
    /// and returns them as <see cref="ProviderClaim"/> entries. The count claims and a
    /// serialized JSON blob are stored using the existing metadata_claims system.
    /// Wrapped in try/catch by the caller — exceptions here never fail the main pipeline.
    /// </summary>
    private async Task<IReadOnlyList<ProviderClaim>> DiscoverChildEntitiesAsync(
        string parentQid,
        MediaType mediaType,
        string language,
        CancellationToken ct)
    {
        var claims = new List<ProviderClaim>();
        var cfg    = _config.ChildEntityDiscovery;

        switch (mediaType)
        {
            case MediaType.TV:
            {
                var tvCfg   = cfg.Tv;
                var seasons = await _reconciler!.GetChildEntitiesAsync(
                    parentQid,
                    tvCfg.SeasonProperty,
                    tvCfg.SeasonTypeFilter.Count > 0 ? tvCfg.SeasonTypeFilter : null,
                    ["P1476", "P1545"],
                    language,
                    ct).ConfigureAwait(false);

                if (seasons.Count == 0)
                    return claims;

                var seasonsCapped = seasons.Take(MaxTvSeasons).ToList();

                // Fetch episodes for each season concurrently (capped).
                var seasonResults = new List<(ChildEntityInfo Season, IReadOnlyList<ChildEntityInfo> Episodes)>();
                var totalEpisodes = 0;

                foreach (var season in seasonsCapped)
                {
                    if (totalEpisodes >= MaxChildEntities) break;

                    var episodes = await _reconciler!.GetChildEntitiesAsync(
                        season.EntityId,
                        tvCfg.EpisodeProperty,
                        tvCfg.EpisodeTypeFilter.Count > 0 ? tvCfg.EpisodeTypeFilter : null,
                        tvCfg.EpisodeProperties.Count > 0 ? tvCfg.EpisodeProperties : ["P1476", "P1545", "P577", "P2047", "P57"],
                        language,
                        ct).ConfigureAwait(false);

                    seasonResults.Add((season, episodes));
                    totalEpisodes += episodes.Count;
                }

                // Build JSON blob.
                var seasonNodes = new List<object>();
                foreach (var (season, episodes) in seasonResults)
                {
                    var episodeNodes = episodes.Select(ep => new
                    {
                        qid          = ep.EntityId,
                        title        = ep.Label,
                        ordinal      = ep.Ordinal,
                        air_date     = GetFirstStr(ep.Properties, "P577"),
                        duration_minutes = ParseDurationMinutes(GetFirstStr(ep.Properties, "P2047")),
                        director     = GetFirstLabel(ep.Properties, "P57"),
                    }).ToList();

                    seasonNodes.Add(new
                    {
                        qid     = season.EntityId,
                        label   = season.Label,
                        ordinal = season.Ordinal,
                        episodes = episodeNodes,
                    });
                }

                var jsonBlob = JsonSerializer.Serialize(new { seasons = seasonNodes });
                claims.Add(new ProviderClaim(MetadataFieldConstants.SeasonCount,      seasonsCapped.Count.ToString(), ClaimConfidence.WikidataProperty));
                claims.Add(new ProviderClaim(MetadataFieldConstants.EpisodeCount,     totalEpisodes.ToString(),       ClaimConfidence.WikidataProperty));
                claims.Add(new ProviderClaim(MetadataFieldConstants.ChildEntitiesJson, jsonBlob,                      ClaimConfidence.WikidataProperty));

                _logger.LogInformation(
                    "{Provider}: child entity discovery — TV {QID}: {SeasonCount} seasons, {EpisodeCount} episodes",
                    Name, parentQid, seasonsCapped.Count, totalEpisodes);
                break;
            }

            case MediaType.Music:
            {
                var musicCfg   = cfg.Music;
                var trackProps = musicCfg.TrackProperties.Count > 0
                    ? musicCfg.TrackProperties
                    : ["P1476", "P1545", "P2047", "P175", "P577"];

                // Try primary property first; fall back to secondary if no results.
                var tracks = await _reconciler!.GetChildEntitiesAsync(
                    parentQid,
                    musicCfg.TrackProperty,
                    musicCfg.TrackTypeFilter.Count > 0 ? musicCfg.TrackTypeFilter : null,
                    trackProps,
                    language,
                    ct).ConfigureAwait(false);

                if (tracks.Count == 0 && !string.IsNullOrEmpty(musicCfg.TrackPropertyFallback)
                    && musicCfg.TrackPropertyFallback != musicCfg.TrackProperty)
                {
                    tracks = await _reconciler!.GetChildEntitiesAsync(
                        parentQid,
                        musicCfg.TrackPropertyFallback,
                        musicCfg.TrackTypeFilter.Count > 0 ? musicCfg.TrackTypeFilter : null,
                        trackProps,
                        language,
                        ct).ConfigureAwait(false);
                }

                if (tracks.Count == 0)
                    return claims;

                var tracksCapped = tracks.Take(MaxChildEntities).ToList();

                var trackNodes = tracksCapped.Select(t => new
                {
                    qid              = t.EntityId,
                    title            = t.Label,
                    ordinal          = t.Ordinal,
                    duration_minutes = ParseDurationMinutes(GetFirstStr(t.Properties, "P2047")),
                    performer        = GetFirstLabel(t.Properties, "P175"),
                    release_date     = GetFirstStr(t.Properties, "P577"),
                }).ToList();

                var jsonBlob = JsonSerializer.Serialize(new { tracks = trackNodes });
                claims.Add(new ProviderClaim(MetadataFieldConstants.TrackCount,        tracksCapped.Count.ToString(), ClaimConfidence.WikidataProperty));
                claims.Add(new ProviderClaim(MetadataFieldConstants.ChildEntitiesJson, jsonBlob,                      ClaimConfidence.WikidataProperty));

                _logger.LogInformation(
                    "{Provider}: child entity discovery — Music {QID}: {TrackCount} tracks",
                    Name, parentQid, tracksCapped.Count);
                break;
            }

            case MediaType.Comics:
            {
                var comicsCfg   = cfg.Comics;
                var issueProps  = comicsCfg.IssueProperties.Count > 0
                    ? comicsCfg.IssueProperties
                    : ["P1476", "P1545", "P577"];

                var issues = await _reconciler!.GetChildEntitiesAsync(
                    parentQid,
                    comicsCfg.IssueProperty,
                    comicsCfg.IssueTypeFilter.Count > 0 ? comicsCfg.IssueTypeFilter : null,
                    issueProps,
                    language,
                    ct).ConfigureAwait(false);

                if (issues.Count == 0)
                    return claims;

                var issuesCapped = issues.Take(MaxChildEntities).ToList();

                var issueNodes = issuesCapped.Select(i => new
                {
                    qid              = i.EntityId,
                    title            = i.Label,
                    ordinal          = i.Ordinal,
                    publication_date = GetFirstStr(i.Properties, "P577"),
                }).ToList();

                var jsonBlob = JsonSerializer.Serialize(new { issues = issueNodes });
                claims.Add(new ProviderClaim(MetadataFieldConstants.IssueCount,        issuesCapped.Count.ToString(), ClaimConfidence.WikidataProperty));
                claims.Add(new ProviderClaim(MetadataFieldConstants.ChildEntitiesJson, jsonBlob,                      ClaimConfidence.WikidataProperty));

                _logger.LogInformation(
                    "{Provider}: child entity discovery — Comics {QID}: {IssueCount} issues",
                    Name, parentQid, issuesCapped.Count);
                break;
            }
        }

        return claims;
    }

    // ── Private: FetchPersonAsync ─────────────────────────────────────────────

    private async Task<IReadOnlyList<ProviderClaim>> FetchPersonAsync(
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        var name = request.PersonName ?? request.Author ?? request.Narrator;
        if (string.IsNullOrWhiteSpace(name))
            return [];

        // Use PreResolvedQid if provided.
        var qid = request.PreResolvedQid;

        if (string.IsNullOrWhiteSpace(qid))
        {
            // Build constraints from person role hints.
            var constraints = BuildPersonConstraints(request);
            var candidates  = await ReconcileAsync(name, constraints, ct).ConfigureAwait(false);

            if (candidates.Count == 0)
                return [];

            var top = candidates[0];
            if (top.Score < _config.Reconciliation.ReviewThreshold)
                return [];

            qid = top.Id;
        }

        // Extend with person properties.
        var personProps = _config.DataExtension.PersonProperties;
        var allProps    = personProps.Core
            .Concat(personProps.Social)
            .Concat(personProps.PenNames)
            .Distinct()
            .ToList();

        // Inject language-specific label (Len) and description (Den) magic suffixes.
        var language = _configLoader?.LoadCore().Language.Metadata ?? "en";
        allProps.Add($"L{language}");
        allProps.Add($"D{language}");

        if (allProps.Count == 0)
            return [new ProviderClaim(BridgeIdKeys.WikidataQid, qid, 1.0)];

        var extensions = await ExtendAsync([qid], allProps, ct).ConfigureAwait(false);
        extensions.TryGetValue(qid, out var extPersonProps);

        var claims = new List<ProviderClaim>
        {
            new(BridgeIdKeys.WikidataQid, qid, 1.0)
        };

        if (extPersonProps is not null)
            claims.AddRange(ExtensionToClaims(qid, extPersonProps, _config.DataExtension.PropertyLabels, isWork: false, castMemberLimit: 0, metadataLanguage: language));

        // ── Wikipedia description ─────────────────────────────────────────────
        // Fetch a rich Wikipedia description for this person using the resolved QID.
        // Failures never block — an empty list is returned and execution continues.
        var wikiPersonClaims = await FetchWikipediaDescriptionAsync(qid, language, ct)
            .ConfigureAwait(false);
        claims.AddRange(wikiPersonClaims);

        _logger.LogInformation("{Provider}: fetched {Count} person claims for QID {QID}",
            Name, claims.Count, qid);

        return claims;
    }

    // ── Private: Wikipedia description ───────────────────────────────────────────

    /// <summary>
    /// Fetches a rich Wikipedia description for the given Wikidata QID.
    /// Returns up to three claims: "description" (confidence 0.90), "wikipedia_url" (1.0),
    /// and optionally "plot_summary". Uses language fallback built into the library.
    /// Always returns an empty list on failure — never throws.
    /// </summary>
    private async Task<IReadOnlyList<ProviderClaim>> FetchWikipediaDescriptionAsync(
        string qid,
        string language,
        CancellationToken ct)
    {
        if (_reconciler is null || string.IsNullOrWhiteSpace(qid))
            return [];

        try
        {
            var lang = NormalizeLang(language);

            // Use language fallback: try requested language, then English
            var fallbackLangs = string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase)
                ? null
                : (IReadOnlyList<string>)["en"];

            var summaries = await _reconciler.GetWikipediaSummariesAsync(
                [qid], lang, fallbackLangs, ct).ConfigureAwait(false);

            var summary = summaries?.FirstOrDefault();
            if (summary is null || string.IsNullOrWhiteSpace(summary.Extract))
            {
                _logger.LogDebug("{Provider}: Wikipedia summary empty for {Qid}", Name, qid);
                return [];
            }

            var resolvedLang = summary.Language ?? lang;

            _logger.LogInformation("{Provider}: Wikipedia description for {Qid} ({Lang}): {Len} chars",
                Name, qid, resolvedLang, summary.Extract.Length);

            var resultClaims = new List<ProviderClaim>
            {
                new(MetadataFieldConstants.Description, StripLeadingMediaWikiHeadings(summary.Extract), ClaimConfidence.Description),
                new("wikipedia_url", summary.ArticleUrl ?? "", 1.0),
            };

            // Fetch Wikipedia Plot/Synopsis section for richer LLM analysis.
            try
            {
                var sections = await _reconciler.GetWikipediaSectionsAsync([qid], resolvedLang, ct)
                    .ConfigureAwait(false);

                if (sections is not null && sections.TryGetValue(qid, out var toc) && toc is not null)
                {
                    var plotSectionNames = new[] { "Plot", "Synopsis", "Plot summary", "Summary", "Premise", "Overview" };
                    var plotSection = toc.FirstOrDefault(s =>
                        plotSectionNames.Any(name => string.Equals(s.Title, name, StringComparison.OrdinalIgnoreCase)));

                    if (plotSection is not null)
                    {
                        var plotContent = await _reconciler.GetWikipediaSectionContentAsync(
                            qid, plotSection.Index, resolvedLang, ct).ConfigureAwait(false);

                        if (!string.IsNullOrWhiteSpace(plotContent))
                        {
                            resultClaims.Add(new ProviderClaim("plot_summary", StripLeadingMediaWikiHeadings(plotContent), ClaimConfidence.PlotSummary));
                            _logger.LogInformation(
                                "{Provider}: Wikipedia plot section '{Section}' for {Qid}: {Len} chars",
                                Name, plotSection.Title, qid, plotContent.Length);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "{Provider}: Wikipedia plot section fetch failed for {Qid}", Name, qid);
            }

            return resultClaims;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "{Provider}: Wikipedia description fetch failed for {Qid}", Name, qid);
            return [];
        }
    }

    /// <summary>
    /// Normalises a BCP-47 language tag to its primary subtag (e.g. "en-US" → "en").
    /// Returns "en" when the input is null or empty.
    /// </summary>
    private static string NormalizeLang(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
            return "en";
        var primary = lang.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
        return primary.ToLowerInvariant();
    }

    /// <summary>
    /// Strips MediaWiki section heading lines (e.g. "== Plot ==", "=== Synopsis ===") from
    /// the start of a description string and trims any resulting leading whitespace.
    /// Headings anywhere after the first non-heading line are left untouched.
    /// </summary>
    private static string StripLeadingMediaWikiHeadings(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var lines = text.Split('\n');
        var firstContentLine = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            // Match lines that are pure MediaWiki headings: ==...== with optional surrounding whitespace
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^={2,}\s*.+?\s*={2,}$"))
            {
                firstContentLine = i + 1;
            }
            else if (!string.IsNullOrWhiteSpace(trimmed))
            {
                // First non-heading, non-blank line — stop scanning
                break;
            }
        }

        return firstContentLine == 0
            ? text
            : string.Join('\n', lines.Skip(firstContentLine)).TrimStart();
    }

    /// <summary>
    /// Filters edition data by media type. Audiobooks get only audiobook-class editions;
    /// books get only non-audiobook editions. Other media types get all editions unfiltered.
    /// </summary>
    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>>> FilterEditionsByMediaType(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>>> editions,
        MediaType mediaType)
    {
        // Only filter for book-related media types.
        if (mediaType != MediaType.Audiobooks && mediaType != MediaType.Books)
            return editions;

        var audiobookClasses = _config.InstanceOfClasses.TryGetValue("Audiobooks", out var classes)
            ? new HashSet<string>(classes, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(["Q122731938", "Q106833962"], StringComparer.OrdinalIgnoreCase);

        var filtered = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (qid, props) in editions)
        {
            var isAudiobook = props.TryGetValue("P31", out var p31)
                && p31.Any(c => c.Value?.EntityId is not null && audiobookClasses.Contains(c.Value.EntityId!));

            if (mediaType == MediaType.Audiobooks && isAudiobook)
                filtered[qid] = props;
            else if (mediaType == MediaType.Books && !isAudiobook)
                filtered[qid] = props;
        }

        // Fall back to unfiltered if no editions match the filter (avoid losing all data).
        return filtered.Count > 0 ? filtered : editions;
    }


    private static Dictionary<string, string>? BuildTitleSearchConstraints(ProviderLookupRequest request)
    {
        var c = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(request.Author))
            c["P50"] = request.Author;
        return c.Count > 0 ? c : null;
    }

    /// <summary>
    /// Splits a multi-author/creator string on common separators: " &amp; ", " and ", ", ".
    /// Returns individual names, trimmed and non-empty.
    /// </summary>
    private static List<string> SplitAuthors(string authors)
    {
        var parts = System.Text.RegularExpressions.Regex.Split(
            authors,
            @"\s+&\s+|\s+and\s+|,\s*",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return parts
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
    }

    private Dictionary<string, string>? BuildPersonConstraints(ProviderLookupRequest request)
    {
        var c = new Dictionary<string, string>(StringComparer.Ordinal);

        // Use configured person_property_constraints from config.
        foreach (var (pCode, claimKey) in _config.Reconciliation.PersonPropertyConstraints)
        {
            // Map claimKey back to a request value if available.
            var val = claimKey switch
            {
                "notable_work_title" => request.Title,
                "occupation"         => request.PersonRole,
                _                    => null,
            };
            if (!string.IsNullOrWhiteSpace(val))
                c[pCode] = val;
        }

        return c.Count > 0 ? c : null;
    }

    // ── Private: Extension → ProviderClaim conversion ─────────────────────────

    private static IEnumerable<ProviderClaim> ExtensionToClaims(
        string entityQid,
        IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>> properties,
        Dictionary<string, string> propertyLabels,
        bool isWork,
        int castMemberLimit = 20,
        string? metadataLanguage = null)
    {
        foreach (var (pCode, rawClaims) in properties)
        {
            // Cap multi-valued cast member properties to prevent storing dozens of minor roles.
            var claims = rawClaims;
            if (string.Equals(pCode, "P161", StringComparison.OrdinalIgnoreCase)
                && castMemberLimit > 0 && rawClaims.Count > castMemberLimit)
            {
                claims = rawClaims.Take(castMemberLimit).ToList();
            }
            // ── Magic suffix handling (Len, Den, etc.) ──
            // L{lang} returns the entity label in the user's language.
            // D{lang} returns the entity description in the user's language.
            if (pCode.Length == 3 && pCode[0] == 'L' && char.IsLower(pCode[1]))
            {
                // Emit the Wikidata label as a title claim (for works) or name claim (for persons)
                // at lower confidence than the reconciliation label (0.98). The reconciliation label
                // is typically the shorter, more natural title (e.g. "Frankenstein") while L{lang}
                // can return the full formal title (e.g. "Frankenstein; or, The Modern Prometheus").
                if (claims.Count > 0 && !string.IsNullOrWhiteSpace(claims[0].Value?.RawValue))
                {
                    var labelClaimKey = isWork ? MetadataFieldConstants.Title : "name";
                    yield return new ProviderClaim(labelClaimKey, claims[0].Value!.RawValue!, ClaimConfidence.WikidataProperty);
                }
                continue;
            }

            if (pCode.Length == 3 && pCode[0] == 'D' && char.IsLower(pCode[1]))
            {
                if (claims.Count > 0 && !string.IsNullOrWhiteSpace(claims[0].Value?.RawValue))
                    yield return new ProviderClaim(MetadataFieldConstants.Description, claims[0].Value!.RawValue!, ClaimConfidence.Description);
                continue;
            }

            if (!propertyLabels.TryGetValue(pCode, out var claimKey))
                continue;

            // P18 (image) only for Person entities — and needs URL conversion.
            if (string.Equals(pCode, "P18", StringComparison.OrdinalIgnoreCase) && isWork)
                continue;

            // P31 (instance_of): for works, used internally for filtering only — skip claims.
            // For persons, emit as claims so pseudonym detection (Q127843/Q15632617) works.
            if (string.Equals(pCode, "P31", StringComparison.OrdinalIgnoreCase) && isWork)
                continue;

            // P1476 (title) — monolingual text; only take the first value to avoid
            // emitting every language translation as a separate claim.
            // When metadataLanguage is configured, prefer the value whose Language matches
            // the user's metadata language (e.g. "en"). Without this, the Wikidata API may
            // return the original Japanese or French title first for foreign-language works
            // (e.g. "千と千尋の神隠し" for Spirited Away when the user expects English).
            bool isMonolingualTitle = string.Equals(pCode, "P1476", StringComparison.OrdinalIgnoreCase);
            if (isMonolingualTitle && !string.IsNullOrWhiteSpace(metadataLanguage) && claims.Count > 1)
            {
                // Prefer the value in the user's metadata language; fall through to the
                // original order (first value) if no match is found.
                var langNorm = metadataLanguage.Split('-', '_')[0].ToLowerInvariant();
                var preferredClaim = claims.FirstOrDefault(c =>
                    !string.IsNullOrWhiteSpace(c.Value?.Language)
                    && c.Value!.Language!.Split('-', '_')[0].Equals(langNorm, StringComparison.OrdinalIgnoreCase));
                if (preferredClaim is not null)
                    claims = [preferredClaim];
            }

            foreach (var claim in claims)
            {
                // Special handling for P18: convert Commons filename to URL.
                if (string.Equals(pCode, "P18", StringComparison.OrdinalIgnoreCase))
                {
                    var filename = claim.Value?.RawValue;
                    if (!string.IsNullOrWhiteSpace(filename))
                    {
                        var commonsUrl = $"https://commons.wikimedia.org/wiki/Special:FilePath/{Uri.EscapeDataString(filename)}";
                        yield return new ProviderClaim("headshot_url", commonsUrl, ClaimConfidence.HeadshotUrl);
                    }
                    continue;
                }

                // Determine confidence and string value.
                // GetPropertiesAsync (v0.8+) calls ResolveClaimsEntityLabelsAsync internally,
                // populating EntityLabel for all entity references. v0.9+ made EntityLabel
                // publicly settable, so JSON cache round-trips preserve labels correctly.
                (string? strVal, double confidence) = ExtractValueAndConfidence(claim, pCode);

                // P179 (part_of_the_series): skip award lists, polls, and rankings.
                if (string.Equals(pCode, "P179", StringComparison.OrdinalIgnoreCase))
                {
                    var seriesLabel = strVal ?? claim.Value?.EntityLabel ?? claim.Value?.RawValue;
                    if (!string.IsNullOrWhiteSpace(seriesLabel) && IsLikelyAwardList(seriesLabel))
                        continue;
                }

                if (!string.IsNullOrWhiteSpace(strVal))
                {
                    // Normalize bridge ID values (strip ISBN dashes, uppercase ASINs, etc.)
                    if (IsBridgeProperty(pCode))
                    {
                        var normalized = IdentifierNormalizationService.NormalizeRaw(pCode, strVal);
                        if (!string.IsNullOrWhiteSpace(normalized))
                            strVal = normalized;
                    }
                    yield return new ProviderClaim(claimKey, strVal, confidence);
                }

                // Emit individual companion _qid claim per entity value.
                // Prefer EntityLabel (populated by library v0.8.0), then RawValue, then EntityId.
                if (claim.Value?.EntityId is not null)
                {
                    var label = claim.Value.EntityLabel ?? claim.Value.RawValue ?? claim.Value.EntityId;
                    yield return new ProviderClaim($"{claimKey}_qid", $"{claim.Value.EntityId}::{label}", ClaimConfidence.EntityQidReference);
                }

                // Only emit the first value for monolingual title properties.
                if (isMonolingualTitle) break;
            }
        }
    }

    private static (string? value, double confidence) ExtractValueAndConfidence(
        WikidataClaim claim, string pCode)
    {
        var val = claim.Value;
        if (val is null) return (null, 0.0);

        // Date values.
        if (val.Kind == WikidataValueKind.Time)
        {
            var year = ExtractYear(val.RawValue);
            return (year, ClaimConfidence.AlternateTitle);
        }

        // Entity reference.
        if (val.Kind == WikidataValueKind.EntityId)
        {
            var isBridge = IsBridgeProperty(pCode);
            if (isBridge)
                return (val.EntityId, ClaimConfidence.BridgeId);

            // P50 (author) claims from Wikidata get a reduced confidence (0.75) so that
            // an embedded author from file metadata (confidence 1.0) always wins in the
            // priority cascade. This preserves pen names: when the EPUB credits a pen name
            // like "James S. A. Corey" but Wikidata P50 lists the real authors, the file's
            // credited author takes precedence. The pen name preservation block in
            // FetchWorkAsync re-keys P50 real-name claims when a mismatch is detected —
            // this reduced confidence acts as a second safety net for that same scenario.
            if (string.Equals(pCode, "P50", StringComparison.OrdinalIgnoreCase))
                return (val.EntityLabel ?? val.RawValue ?? val.EntityId, ClaimConfidence.WikidataAuthorRaw);

            // For other entity references (series, director, etc.) prefer EntityLabel, then RawValue.
            return (val.EntityLabel ?? val.RawValue ?? val.EntityId, ClaimConfidence.WikidataProperty);
        }

        // Quantity values.
        if (val.Kind == WikidataValueKind.Quantity)
            return (val.Amount?.ToString(), ClaimConfidence.Duration);

        // Plain string / monolingual text.
        if (!string.IsNullOrWhiteSpace(val.RawValue))
            return (val.RawValue, ClaimConfidence.WikidataProperty);

        return (null, 0.0);
    }

    private static bool IsBridgeProperty(string pCode) => pCode switch
    {
        "P212"  => true, // isbn_13
        "P957"  => true, // isbn_10
        "P5749" => true, // asin
        "P4947" => true, // tmdb_movie_id
        "P345"  => true, // imdb_id
        "P6395" => true, // apple_books_id
        "P5905" => true, // comic_vine_id
        "P434"  => true, // musicbrainz_artist_id
        "P436"  => true, // musicbrainz_release_id
        "P5842" => true, // apple_podcasts_id
        _       => false,
    };

    /// <summary>
    /// Returns true when a P179 series label looks like an award list, poll, or ranking
    /// rather than a narrative series. These should not be emitted as "series" claims.
    /// </summary>
    private static bool IsLikelyAwardList(string label)
    {
        var lower = label.ToLowerInvariant();
        string[] skipPatterns =
        [
            "greatest", "best of", "top ", "100 ", " 100", "poll", "ranking",
            "award", "bfi", "sight & sound", "sight and sound", "afi",
            "all-time", "all time", "most influential", "canonical"
        ];
        return skipPatterns.Any(p => lower.Contains(p));
    }

    private static string? ExtractYear(string isoDate)
    {
        if (string.IsNullOrWhiteSpace(isoDate))
            return null;

        // Handle "+1965-08-01T00:00:00Z" and "1965-01-01T00:00:00Z" formats.
        var s = isoDate.TrimStart('+');
        if (s.Length >= 4 && int.TryParse(s[..4], out var year) && year > 0)
            return year.ToString();

        return null;
    }

    // ── Private: Extension helpers ────────────────────────────────────────────

    /// <summary>
    /// Converts a Wikidata P2047 duration value (seconds as string) to whole minutes.
    /// Returns null when the value is missing or unparseable.
    /// </summary>
    private static int? ParseDurationMinutes(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;
        if (double.TryParse(rawValue, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            return (int)Math.Round(seconds / 60.0);
        return null;
    }

    private static string? GetFirstLabel(
        IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>> properties,
        string pCode)
    {
        return properties.TryGetValue(pCode, out var claims) && claims.Count > 0
            ? claims[0].Value?.RawValue ?? claims[0].Value?.EntityId
            : null;
    }

    private static string? GetFirstStr(
        IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>> properties,
        string pCode)
    {
        if (!properties.TryGetValue(pCode, out var claims) || claims.Count == 0)
            return null;
        var val = claims[0].Value;
        if (val is null) return null;
        return val.RawValue;
    }

    // ── Private: Cache key + SHA-256 ─────────────────────────────────────────

    private string BuildCacheKey(string input) =>
        $"{_providerId}:{ComputeSha256(input)}";

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ── Public: Entity staleness check ───────────────────────────────────────

    /// <summary>
    /// Lightweight staleness check: compares stored revision IDs against current Wikidata
    /// revision IDs. Returns only QIDs that have changed since the stored revision.
    /// Used by the 30-day refresh cycle to skip expensive re-fetches for unchanged entities.
    /// </summary>
    public async Task<IReadOnlyList<string>> CheckEntityStalenessAsync(
        IReadOnlyDictionary<string, long> storedRevisions,
        CancellationToken ct = default)
    {
        if (_reconciler is null || storedRevisions.Count == 0)
            return [];

        try
        {
            var qids = storedRevisions.Keys.ToList();
            var currentRevisions = await _reconciler.GetRevisionIdsAsync(qids, ct).ConfigureAwait(false);

            var staleQids = new List<string>();
            foreach (var (qid, storedRevId) in storedRevisions)
            {
                if (!currentRevisions.TryGetValue(qid, out var current))
                {
                    // Entity not found — treat as stale (may have been deleted/merged)
                    staleQids.Add(qid);
                    continue;
                }

                if (current.RevisionId != storedRevId)
                    staleQids.Add(qid);
            }

            _logger.LogDebug("{Provider}: staleness check — {Stale}/{Total} entities have changed",
                Name, staleQids.Count, storedRevisions.Count);

            return staleQids;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Provider}: staleness check failed — treating all as stale", Name);
            return storedRevisions.Keys.ToList();
        }
    }

}
