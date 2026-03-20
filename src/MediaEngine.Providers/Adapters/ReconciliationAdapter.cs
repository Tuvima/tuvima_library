using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Contracts;
using MediaEngine.Domain.Services;
using MediaEngine.Storage.Models;

namespace MediaEngine.Providers.Adapters;

/// <summary>
/// Wikidata adapter using the OpenRefine Reconciliation API and Data Extension API.
///
/// <para>
/// This adapter replaces the SPARQL-based WikidataAdapter. Instead of custom SPARQL queries,
/// it uses the standard OpenRefine reconciliation protocol supported by the Wikidata reconciliation
/// service at wikidata.reconci.link. This approach is more maintainable and does not require
/// a deep SPARQL implementation.
/// </para>
///
/// <para>
/// Primary operations:
/// <list type="bullet">
///   <item>Reconcile entity names to Wikidata QIDs via the Reconciliation API.</item>
///   <item>Extend QIDs with structured property values via the Data Extension API.</item>
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
    private readonly IWikibaseApiService? _wikibaseApi;

    // Throttle: per-instance semaphore + timestamp gap.
    private readonly SemaphoreSlim _throttle;
    private DateTime _lastCallUtc = DateTime.MinValue;

    // Parsed once at construction.
    private readonly Guid _providerId;

    // Runtime-learned P279 class→media type mappings (in-memory, not persisted).
    private readonly ConcurrentDictionary<string, LearnedClassEntry> _learnedClasses = new();

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
        IWikibaseApiService? wikibaseApi = null)
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
        _wikibaseApi   = wikibaseApi;

        _throttle = new SemaphoreSlim(
            Math.Max(1, config.MaxConcurrency),
            Math.Max(1, config.MaxConcurrency));

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
            var candidates  = await ReconcileAsync(
                request.Title, constraints, ct).ConfigureAwait(false);

            if (candidates.Count == 0)
                return [];

            // Optionally filter by media type using P31.
            if (request.MediaType != MediaType.Unknown)
            {
                var filtered = await FilterByMediaTypeAsync(
                    candidates, request.MediaType, ct).ConfigureAwait(false);

                // For audiobooks: if audiobook-specific filtering eliminates everything,
                // fall back to Books classes (an audiobook is a format of a literary work).
                if (filtered.Count == 0 && request.MediaType == MediaType.Audiobooks)
                {
                    _logger.LogDebug("{Provider}: audiobook filter returned 0 results, falling back to Books classes",
                        Name);
                    filtered = await FilterByMediaTypeAsync(
                        candidates, MediaType.Books, ct).ConfigureAwait(false);
                }

                candidates = filtered.Count > 0 ? filtered : candidates;
            }

            // For audiobook searches: discover audiobook editions via P747 for work-level results.
            // Edition results go first (they're more specific), work fallbacks come after.
            if (request.MediaType == MediaType.Audiobooks)
            {
                var editionResults = new List<SearchResultItem>();
                var workResults    = new List<SearchResultItem>();

                foreach (var c in candidates.Take(limit))
                {
                    // Try to discover audiobook editions for this candidate.
                    var editions = await DiscoverAudiobookEditionsAsync(c.QID, null, ct)
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
                                Title:          c.Label,
                                Author:         null,
                                Description:    editionDesc,
                                Year:           null,
                                ThumbnailUrl:   null,
                                ProviderItemId: ed.EditionQid ?? c.QID,
                                Confidence:     c.Score / 100.0,
                                ProviderName:   Name,
                                ResultType:     "audiobook_edition"));
                        }
                    }

                    // Always include the work as a fallback.
                    workResults.Add(new SearchResultItem(
                        Title:          c.Label,
                        Author:         null,
                        Description:    c.Description,
                        Year:           null,
                        ThumbnailUrl:   null,
                        ProviderItemId: c.QID,
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
                    Title:          c.Label,
                    Author:         null,
                    Description:    c.Description,
                    Year:           null,
                    ThumbnailUrl:   null,
                    ProviderItemId: c.QID,
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
    /// </summary>
    /// <param name="query">The entity name to reconcile (e.g. "Dune", "Frank Herbert").</param>
    /// <param name="propertyConstraints">Optional P-code → value constraints to narrow the search.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<ReconciliationCandidate>> ReconcileAsync(
        string query,
        Dictionary<string, string>? propertyConstraints = null,
        CancellationToken ct = default)
    {
        var requests = new List<ReconcileRequest>
        {
            new("q0", query, propertyConstraints)
        };

        var results = await ReconcileBatchAsync(requests, ct).ConfigureAwait(false);
        return results.TryGetValue("q0", out var candidates) ? candidates : [];
    }

    /// <summary>
    /// Reconciles multiple entities in a single HTTP call (up to <c>batch_size</c> per call).
    /// Automatically splits large batches.
    /// </summary>
    /// <param name="requests">List of reconciliation requests.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Dictionary keyed by <see cref="ReconcileRequest.QueryId"/>.</returns>
    public async Task<Dictionary<string, IReadOnlyList<ReconciliationCandidate>>> ReconcileBatchAsync(
        IReadOnlyList<ReconcileRequest> requests,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, IReadOnlyList<ReconciliationCandidate>>(
            StringComparer.Ordinal);

        if (requests.Count == 0)
            return result;

        var batchSize = Math.Max(1, _config.BatchSize);

        for (int offset = 0; offset < requests.Count; offset += batchSize)
        {
            var batch = requests.Skip(offset).Take(batchSize).ToList();
            var batchResult = await ExecuteReconcileBatchAsync(batch, ct).ConfigureAwait(false);
            foreach (var (k, v) in batchResult)
                result[k] = v;
        }

        return result;
    }

    /// <summary>
    /// Extends a set of QIDs with property values via the Data Extension API.
    /// </summary>
    /// <param name="qids">Wikidata Q-identifiers to extend.</param>
    /// <param name="propertyCodes">P-codes to fetch (e.g. ["P50", "P577"]).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<ExtensionResult>> ExtendAsync(
        IReadOnlyList<string> qids,
        IReadOnlyList<string> propertyCodes,
        CancellationToken ct = default)
    {
        if (qids.Count == 0 || propertyCodes.Count == 0)
            return [];

        var endpoint = _config.Endpoints.Reconciliation;
        var extendPayload = BuildExtendPayload(qids, propertyCodes);
        // Language must be part of the cache key: Wikidata returns language-specific
        // label/description values (e.g. Len, Den) and the same QID+properties payload
        // will yield different results for "en" vs "es". Without the language in the key,
        // a cached response from a prior run with the wrong language is reused for all
        // subsequent queries, causing foreign-language titles and Cyrillic names to persist.
        var language = _configLoader?.LoadCore().Language ?? "en";
        var cacheKey = BuildCacheKey($"extend:{language}:{extendPayload}");

        // Check cache.
        if (_responseCache is not null)
        {
            var cached = await _responseCache.FindAsync(cacheKey, ct).ConfigureAwait(false);
            if (cached is not null)
            {
                _logger.LogDebug("{Provider}: extend cache HIT", Name);
                return ParseExtensionResponse(cached.ResponseJson, _config.DataExtension.PropertyLabels);
            }
        }
        var responseBody = await PostFormAsync(
            endpoint, $"extend={Uri.EscapeDataString(extendPayload)}&uselang={language}", ct)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(responseBody))
            return [];

        // Cache the response.
        if (_responseCache is not null)
        {
            var queryHash = ComputeSha256(extendPayload);
            await _responseCache.UpsertAsync(
                cacheKey, _providerId.ToString(), queryHash,
                responseBody, null, _config.CacheTtlHours, ct)
                .ConfigureAwait(false);
        }

        return ParseExtensionResponse(responseBody, _config.DataExtension.PropertyLabels);
    }

    /// <summary>
    /// Filters reconciliation candidates by media type using P31 (instance_of) lookups.
    /// Walks P279 (subclass_of) up to 3 levels for unknown classes, caching learned mappings.
    /// Candidates with no P31 data that match any expected class are retained.
    /// </summary>
    public async Task<IReadOnlyList<ReconciliationCandidate>> FilterByMediaTypeAsync(
        IReadOnlyList<ReconciliationCandidate> candidates,
        MediaType mediaType,
        CancellationToken ct = default)
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

        // Build the exclusion set — entity types that should never match for this media type
        // (e.g. franchises, multimedia series) even if they walk up to an expected class via P279.
        var excludedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_config.ExcludeClasses.TryGetValue(mediaTypeKey, out var excludedClasses)
            && excludedClasses.Count > 0)
        {
            foreach (var qid in excludedClasses)
                excludedSet.Add(qid);
        }

        var qids = candidates.Select(c => c.QID).ToList();

        // Extend all candidates with P31.
        var extensions = await ExtendAsync(qids, ["P31"], ct).ConfigureAwait(false);
        var p31ByQid   = extensions.ToDictionary(e => e.QID, e => e.Properties, StringComparer.OrdinalIgnoreCase);

        var filtered = new List<ReconciliationCandidate>();
        foreach (var candidate in candidates)
        {
            if (!p31ByQid.TryGetValue(candidate.QID, out var props)
                || !props.TryGetValue("P31", out var p31Values)
                || p31Values.Count == 0)
            {
                // No P31 data — cannot filter, keep candidate.
                filtered.Add(candidate);
                continue;
            }

            var instanceOfQids = p31Values
                .Where(v => v.Id is not null)
                .Select(v => v.Id!)
                .ToList();

            // Reject candidates whose P31 directly matches an excluded class —
            // these are franchises, multimedia series, and other meta-types that should
            // never be returned as the canonical work even if they share a title.
            if (excludedSet.Count > 0 && instanceOfQids.Any(qid => excludedSet.Contains(qid)))
            {
                _logger.LogDebug(
                    "{Provider}: candidate {QID} '{Label}' excluded — P31 matches exclude_classes for {MediaType}",
                    Name, candidate.QID, candidate.Label, mediaTypeKey);
                continue;
            }

            var matched = false;
            foreach (var classQid in instanceOfQids)
            {
                if (expectedSet.Contains(classQid))
                {
                    matched = true;
                    break;
                }

                // Walk P279 subclass_of up to 3 levels.
                if (await WalkSubclassAsync(classQid, expectedSet, mediaTypeKey, 3, ct).ConfigureAwait(false))
                {
                    matched = true;
                    break;
                }
            }

            if (matched)
                filtered.Add(candidate);
        }

        _logger.LogDebug("{Provider}: FilterByMediaType({MediaType}) kept {Kept}/{Total} candidates",
            Name, mediaTypeKey, filtered.Count, candidates.Count);

        return filtered;
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
        if (string.IsNullOrWhiteSpace(masterWorkQid))
            return null;

        try
        {
            // Step 2a: Fetch P747 (has_edition_or_translation) from the master work.
            var workExtensions = await ExtendAsync([masterWorkQid], ["P747"], ct).ConfigureAwait(false);
            var workData = workExtensions.FirstOrDefault(e =>
                string.Equals(e.QID, masterWorkQid, StringComparison.OrdinalIgnoreCase));

            if (workData is null
                || !workData.Properties.TryGetValue("P747", out var editionValues)
                || editionValues.Count == 0)
            {
                _logger.LogDebug("{Provider}: no P747 editions found on master work {QID} — audiobook pivot skipped",
                    Name, masterWorkQid);
                return null;
            }

            var editionQids = editionValues
                .Where(v => v.Id is not null)
                .Select(v => v.Id!)
                .Distinct()
                .ToList();

            if (editionQids.Count == 0)
                return null;

            _logger.LogDebug("{Provider}: master work {QID} has {Count} edition(s) — filtering for audiobook class",
                Name, masterWorkQid, editionQids.Count);

            // Step 2b: Extend all editions with P31 + P175 (narrator) for filtering and ranking.
            var editionData = await ExtendAsync(editionQids, ["P31", "P175"], ct).ConfigureAwait(false);

            // Audiobook class QIDs from config (Q122731938 = audiobook, Q106833962 = audiobook edition).
            var audiobookClasses = _config.InstanceOfClasses.TryGetValue("Audiobooks", out var classes)
                ? new HashSet<string>(classes, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(["Q122731938", "Q106833962"], StringComparer.OrdinalIgnoreCase);

            // Collect audiobook editions with optional narrator for ranking.
            var audiobookEditions = new List<(string QID, string? Narrator)>();
            foreach (var edition in editionData)
            {
                if (!edition.Properties.TryGetValue("P31", out var p31Values))
                    continue;

                var isAudiobook = p31Values.Any(v =>
                    v.Id is not null && audiobookClasses.Contains(v.Id!));

                if (!isAudiobook)
                    continue;

                var narrator = GetFirstLabel(edition.Properties, "P175");
                audiobookEditions.Add((edition.QID, narrator));
            }

            if (audiobookEditions.Count == 0)
            {
                _logger.LogDebug("{Provider}: no audiobook-class editions found for master work {QID}",
                    Name, masterWorkQid);
                return null;
            }

            // Step 2c: If narrator hint provided and multiple editions exist, rank by narrator match.
            string selectedQid;
            if (!string.IsNullOrWhiteSpace(narratorHint) && audiobookEditions.Count > 1)
            {
                selectedQid = audiobookEditions
                    .OrderByDescending(e => _fuzzy.ComputeTokenSetRatio(narratorHint, e.Narrator ?? ""))
                    .First()
                    .QID;

                _logger.LogInformation(
                    "{Provider}: audiobook pivot — selected edition {QID} for master work {MasterQID} " +
                    "(narrator hint: '{Narrator}', {Count} candidates ranked)",
                    Name, selectedQid, masterWorkQid, narratorHint, audiobookEditions.Count);
            }
            else
            {
                selectedQid = audiobookEditions[0].QID;
                _logger.LogInformation(
                    "{Provider}: audiobook pivot — resolved edition {QID} for master work {MasterQID}",
                    Name, selectedQid, masterWorkQid);
            }

            return selectedQid;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "{Provider}: ResolveAudiobookEditionQidAsync failed for master work {QID}",
                Name, masterWorkQid);
            return null;
        }
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
        if (string.IsNullOrWhiteSpace(workQid))
            return [];

        try
        {
            // Extend the work with P747 to get edition QIDs.
            var workExtensions = await ExtendAsync([workQid], ["P747"], ct).ConfigureAwait(false);
            var workData       = workExtensions.FirstOrDefault(e =>
                string.Equals(e.QID, workQid, StringComparison.OrdinalIgnoreCase));

            if (workData is null
                || !workData.Properties.TryGetValue("P747", out var editionValues)
                || editionValues.Count == 0)
                return [];

            var editionQids = editionValues
                .Where(v => v.Id is not null)
                .Select(v => v.Id!)
                .ToList();

            if (editionQids.Count == 0)
                return [];

            // Extend editions with audiobook-relevant properties.
            var audiobookProps = _config.DataExtension.AudiobookEditionProperties;
            if (audiobookProps.Count == 0)
                audiobookProps = ["P175", "P2047", "P5749", "P123", "P31"];

            var editionExtensions = await ExtendAsync(editionQids, [.. audiobookProps, "P31"], ct)
                .ConfigureAwait(false);

            // Filter to audiobook-class editions only.
            var audiobookClasses = _config.InstanceOfClasses.TryGetValue("Audiobooks", out var classes)
                ? new HashSet<string>(classes, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(["Q122731938", "Q106833962"], StringComparer.OrdinalIgnoreCase);

            var results = new List<AudiobookEditionData>();
            foreach (var edition in editionExtensions)
            {
                if (!edition.Properties.TryGetValue("P31", out var p31)
                    || !p31.Any(v => v.Id is not null && audiobookClasses.Contains(v.Id!)))
                    continue;

                // Extract audiobook edition metadata.
                var narrator  = GetFirstLabel(edition.Properties, "P175");
                var duration  = GetFirstStr(edition.Properties, "P2047");
                var asin      = GetFirstStr(edition.Properties, "P5749");
                var publisher = GetFirstLabel(edition.Properties, "P123");

                results.Add(new AudiobookEditionData(edition.QID, null, narrator, duration, asin, publisher));
            }

            // Rank editions by narrator match if hint available.
            if (!string.IsNullOrWhiteSpace(narratorHint) && results.Count > 1)
            {
                results = results
                    .OrderByDescending(e => _fuzzy.ComputeTokenSetRatio(
                        narratorHint, e.Narrator ?? ""))
                    .ToList();
            }

            _logger.LogDebug("{Provider}: discovered {Count} audiobook edition(s) for {QID}",
                Name, results.Count, workQid);

            return results;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Provider}: DiscoverAudiobookEditionsAsync failed for {QID}", Name, workQid);
            return [];
        }
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

            // Clean audiobook titles before reconciliation — strip "(Unabridged)", ": A Novel", etc.
            var searchTitle = request.MediaType == MediaType.Audiobooks
                ? CleanAudiobookTitle(request.Title)
                : request.Title;

            var constraints = BuildTitleSearchConstraints(request);
            var candidates  = await ReconcileAsync(searchTitle, constraints, ct).ConfigureAwait(false);

            if (candidates.Count == 0)
            {
                _logger.LogDebug("{Provider}: no reconciliation candidates for '{Title}'",
                    Name, request.Title);
                return [];
            }

            // Filter by media type when known.
            IReadOnlyList<ReconciliationCandidate> filtered = candidates;
            if (request.MediaType != MediaType.Unknown)
            {
                filtered = await FilterByMediaTypeAsync(candidates, request.MediaType, ct)
                    .ConfigureAwait(false);
                if (filtered.Count == 0)
                    filtered = candidates; // Fall back to unfiltered if nothing passes
            }

            // Accept the top candidate if it meets the auto-accept threshold.
            var top = filtered[0];
            if (top.Score < _config.Reconciliation.ReviewThreshold)
            {
                _logger.LogDebug(
                    "{Provider}: top candidate '{Label}' ({QID}) score {Score} below review threshold",
                    Name, top.Label, top.QID, top.Score);
                return [];
            }

            // Fuzzy title verification — prevent blind acceptance of wrong Wikidata entities.
            // A high Wikidata reconciliation score does not guarantee the title matches.
            // Audiobooks get a relaxed threshold (0.50) because file titles often contain
            // extra text like narrator names, edition info, or subtitle variations.
            if (!string.IsNullOrWhiteSpace(request.Title))
            {
                var compareTitle = request.MediaType == MediaType.Audiobooks
                    ? CleanAudiobookTitle(request.Title)
                    : request.Title;
                var fuzzyThreshold = request.MediaType == MediaType.Audiobooks ? 0.50 : 0.60;
                var titleMatch = _fuzzy.ComputeTokenSetRatio(compareTitle, top.Label);
                if (titleMatch < fuzzyThreshold)
                {
                    _logger.LogDebug(
                        "{Provider}: title mismatch for top candidate '{Label}' ({QID}) — " +
                        "fuzzy score {Score:F2} < {Threshold:F2} against local title '{LocalTitle}'",
                        Name, top.Label, top.QID, titleMatch, fuzzyThreshold, compareTitle);
                    return [];
                }
            }

            qid = top.QID;
            reconciliationLabel = top.Label;
        }

        // ── Step 2 & 3: Audiobook Edition Pivot ──────────────────────────────────
        // For audiobooks, the master work QID (e.g. Dune = Q190192) does not carry
        // an audiobook ISBN — only its audiobook edition item (P747 + P31 filter) does.
        // Pivot to the edition QID so that Data Extension returns the audiobook-specific
        // P212 (ISBN-13) and other edition-level bridge IDs (P5749 / ASIN, P3861 / Apple Books ID).
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
        var language = _configLoader?.LoadCore().Language ?? "en";

        var claims = new List<ProviderClaim>
        {
            // Always emit the master work QID as the canonical wikidata_qid.
            // This ensures Hub grouping is based on the creative work, not the edition.
            new("wikidata_qid", masterWorkQid, 1.0)
        };

        // When we pivoted to an audiobook edition, also emit the edition QID as a separate
        // claim so other parts of the pipeline can reference it (e.g. for cover art lookup).
        if (audiobookEditionQid is not null)
            claims.Add(new ProviderClaim("audiobook_edition_qid", audiobookEditionQid, 1.0));

        // Emit the reconciliation match label as a title claim (lower confidence than L{lang}).
        if (!string.IsNullOrWhiteSpace(reconciliationLabel))
            claims.Add(new ProviderClaim("title", reconciliationLabel, 0.93));

        // extData holds the master work extension result — used by pen name detection and
        // edition bridge ID resolution below. Set inside both branches.
        ExtensionResult? extData = null;

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
            extData = masterExtensions.FirstOrDefault(e =>
                string.Equals(e.QID, masterWorkQid, StringComparison.OrdinalIgnoreCase));
            if (extData is not null)
                claims.AddRange(ExtensionToClaims(extData, _config.DataExtension.PropertyLabels, isWork: true));

            // Edition: edition-specific properties + bridges
            var editionProps = (_config.DataExtension.AudiobookEditionProperties ?? [])
                .Concat(workProps.Bridges)
                .Concat(workProps.Editions)
                .Distinct()
                .ToList();

            if (editionProps.Count > 0)
            {
                var editionExtensions = await ExtendAsync([audiobookEditionQid], editionProps, ct).ConfigureAwait(false);
                var editionData = editionExtensions.FirstOrDefault(e =>
                    string.Equals(e.QID, audiobookEditionQid, StringComparison.OrdinalIgnoreCase));
                if (editionData is not null)
                    claims.AddRange(ExtensionToClaims(editionData, _config.DataExtension.PropertyLabels, isWork: true));
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
                return [new ProviderClaim("wikidata_qid", masterWorkQid, 1.0)];

            var extensions = await ExtendAsync([qid], allProps, ct).ConfigureAwait(false);
            extData = extensions.FirstOrDefault(e =>
                string.Equals(e.QID, qid, StringComparison.OrdinalIgnoreCase));

            if (extData is not null)
                claims.AddRange(ExtensionToClaims(extData, _config.DataExtension.PropertyLabels, isWork: true));
        }

        // Fix entity reference labels that may be in wrong language.
        // The Data Extension API returns entity names in Wikidata's "best" language,
        // not necessarily the configured language. Re-fetch labels for person entities.
        claims = await ResolveEntityLabelsInLanguageAsync(claims, language, ct).ConfigureAwait(false);

        // ── Pen name detection via P50 + P742 ────────────────────────────────
        // When Wikidata P50 lists 2+ authors (e.g. Daniel Abraham + Ty Franck for
        // "The Expanse"), their individual real names become the author claims. But if
        // all those authors share a common pen name (P742), the work was *published*
        // under that pen name. Emit a high-confidence pen name claim (0.95) so it
        // beats the individual real-name claims (0.90) in the scoring election.
        //
        // Wikidata P742 (pseudonym) is stored as an item reference (entity link),
        // not a plain string. The pen name entity has its own QID (e.g. Q18614905
        // for "James S. A. Corey"). We match shared pen names by QID to avoid
        // language/label mismatches, then resolve the display label for the claim.
        if (extData is not null
            && extData.Properties.TryGetValue("P50", out var authorRefs)
            && authorRefs.Count >= 2)
        {
            try
            {
                var authorQids = authorRefs
                    .Where(v => v.Id is not null)
                    .Select(v => v.Id!)
                    .Distinct()
                    .ToList();

                if (authorQids.Count >= 2)
                {
                    // Fetch P742 (pseudonym/pen name) for each co-author.
                    var penNameExtensions = await ExtendAsync(authorQids, ["P742"], ct)
                        .ConfigureAwait(false);

                    // Collect all P742 values per author, keyed by their canonical identifier.
                    // P742 values are Wikidata item references (Id/Label), not plain strings (Str).
                    // We use a (key → display label) map: key is the QID when available so that
                    // the intersection is stable regardless of label language; display label is
                    // the human-readable pen name string used for the final claim value.
                    var penNamesByAuthor = penNameExtensions
                        .Where(e => e.Properties.TryGetValue("P742", out var vals) && vals.Count > 0)
                        .ToDictionary(
                            e => e.QID,
                            e => e.Properties["P742"]
                                .Where(v => v.Str is not null || v.Id is not null)
                                .Select(v =>
                                {
                                    // Use QID as the canonical key for intersection; prefer label as display name.
                                    var key = v.Id ?? v.Str!;
                                    var display = v.Label ?? v.Str ?? v.Id!;
                                    return (key, display);
                                })
                                .DistinctBy(t => t.key, StringComparer.OrdinalIgnoreCase)
                                .ToList(),
                            StringComparer.OrdinalIgnoreCase);

                    if (penNamesByAuthor.Count >= 2)
                    {
                        // Find pen name keys shared by all co-authors.
                        var firstAuthorKeys = new HashSet<string>(
                            penNamesByAuthor.Values.First().Select(t => t.key),
                            StringComparer.OrdinalIgnoreCase);

                        var sharedKeys = penNamesByAuthor.Values
                            .Skip(1)
                            .Aggregate(
                                firstAuthorKeys,
                                (acc, next) =>
                                {
                                    acc.IntersectWith(next.Select(t => t.key));
                                    return acc;
                                });

                        if (sharedKeys.Count > 0)
                        {
                            var sharedKey = sharedKeys.First();
                            // Resolve the display label from the first author's pen name list.
                            var penName = penNamesByAuthor.Values.First()
                                .FirstOrDefault(t => string.Equals(t.key, sharedKey, StringComparison.OrdinalIgnoreCase))
                                .display ?? sharedKey;

                            // Higher confidence than the individual real-name claims (0.90) so the pen name wins.
                            claims.Add(new ProviderClaim("author", penName, 0.95));
                            _logger.LogInformation(
                                "{Provider}: pen name detected for QID {QID} — {AuthorCount} co-authors share pen name \"{PenName}\"",
                                Name, masterWorkQid, authorQids.Count, penName);
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
        if (!string.IsNullOrWhiteSpace(request.Author) && extData is not null
            && extData.Properties.TryGetValue("P50", out var p50AuthorRefs)
            && p50AuthorRefs.Count > 0)
        {
            // Collect the author labels that ExtensionToClaims already emitted.
            var wikiAuthorClaims = claims
                .Where(c => string.Equals(c.Key, "author", StringComparison.OrdinalIgnoreCase))
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
                        // Re-key the existing Wikidata P50 real-name "author" claims so they
                        // don't compete in the canonical author field election.
                        var indicesToRekey = new List<int>();
                        for (int i = 0; i < claims.Count; i++)
                        {
                            if (string.Equals(claims[i].Key, "author", StringComparison.OrdinalIgnoreCase))
                            {
                                indicesToRekey.Add(i);
                            }
                        }

                        foreach (var idx in indicesToRekey)
                        {
                            var original = claims[idx];
                            claims[idx] = new ProviderClaim("author_real_name", original.Value, original.Confidence);
                        }

                        // Emit the embedded (credited) author name at high confidence so it
                        // wins as the canonical author value in the priority cascade.
                        claims.Add(new ProviderClaim("author", embeddedAuthor, 0.95));
                        _logger.LogInformation(
                            "{Provider}: embedded author \"{EmbeddedAuthor}\" does not match Wikidata P50 " +
                            "authors for QID {QID} — treating as pen name. Real author claims re-keyed to " +
                            "\"author_real_name\"; credited pen name emitted as canonical author.",
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
        if (extData is not null)
        {
            var editionBridgeProps = new[] { "P212", "P957", "P5749", "P3861", "P2969", "P648" }
                .Where(p => _config.DataExtension.PropertyLabels.ContainsKey(p))
                .ToList();

            // When we pivoted to an audiobook edition, extData is that edition — it won't
            // have P747 pointing to further sub-editions. For the standard bridge fallback,
            // we need P747 from the master work. Fetch it separately in that case.
            ExtensionResult? editionSource = extData;
            if (audiobookEditionQid is not null)
            {
                try
                {
                    var masterExtensions = await ExtendAsync([masterWorkQid], ["P747"], ct).ConfigureAwait(false);
                    editionSource = masterExtensions.FirstOrDefault(e =>
                        string.Equals(e.QID, masterWorkQid, StringComparison.OrdinalIgnoreCase))
                        ?? extData;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("{Provider}: master work P747 fetch failed for {QID}: {Message}",
                        Name, masterWorkQid, ex.Message);
                }
            }

            if (editionBridgeProps.Count > 0
                && editionSource is not null
                && editionSource.Properties.TryGetValue("P747", out var editionRefs)
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
                        .Where(v => v.Id is not null)
                        .Select(v => v.Id!)
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

                            var editionData = await ExtendAsync(editionQids, propsWithP31, ct)
                                .ConfigureAwait(false);

                            // Filter editions by media type: audiobooks get audiobook-class
                            // editions only, books get non-audiobook editions.
                            var filteredEditions = FilterEditionsByMediaType(editionData, request.MediaType);

                            foreach (var propCode in missingProps)
                            {
                                var claimKey = _config.DataExtension.PropertyLabels[propCode];

                                foreach (var edition in filteredEditions)
                                {
                                    if (!edition.Properties.TryGetValue(propCode, out var vals)
                                        || vals.Count == 0)
                                        continue;

                                    var firstVal = vals.FirstOrDefault();
                                    if (firstVal is null) continue;

                                    var strVal = firstVal.Str ?? firstVal.Id;
                                    if (!string.IsNullOrWhiteSpace(strVal))
                                    {
                                        // Normalize bridge ID values for clean storage.
                                        var normalized = IdentifierNormalizationService.NormalizeRaw(propCode, strVal);
                                        if (!string.IsNullOrWhiteSpace(normalized))
                                        {
                                            claims.Add(new ProviderClaim(claimKey, normalized, 0.90));
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

        return claims;
    }

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

            qid = top.QID;
        }

        // Extend with person properties.
        var personProps = _config.DataExtension.PersonProperties;
        var allProps    = personProps.Core
            .Concat(personProps.Social)
            .Concat(personProps.PenNames)
            .Distinct()
            .ToList();

        // Inject language-specific label (Len) and description (Den) magic suffixes.
        var language = _configLoader?.LoadCore().Language ?? "en";
        allProps.Add($"L{language}");
        allProps.Add($"D{language}");

        if (allProps.Count == 0)
            return [new ProviderClaim("wikidata_qid", qid, 1.0)];

        var extensions = await ExtendAsync([qid], allProps, ct).ConfigureAwait(false);
        var extData    = extensions.FirstOrDefault(e =>
            string.Equals(e.QID, qid, StringComparison.OrdinalIgnoreCase));

        var claims = new List<ProviderClaim>
        {
            new("wikidata_qid", qid, 1.0)
        };

        if (extData is not null)
            claims.AddRange(ExtensionToClaims(extData, _config.DataExtension.PropertyLabels, isWork: false));

        // Fix entity reference labels that may be in wrong language.
        claims = await ResolveEntityLabelsInLanguageAsync(claims, language, ct).ConfigureAwait(false);

        _logger.LogInformation("{Provider}: fetched {Count} person claims for QID {QID}",
            Name, claims.Count, qid);

        return claims;
    }

    /// <summary>
    /// Filters edition data by media type. Audiobooks get only audiobook-class editions;
    /// books get only non-audiobook editions. Other media types get all editions unfiltered.
    /// </summary>
    private IReadOnlyList<ExtensionResult> FilterEditionsByMediaType(
        IReadOnlyList<ExtensionResult> editions,
        MediaType mediaType)
    {
        // Only filter for book-related media types.
        if (mediaType != MediaType.Audiobooks && mediaType != MediaType.Books)
            return editions;

        var audiobookClasses = _config.InstanceOfClasses.TryGetValue("Audiobooks", out var classes)
            ? new HashSet<string>(classes, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(["Q122731938", "Q106833962"], StringComparer.OrdinalIgnoreCase);

        var filtered = new List<ExtensionResult>();
        foreach (var edition in editions)
        {
            var isAudiobook = edition.Properties.TryGetValue("P31", out var p31)
                && p31.Any(v => v.Id is not null && audiobookClasses.Contains(v.Id!));

            if (mediaType == MediaType.Audiobooks && isAudiobook)
                filtered.Add(edition);
            else if (mediaType == MediaType.Books && !isAudiobook)
                filtered.Add(edition);
        }

        // Fall back to unfiltered if no editions match the filter (avoid losing all data).
        return filtered.Count > 0 ? filtered : editions;
    }

    // ── Private: Reconciliation batch HTTP call ───────────────────────────────

    private async Task<Dictionary<string, IReadOnlyList<ReconciliationCandidate>>> ExecuteReconcileBatchAsync(
        IReadOnlyList<ReconcileRequest> batch,
        CancellationToken ct)
    {
        var result  = new Dictionary<string, IReadOnlyList<ReconciliationCandidate>>(StringComparer.Ordinal);
        var payload = BuildQueriesPayload(batch, _config.Reconciliation.MaxCandidates);

        // Language must be included in the cache key: reconci.link embeds the language
        // in the endpoint URL path (e.g. /en/api vs /fr/api), so the same query payload
        // will yield different label/description text for different language settings.
        var language = _configLoader?.LoadCore().Language ?? "en";
        var cacheKey = BuildCacheKey($"reconcile:{language}:{payload}");

        if (_responseCache is not null)
        {
            var cached = await _responseCache.FindAsync(cacheKey, ct).ConfigureAwait(false);
            if (cached is not null)
            {
                _logger.LogDebug("{Provider}: reconcile cache HIT", Name);
                return ParseReconcileResponse(cached.ResponseJson);
            }
        }

        // The Wikidata reconci.link service embeds language in the URL path
        // (https://wikidata.reconci.link/{lang}/api). Substitute the configured
        // language so that changes to CoreConfiguration.Language are respected
        // automatically, without needing to edit the endpoint URL in config.
        var endpoint = SubstituteLanguageInEndpoint(_config.Endpoints.Reconciliation, language);
        var formBody = $"queries={Uri.EscapeDataString(payload)}";
        var responseBody = await PostFormAsync(endpoint, formBody, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(responseBody))
            return result;

        if (_responseCache is not null)
        {
            var queryHash = ComputeSha256(payload);
            await _responseCache.UpsertAsync(
                cacheKey, _providerId.ToString(), queryHash,
                responseBody, null, _config.CacheTtlHours, ct)
                .ConfigureAwait(false);
        }

        return ParseReconcileResponse(responseBody);
    }

    // ── Private: HTTP POST with throttle ─────────────────────────────────────

    private async Task<string?> PostFormAsync(
        string endpoint,
        string formBody,
        CancellationToken ct)
    {
        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_config.ThrottleMs > 0)
            {
                var elapsed = (DateTime.UtcNow - _lastCallUtc).TotalMilliseconds;
                if (elapsed < _config.ThrottleMs)
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(_config.ThrottleMs - elapsed), ct)
                        .ConfigureAwait(false);
            }

            using var client  = _httpFactory.CreateClient("wikidata_reconciliation");
            using var content = new StringContent(formBody, Encoding.UTF8, "application/x-www-form-urlencoded");
            using var response = await client.PostAsync(endpoint, content, ct).ConfigureAwait(false);
            _lastCallUtc = DateTime.UtcNow;

            _logger.LogDebug("{Provider}: POST {Endpoint} → {StatusCode}",
                Name, endpoint, (int)response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("{Provider}: POST {Endpoint} returned {StatusCode}",
                    Name, endpoint, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _throttle.Release();
        }
    }

    // ── Private: Payload builders ─────────────────────────────────────────────

    private static string BuildQueriesPayload(
        IReadOnlyList<ReconcileRequest> requests,
        int limit)
    {
        var obj = new JsonObject();
        foreach (var req in requests)
        {
            var queryObj = new JsonObject
            {
                ["query"] = req.Query,
                ["limit"] = limit,
            };

            if (req.PropertyConstraints is { Count: > 0 })
            {
                var props = new JsonArray();
                foreach (var (pid, val) in req.PropertyConstraints)
                {
                    props.Add(new JsonObject { ["pid"] = pid, ["v"] = val });
                }
                queryObj["properties"] = props;
            }

            obj[req.QueryId] = queryObj;
        }
        return obj.ToJsonString();
    }

    private static string BuildExtendPayload(
        IReadOnlyList<string> qids,
        IReadOnlyList<string> propertyCodes)
    {
        var idsArray  = new JsonArray();
        foreach (var qid in qids)
            idsArray.Add(qid);

        var propsArray = new JsonArray();
        foreach (var p in propertyCodes)
            propsArray.Add(new JsonObject { ["id"] = p });

        var obj = new JsonObject
        {
            ["ids"]        = idsArray,
            ["properties"] = propsArray,
        };
        return obj.ToJsonString();
    }

    private static Dictionary<string, string>? BuildTitleSearchConstraints(ProviderLookupRequest request)
    {
        var c = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(request.Author))
            c["P50"] = request.Author;
        return c.Count > 0 ? c : null;
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

    // ── Private: Response parsers ─────────────────────────────────────────────

    private static Dictionary<string, IReadOnlyList<ReconciliationCandidate>> ParseReconcileResponse(
        string json)
    {
        var result = new Dictionary<string, IReadOnlyList<ReconciliationCandidate>>(StringComparer.Ordinal);

        try
        {
            var root = JsonNode.Parse(json);
            if (root is not JsonObject rootObj)
                return result;

            foreach (var (queryId, queryNode) in rootObj)
            {
                if (queryNode is not JsonObject queryObj)
                    continue;

                var resultArr = queryObj["result"] as JsonArray;
                if (resultArr is null)
                    continue;

                var candidates = new List<ReconciliationCandidate>();
                foreach (var item in resultArr)
                {
                    if (item is not JsonObject itemObj)
                        continue;

                    var id    = itemObj["id"]?.GetValue<string>();
                    var name  = itemObj["name"]?.GetValue<string>();
                    var desc  = itemObj["description"]?.GetValue<string>();
                    var score = itemObj["score"]?.GetValue<double>() ?? 0.0;
                    var match = itemObj["match"]?.GetValue<bool>() ?? false;

                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                        continue;

                    // Strip "http://www.wikidata.org/entity/" prefix if present.
                    var qid = id.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? id.Split('/').Last()
                        : id;

                    candidates.Add(new ReconciliationCandidate(qid, name, desc, score, match));
                }

                result[queryId] = candidates;
            }
        }
        catch (JsonException ex)
        {
            // Return partial results on parse error.
            _ = ex;
        }

        return result;
    }

    private static IReadOnlyList<ExtensionResult> ParseExtensionResponse(
        string json,
        Dictionary<string, string> propertyLabels)
    {
        var results = new List<ExtensionResult>();

        try
        {
            var root = JsonNode.Parse(json);
            if (root is not JsonObject rootObj)
                return results;

            var rowsNode = rootObj["rows"] as JsonObject;
            if (rowsNode is null)
                return results;

            foreach (var (qid, rowNode) in rowsNode)
            {
                if (rowNode is not JsonObject rowObj)
                    continue;

                var properties = new Dictionary<string, List<ExtensionValue>>(StringComparer.OrdinalIgnoreCase);

                foreach (var (pCode, valuesNode) in rowObj)
                {
                    if (valuesNode is not JsonArray valArr)
                        continue;

                    var values = new List<ExtensionValue>();
                    foreach (var valItem in valArr)
                    {
                        if (valItem is not JsonObject valObj)
                            continue;

                        // Use SafeString() instead of GetValue<string>() — the Wikidata
                        // Data Extension API can return numeric JSON values (e.g. for the
                        // "float" field on quantity/duration properties).  GetValue<string>()
                        // throws InvalidOperationException when the node kind is Number, which
                        // propagates past the JsonException catch and crashes the entire pipeline.
                        var str   = SafeNodeString(valObj["str"]);
                        var id    = SafeNodeString(valObj["id"]);
                        var label = SafeNodeString(valObj["text"]) // reconci.link uses "text" for labels
                                 ?? SafeNodeString(valObj["name"]);
                        var date  = SafeNodeString(valObj["date"]);
                        var flt   = SafeNodeString(valObj["float"]);

                        // Extract QID from full URI if present.
                        if (id is not null && id.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            id = id.Split('/').Last();

                        values.Add(new ExtensionValue(str, id, label, date, flt));
                    }

                    if (values.Count > 0)
                        properties[pCode] = values;
                }

                results.Add(new ExtensionResult(qid, properties));
            }
        }
        catch (JsonException)
        {
            // Return partial results.
        }

        return results;
    }

    // ── Private: Extension → ProviderClaim conversion ─────────────────────────

    private static IEnumerable<ProviderClaim> ExtensionToClaims(
        ExtensionResult ext,
        Dictionary<string, string> propertyLabels,
        bool isWork)
    {
        foreach (var (pCode, values) in ext.Properties)
        {
            // ── Magic suffix handling (Len, Den, etc.) ──
            // L{lang} returns the entity label in the user's language.
            // D{lang} returns the entity description in the user's language.
            if (pCode.Length == 3 && pCode[0] == 'L' && char.IsLower(pCode[1]))
            {
                if (isWork && values.Count > 0 && !string.IsNullOrWhiteSpace(values[0].Str))
                    yield return new ProviderClaim("title", values[0].Str!, 0.95);
                continue;
            }

            if (pCode.Length == 3 && pCode[0] == 'D' && char.IsLower(pCode[1]))
            {
                if (values.Count > 0 && !string.IsNullOrWhiteSpace(values[0].Str))
                    yield return new ProviderClaim("description", values[0].Str!, 0.90);
                continue;
            }

            if (!propertyLabels.TryGetValue(pCode, out var claimKey))
                continue;

            // P18 (image) only for Person entities — and needs URL conversion.
            if (string.Equals(pCode, "P18", StringComparison.OrdinalIgnoreCase) && isWork)
                continue;

            // P31 (instance_of) used internally for filtering, never as a claim.
            if (string.Equals(pCode, "P31", StringComparison.OrdinalIgnoreCase))
                continue;

            // P1476 (title) — monolingual text; only take the first value to avoid
            // emitting every language translation as a separate claim.
            bool isMonolingualTitle = string.Equals(pCode, "P1476", StringComparison.OrdinalIgnoreCase);

            foreach (var val in values)
            {
                // Special handling for P18: convert Commons filename to URL.
                if (string.Equals(pCode, "P18", StringComparison.OrdinalIgnoreCase))
                {
                    var filename = val.Str;
                    if (!string.IsNullOrWhiteSpace(filename))
                    {
                        var commonsUrl = $"https://commons.wikimedia.org/wiki/Special:FilePath/{Uri.EscapeDataString(filename)}";
                        yield return new ProviderClaim("headshot_url", commonsUrl, 0.90);
                    }
                    continue;
                }

                // Determine confidence and string value.
                (string? strVal, double confidence) = ExtractValueAndConfidence(val, pCode);

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
                // Use the human-readable label when available; fall back to QID.
                if (!string.IsNullOrWhiteSpace(val.Id))
                {
                    var label = val.Label ?? val.Str ?? val.Id;
                    yield return new ProviderClaim($"{claimKey}_qid", $"{val.Id}::{label}", 0.90);
                }

                // Only emit the first value for monolingual title properties.
                if (isMonolingualTitle) break;
            }
        }
    }

    private static (string? value, double confidence) ExtractValueAndConfidence(
        ExtensionValue val, string pCode)
    {
        // Date values.
        if (val.Date is not null)
        {
            // Reduce ISO date to 4-digit year.
            var year = ExtractYear(val.Date);
            return (year, 0.85);
        }

        // Entity reference (has both Id and Label).
        if (val.Id is not null)
        {
            // For bridge identifier properties, store the QID string.
            var isBridge = IsBridgeProperty(pCode);
            if (isBridge)
                return (val.Id, 0.95);

            // For entity references (author, series, etc.) prefer the label.
            return (val.Label ?? val.Id, 0.90);
        }

        // Float values.
        if (val.Float is not null)
            return (val.Float, 0.85);

        // Plain string.
        if (val.Str is not null)
            return (val.Str, 0.90);

        return (null, 0.0);
    }

    private static bool IsBridgeProperty(string pCode) => pCode switch
    {
        "P212"  => true, // isbn_13
        "P957"  => true, // isbn_10
        "P5749" => true, // asin
        "P4947" => true, // tmdb_movie_id
        "P345"  => true, // imdb_id
        "P3861" => true, // apple_books_id
        "P5905" => true, // comic_vine_id
        "P434"  => true, // musicbrainz_artist_id
        "P436"  => true, // musicbrainz_release_id
        "P5842" => true, // apple_podcasts_id
        _       => false,
    };

    /// <summary>
    /// Strips common audiobook title suffixes that interfere with Wikidata reconciliation.
    /// Examples: "(Unabridged)", ": A Novel", "- A Memoir", "(Audiobook)", etc.
    /// </summary>
    private static string CleanAudiobookTitle(string title)
    {
        // Remove parenthesized/bracketed suffixes: (Unabridged), [Abridged], (Audiobook), etc.
        var cleaned = System.Text.RegularExpressions.Regex.Replace(
            title,
            @"\s*[\(\[]\s*(?:Unabridged|Abridged|Audiobook|Audio\s*Edition|Narrated)\s*[\)\]]",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        // Remove trailing subtitle patterns: ": A Novel", "- A Memoir", ": A Thriller", etc.
        cleaned = System.Text.RegularExpressions.Regex.Replace(
            cleaned,
            @"\s*[:\-–—]\s*A\s+(?:Novel|Memoir|Thriller|Mystery|Romance|Story|Tale|Novella|Biography|History)\s*$",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        // Remove trailing "A Novel" etc. without separator
        cleaned = System.Text.RegularExpressions.Regex.Replace(
            cleaned,
            @"\s+A\s+(?:Novel|Memoir)\s*$",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? title : cleaned;
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

    // ── Private: P279 subclass walk ───────────────────────────────────────────

    private async Task<bool> WalkSubclassAsync(
        string classQid,
        HashSet<string> expectedClasses,
        string mediaTypeKey,
        int maxDepth,
        CancellationToken ct)
    {
        if (maxDepth <= 0)
            return false;

        // Check learned classes first.
        if (_learnedClasses.TryGetValue(classQid, out var learned))
            return string.Equals(learned.MediaType, mediaTypeKey, StringComparison.OrdinalIgnoreCase);

        var extensions = await ExtendAsync([classQid], ["P279"], ct).ConfigureAwait(false);
        var classData  = extensions.FirstOrDefault(e =>
            string.Equals(e.QID, classQid, StringComparison.OrdinalIgnoreCase));

        if (classData is null
            || !classData.Properties.TryGetValue("P279", out var parentValues)
            || parentValues.Count == 0)
            return false;

        foreach (var parentVal in parentValues.Where(v => v.Id is not null))
        {
            var parentQid = parentVal.Id!;

            if (expectedClasses.Contains(parentQid))
            {
                // Learned: classQid maps to mediaTypeKey via parentQid.
                _learnedClasses.TryAdd(classQid,
                    new LearnedClassEntry(classQid, mediaTypeKey, parentQid, DateTime.UtcNow));
                return true;
            }

            if (await WalkSubclassAsync(parentQid, expectedClasses, mediaTypeKey, maxDepth - 1, ct)
                    .ConfigureAwait(false))
            {
                _learnedClasses.TryAdd(classQid,
                    new LearnedClassEntry(classQid, mediaTypeKey, parentQid, DateTime.UtcNow));
                return true;
            }
        }

        return false;
    }

    // ── Private: Extension helpers ────────────────────────────────────────────

    /// <summary>
    /// Safely converts a <see cref="JsonNode"/> to a string regardless of its
    /// underlying JSON kind.  Unlike <c>GetValue&lt;string&gt;()</c>, this does
    /// not throw when the node holds a Number, Boolean, or other non-string value
    /// — it simply calls <c>ToString()</c> so callers always get a usable string
    /// or <c>null</c> when the node is absent.
    /// </summary>
    private static string? SafeNodeString(JsonNode? node)
    {
        if (node is null) return null;

        // JsonValue<string> — fast path; avoids boxing via GetValue<T>.
        if (node is JsonValue jv && jv.TryGetValue<string>(out var s))
            return s;

        // Number, Boolean, or other primitive — convert to string representation.
        // This handles the "float" / "int" fields from the Wikidata Data Extension
        // API when they come back as JSON numbers rather than quoted strings.
        return node.ToString();
    }

    private static string? GetFirstLabel(
        Dictionary<string, List<ExtensionValue>> properties,
        string pCode)
    {
        return properties.TryGetValue(pCode, out var vals) && vals.Count > 0
            ? vals[0].Label ?? vals[0].Str
            : null;
    }

    private static string? GetFirstStr(
        Dictionary<string, List<ExtensionValue>> properties,
        string pCode)
    {
        return properties.TryGetValue(pCode, out var vals) && vals.Count > 0
            ? vals[0].Str ?? vals[0].Date
            : null;
    }

    // ── Private: Cache key + SHA-256 ─────────────────────────────────────────

    private string BuildCacheKey(string input) =>
        $"{_providerId}:{ComputeSha256(input)}";

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ── Private: Entity reference label language correction ──────────────────

    /// <summary>
    /// Re-fetches entity labels for ALL entity-valued claims in the configured language.
    /// The Data Extension API's entity reference "name" field does not respect uselang —
    /// it returns Wikidata's default label, which may be in any language.
    ///
    /// <para>
    /// Once a QID is resolved, Wikidata IS the authority — including for entity reference
    /// labels (author names, series names, genre names). File metadata is only relevant
    /// BEFORE identity is confirmed. After QID resolution, we never fall back to file data.
    /// </para>
    ///
    /// <para>
    /// For entities where a label in the configured language IS found:
    /// - The <c>_qid</c> companion claim is updated to <c>"Q123::NewLabel"</c>.
    /// - The primary label claim is updated to the resolved label.
    /// </para>
    ///
    /// <para>
    /// For entities where NO label exists in the configured language (rare):
    /// - The <c>_qid</c> companion claim retains its ORIGINAL label from the Data Extension
    ///   response (still Wikidata data — just possibly in a different language).
    /// - The primary label claim is kept with its original value. We never drop Wikidata data.
    /// </para>
    /// </summary>
    private async Task<List<ProviderClaim>> ResolveEntityLabelsInLanguageAsync(
        List<ProviderClaim> claims,
        string language,
        CancellationToken ct)
    {
        // Collect ALL entity QIDs from companion _qid claims.
        // Format: "Q123::Label" or "Q123::Q123"
        var qidsToResolve = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claim in claims)
        {
            if (!claim.Key.EndsWith("_qid", StringComparison.OrdinalIgnoreCase))
                continue;

            var colonIdx = claim.Value.IndexOf("::", StringComparison.Ordinal);
            if (colonIdx <= 0) continue;

            var qid = claim.Value[..colonIdx].Trim();
            if (qid.StartsWith('Q') && qid.Length > 1 && char.IsDigit(qid[1]))
                qidsToResolve.Add(qid);
        }

        if (qidsToResolve.Count == 0)
            return claims;

        // PRIMARY: use wbgetentities as the authoritative label source.
        // reconci.link L{lang} pseudo-properties are not reliable — they sometimes return
        // labels in the wrong language or return empty for valid entities (e.g. Q18590295 "Andy Weir").
        // wbgetentities is the canonical Wikidata API and always returns the correct label.
        var resolvedLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (_wikibaseApi is not null)
        {
            // Build a language chain: configured language → "mul" → "en" (last resort).
            var langChain = new List<string> { language };
            if (!string.Equals(language, "mul", StringComparison.OrdinalIgnoreCase))
                langChain.Add("mul");
            if (!string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
                langChain.Add("en");

            var stillUnresolved = qidsToResolve.ToList();
            foreach (var lang in langChain)
            {
                if (stillUnresolved.Count == 0) break;

                try
                {
                    var entities = await _wikibaseApi
                        .GetEntitiesBatchAsync(stillUnresolved, lang, ct)
                        .ConfigureAwait(false);

                    var resolvedThisRound = new List<string>();
                    foreach (var entity in entities)
                    {
                        if (!string.IsNullOrWhiteSpace(entity.Label))
                        {
                            resolvedLabels[entity.Qid] = entity.Label;
                            resolvedThisRound.Add(entity.Qid);
                        }
                    }

                    stillUnresolved = stillUnresolved
                        .Where(q => !resolvedThisRound.Any(r =>
                            string.Equals(r, q, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "{Provider}: wbgetentities label fetch failed for lang='{Lang}'",
                        Name, lang);
                    break;
                }
            }

            _logger.LogDebug(
                "{Provider}: resolved {Count}/{Total} entity labels via wbgetentities " +
                "(primary: {Lang}, fallback: mul, en)",
                Name, resolvedLabels.Count, qidsToResolve.Count, language);
        }
        else
        {
            // No IWikibaseApiService available (test environments) — fall back to the
            // reconci.link L{lang} pseudo-property approach.
            _logger.LogDebug(
                "{Provider}: using reconci.link L{Lang} fallback (no IWikibaseApiService available)",
                Name, language);

            var extensions = await ExtendAsync(
                qidsToResolve.ToList(), [$"L{language}"], ct).ConfigureAwait(false);

            foreach (var ext in extensions)
            {
                if (ext.Properties.TryGetValue($"L{language}", out var values) && values.Count > 0)
                {
                    var label = values[0].Str;
                    if (!string.IsNullOrWhiteSpace(label))
                        resolvedLabels[ext.QID] = label;
                }
            }

            _logger.LogDebug(
                "{Provider}: resolved {Count}/{Total} entity labels in language '{Lang}'",
                Name, resolvedLabels.Count, qidsToResolve.Count, language);
        }

        // Replace labels in ALL claims.
        // - QIDs with a resolved label: update label to the L{lang} value (99% case).
        // - QIDs NOT in resolvedLabels (no label in configured language — rare):
        //     _qid companion → keep original label from Data Extension (still Wikidata data)
        //     primary label claim → keep as-is (still Wikidata data, just possibly different language)
        //   We never drop Wikidata claims. File metadata is irrelevant after QID resolution.
        var result = new List<ProviderClaim>(claims.Count);
        foreach (var claim in claims)
        {
            // Fix companion _qid claims: "Q123::OldLabel" → "Q123::NewLabel"
            // When no L{lang} label exists, keep the original claim unchanged.
            if (claim.Key.EndsWith("_qid", StringComparison.OrdinalIgnoreCase))
            {
                var colonIdx = claim.Value.IndexOf("::", StringComparison.Ordinal);
                if (colonIdx > 0)
                {
                    var qid = claim.Value[..colonIdx].Trim();
                    if (resolvedLabels.TryGetValue(qid, out var newLabel))
                    {
                        // Language-correct label found — update to resolved label.
                        result.Add(new ProviderClaim(claim.Key, $"{qid}::{newLabel}", claim.Confidence));
                        continue;
                    }

                    // No L{lang} label for this QID. Keep the original claim as-is.
                    // The original label is still from Wikidata — it's just possibly in a
                    // different language. Keeping it is better than losing the data.
                }
                result.Add(claim);
                continue;
            }

            // Fix primary label claims for entity-valued properties.
            // Match by index: the Nth label claim for a key corresponds to the Nth _qid companion claim.
            var qidKey = $"{claim.Key}_qid";
            var hasCompanion = claims.Any(c =>
                string.Equals(c.Key, qidKey, StringComparison.OrdinalIgnoreCase));

            if (hasCompanion)
            {
                var companions = claims
                    .Where(c => string.Equals(c.Key, qidKey, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var labelClaims = claims
                    .Where(c => string.Equals(c.Key, claim.Key, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var idx = labelClaims.IndexOf(claim);
                if (idx >= 0 && idx < companions.Count)
                {
                    var companionValue = companions[idx].Value;
                    var cIdx = companionValue.IndexOf("::", StringComparison.Ordinal);
                    if (cIdx > 0)
                    {
                        var qid = companionValue[..cIdx].Trim();
                        if (resolvedLabels.TryGetValue(qid, out var newLabel))
                        {
                            // Language-correct label found — update primary claim to resolved label.
                            result.Add(new ProviderClaim(claim.Key, newLabel, claim.Confidence));
                            continue;
                        }

                        // No L{lang} label for this QID. Keep the primary claim unchanged.
                        // The original value is still from Wikidata and should not be discarded.
                        _logger.LogDebug(
                            "{Provider}: no '{Lang}' label for QID {QID} on claim '{Key}' — keeping original Wikidata value",
                            Name, language, qid, claim.Key);
                    }
                }
            }

            result.Add(claim);
        }

        return result;
    }

    // ── Private: Language substitution ───────────────────────────────────────

    /// <summary>
    /// Substitutes the language segment in a reconci.link reconciliation endpoint URL.
    ///
    /// The Wikidata reconciliation service at wikidata.reconci.link uses a language
    /// code in the URL path: <c>https://wikidata.reconci.link/{lang}/api</c>.
    /// This method replaces the language segment so that the configured
    /// <see cref="CoreConfiguration.Language"/> is always used, regardless of
    /// what language code is hardcoded in the config file's endpoint URL.
    ///
    /// If the URL does not match the expected reconci.link pattern, it is returned
    /// unchanged (safe fallback — no accidental URL corruption for custom endpoints).
    /// </summary>
    /// <param name="endpointUrl">The endpoint URL from configuration (e.g. "https://wikidata.reconci.link/en/api").</param>
    /// <param name="language">The two-letter BCP-47 language code to substitute (e.g. "en", "fr").</param>
    /// <returns>The endpoint URL with the language segment replaced, or the original URL if not matched.</returns>
    private static string SubstituteLanguageInEndpoint(string endpointUrl, string language)
    {
        // Pattern: https://wikidata.reconci.link/{lang}/api
        // Replace the lang segment between the host and /api.
        const string host   = "wikidata.reconci.link/";
        const string suffix = "/api";

        var hostIdx = endpointUrl.IndexOf(host, StringComparison.OrdinalIgnoreCase);
        if (hostIdx < 0)
            return endpointUrl; // Not a reconci.link URL — leave unchanged.

        var afterHost = hostIdx + host.Length;
        var apiIdx    = endpointUrl.IndexOf(suffix, afterHost, StringComparison.OrdinalIgnoreCase);
        if (apiIdx < 0)
            return endpointUrl; // Unexpected format — leave unchanged.

        // Rebuild: everything before the lang segment + new lang + /api + anything after.
        return endpointUrl[..afterHost] + language + endpointUrl[apiIdx..];
    }

}
