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
        IProviderResponseCacheRepository? responseCache = null,
        IConfigurationLoader? configLoader = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _config        = config;
        _httpFactory   = httpFactory;
        _logger        = logger;
        _responseCache = responseCache;
        _configLoader  = configLoader;

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
                candidates = await FilterByMediaTypeAsync(
                    candidates, request.MediaType, ct).ConfigureAwait(false);
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
        var cacheKey = BuildCacheKey($"extend:{extendPayload}");

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

        var language = _configLoader?.LoadCore().Language ?? "en";
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
        var qids        = candidates.Select(c => c.QID).ToList();

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
    /// Discovers audiobook editions of a work via P747 (has_edition_or_translation)
    /// followed by P31 filtering to retain only audiobook-class items.
    /// </summary>
    /// <param name="workQid">The Wikidata Q-identifier of the work.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<AudiobookEditionData>> DiscoverAudiobookEditionsAsync(
        string workQid,
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

                results.Add(new AudiobookEditionData(narrator, duration, asin, publisher));
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

            var constraints = BuildTitleSearchConstraints(request);
            var candidates  = await ReconcileAsync(request.Title, constraints, ct).ConfigureAwait(false);

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

            qid = top.QID;
            reconciliationLabel = top.Label;
        }

        // Extend the resolved QID with work properties.
        var workProps = _config.DataExtension.WorkProperties;
        var allProps  = workProps.Core
            .Concat(workProps.Bridges)
            .Concat(workProps.Editions)
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

        // Emit the reconciliation match label as a title claim (lower confidence than L{lang}).
        if (!string.IsNullOrWhiteSpace(reconciliationLabel))
            claims.Add(new ProviderClaim("title", reconciliationLabel, 0.93));

        if (extData is not null)
            claims.AddRange(ExtensionToClaims(extData, _config.DataExtension.PropertyLabels, isWork: true));

        // ── Edition bridge ID resolution ─────────────────────────────────────
        // Wikidata stores ISBNs and other bridge IDs on edition items (P747),
        // not on the work itself. If key bridge IDs are still missing after
        // the work fetch, look them up from edition items.
        if (extData is not null)
        {
            var editionBridgeProps = new[] { "P212", "P957", "P5749", "P3861", "P2969", "P648" }
                .Where(p => _config.DataExtension.PropertyLabels.ContainsKey(p))
                .ToList();

            if (editionBridgeProps.Count > 0
                && extData.Properties.TryGetValue("P747", out var editionRefs)
                && editionRefs.Count > 0)
            {
                // Determine which bridge IDs are still missing from the work-level fetch.
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

                            _logger.LogDebug("{Provider}: edition bridge resolution added {Count} bridge IDs for {QID}",
                                Name, claims.Count(c => missingProps.Any(p =>
                                    _config.DataExtension.PropertyLabels.TryGetValue(p, out var k) &&
                                    string.Equals(c.Key, k, StringComparison.OrdinalIgnoreCase))),
                                qid);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug("{Provider}: edition bridge ID resolution failed for {QID}: {Message}",
                                Name, qid, ex.Message);
                        }
                    }
                }
            }
        }

        _logger.LogInformation("{Provider}: fetched {Count} work claims for QID {QID}",
            Name, claims.Count, qid);

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

        var cacheKey = BuildCacheKey($"reconcile:{payload}");

        if (_responseCache is not null)
        {
            var cached = await _responseCache.FindAsync(cacheKey, ct).ConfigureAwait(false);
            if (cached is not null)
            {
                _logger.LogDebug("{Provider}: reconcile cache HIT", Name);
                return ParseReconcileResponse(cached.ResponseJson);
            }
        }

        var endpoint     = _config.Endpoints.Reconciliation;
        var formBody     = $"queries={Uri.EscapeDataString(payload)}";
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

                        var str   = valObj["str"]?.GetValue<string>();
                        var id    = valObj["id"]?.GetValue<string>();
                        var label = valObj["text"]?.GetValue<string>() // reconci.link uses "text" for labels
                                 ?? valObj["name"]?.GetValue<string>();
                        var date  = valObj["date"]?.GetValue<string>();
                        var flt   = valObj["float"]?.GetValue<string>();

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
                // Always use the QID as the label — more reliable across languages.
                if (!string.IsNullOrWhiteSpace(val.Id))
                {
                    var label = val.Id;
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

}
