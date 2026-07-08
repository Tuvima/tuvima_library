using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;
using MediaEngine.Storage.Models;

namespace MediaEngine.Providers.Adapters;

/// <summary>
/// Universal config-driven adapter that reads its behaviour entirely from a
/// <see cref="ProviderConfiguration"/> loaded from <c>config/providers/{name}.json</c>.
///
/// <para>
/// One instance is created per config file with <c>adapter_type: "config_driven"</c>.
/// The adapter evaluates search strategies in priority order, extracts fields via
/// JSON path expressions, and applies named transforms — all driven by data in the
/// config file. No subclass required.
/// </para>
///
/// <para>
/// Adding a new REST+JSON provider is a zero-code operation: drop a config file
/// in <c>config/providers/</c>, restart, done.
/// </para>
/// </summary>
public sealed class ConfigDrivenAdapter : IExternalMetadataProvider
{
    private readonly ProviderConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ConfigDrivenAdapter> _logger;
    private readonly IProviderResponseCacheRepository? _responseCache;
    private readonly IProviderHealthMonitor _healthMonitor;
    private readonly IProviderRateLimiterCoordinator _rateLimiter;

    // Parsed once at construction.
    private readonly Guid _providerId;
    private readonly HashSet<MediaType> _mediaTypes;
    private readonly HashSet<EntityType> _entityTypes;

    public ConfigDrivenAdapter(
        ProviderConfiguration config,
        IHttpClientFactory httpFactory,
        ILogger<ConfigDrivenAdapter> logger,
        IProviderHealthMonitor healthMonitor,
        IProviderResponseCacheRepository? responseCache = null,
        IProviderRateLimiterCoordinator? rateLimiter = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(healthMonitor);

        _config = config;
        _httpFactory = httpFactory;
        _responseCache = responseCache;
        _logger = logger;
        _healthMonitor = healthMonitor;
        _rateLimiter = rateLimiter ?? new ProviderRateLimiterCoordinator();

        _providerId = !string.IsNullOrEmpty(config.ProviderId)
            ? Guid.Parse(config.ProviderId)
            : Guid.NewGuid();

        // Parse can_handle filters into enum sets for fast lookup.
        _mediaTypes = ParseEnumSet<MediaType>(config.CanHandle?.MediaTypes);
        _entityTypes = ParseEnumSet<EntityType>(config.CanHandle?.EntityTypes);
    }

    // -- IExternalMetadataProvider ---------------------------------------------

    public string Name => _config.Name;

    public ProviderDomain Domain => _config.Domain;

    public IReadOnlyList<string> CapabilityTags => _config.CapabilityTags;

    public Guid ProviderId => _providerId;

    public bool CanHandle(MediaType mediaType) =>
        _mediaTypes.Count == 0 || mediaType == MediaType.Unknown || _mediaTypes.Contains(mediaType);

    public bool CanHandle(EntityType entityType) =>
        _entityTypes.Count == 0 || _entityTypes.Contains(entityType);

    public async Task<IReadOnlyList<ProviderClaim>> FetchAsync(
        ProviderLookupRequest request,
        CancellationToken ct = default)
    {
        if (!CanHandle(request.MediaType) || !CanHandle(request.EntityType))
            return [];

        // Skip providers known to be down — items will be queued as "Waiting for Provider".
        if (_healthMonitor.IsDown(Name))
        {
            _logger.LogDebug("{Provider} is known to be down — skipping", Name);
            return [];
        }

        // Short-circuit when an API key is required but not configured.
        if (_config.RequiresApiKey
            && string.IsNullOrWhiteSpace(_config.HttpClient?.ApiKey)
            && (string.IsNullOrWhiteSpace(_config.HttpClient?.Username)
                || string.IsNullOrWhiteSpace(_config.HttpClient?.Password)))
        {
            _logger.LogWarning(
                "{Provider}: requires an API key but none is configured — skipping. "
                + "Set 'api_key' in the provider's http_client config.",
                Name);
            return [];
        }

        var strategies = FilterStrategiesByMediaType(
            _config.SearchStrategies, request.MediaType)
            ?.OrderBy(s => s.Priority)
            .ToList();

        if (strategies is null or { Count: 0 })
        {
            _logger.LogDebug("{Provider} has no search strategies configured", Name);
            return [];
        }

        // Resolve the effective language based on the provider's language strategy.
        var effectiveLang = ResolveEffectiveLanguage(request);
        var effectiveRequest = string.Equals(effectiveLang, request.Language, StringComparison.OrdinalIgnoreCase)
            ? request
            : CloneRequestWithLanguage(request, effectiveLang);

        foreach (var strategy in strategies)
        {
            // Check required fields are present.
            if (!AllRequiredFieldsPresent(strategy, effectiveRequest))
            {
                _logger.LogDebug(
                    "{Provider}/{Strategy}: skipped — missing required fields",
                    Name, strategy.Name);
                continue;
            }

            try
            {
                var claims = await ExecuteStrategyAsync(strategy, effectiveRequest, ct)
                    .ConfigureAwait(false);

                if (claims.Count > 0)
                {
                    _logger.LogDebug(
                        "{Provider}/{Strategy}: returned {Count} claims",
                        Name, strategy.Name, claims.Count);
                    await _healthMonitor.ReportSuccessAsync(Name, ct);
                    return claims;
                }

                _logger.LogInformation(
                    "{Provider}/{Strategy}: zero results from API, trying next strategy",
                    Name, strategy.Name);
                // Provider responded but had no match — still healthy.
                await _healthMonitor.ReportSuccessAsync(Name, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex,
                    "{Provider}/{Strategy}: failed, trying next strategy",
                    Name, strategy.Name);
                await _healthMonitor.ReportFailureAsync(Name, ex.Message, ct);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
            {
                _logger.LogWarning(ex,
                    "{Provider}/{Strategy}: parse error, trying next strategy",
                    Name, strategy.Name);
            }
        }

        // "Both" strategy: if the metadata-language pass found nothing, retry in English.
        if (_config.LanguageStrategy == LanguageStrategy.Both
            && !string.Equals(effectiveLang, "en", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("{Provider}: 'both' strategy — retrying in English", Name);
            var englishRequest = CloneRequestWithLanguage(request, "en");

            foreach (var strategy in strategies)
            {
                if (!AllRequiredFieldsPresent(strategy, englishRequest))
                    continue;

                try
                {
                    var claims = await ExecuteStrategyAsync(strategy, englishRequest, ct)
                        .ConfigureAwait(false);

                    if (claims.Count > 0)
                    {
                        // Tag claims with source language since they came from English fallback.
                        await _healthMonitor.ReportSuccessAsync(Name, ct);
                        return claims.Select(c => c with { SourceLanguage = "en" }).ToList();
                    }

                    // Provider responded — still healthy even with no match.
                    await _healthMonitor.ReportSuccessAsync(Name, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    _logger.LogWarning(ex,
                        "{Provider}/{Strategy}: English fallback failed",
                        Name, strategy.Name);
                    await _healthMonitor.ReportFailureAsync(Name, ex.Message, ct);
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
                {
                    _logger.LogWarning(ex,
                        "{Provider}/{Strategy}: English fallback parse error",
                        Name, strategy.Name);
                }
            }
        }

        return [];
    }

    /// <summary>
    /// Searches the provider and returns up to <paramref name="limit"/> result candidates,
    /// each with enough context for the user to visually identify a match (title, description,
    /// year, thumbnail, provider item ID).
    ///
    /// Reuses the same URL building and HTTP infrastructure as <see cref="FetchAsync"/>,
    /// but iterates the results array instead of picking a single result.
    /// </summary>
    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        ProviderLookupRequest request,
        int limit = 25,
        CancellationToken ct = default)
    {
        if (!CanHandle(request.MediaType) || !CanHandle(request.EntityType))
            return [];

        // Short-circuit when an API key is required but not configured.
        if (_config.RequiresApiKey
            && string.IsNullOrWhiteSpace(_config.HttpClient?.ApiKey)
            && (string.IsNullOrWhiteSpace(_config.HttpClient?.Username)
                || string.IsNullOrWhiteSpace(_config.HttpClient?.Password)))
        {
            _logger.LogWarning(
                "{Provider}: requires an API key but none is configured — skipping search.",
                Name);
            return [];
        }

        var strategies = FilterStrategiesByMediaType(
            _config.SearchStrategies, request.MediaType)
            ?.OrderBy(s => s.Priority)
            .ToList();

        if (strategies is null or { Count: 0 })
            return [];

        // Resolve the effective language based on the provider's language strategy.
        var effectiveLang = ResolveEffectiveLanguage(request);
        var effectiveRequest = string.Equals(effectiveLang, request.Language, StringComparison.OrdinalIgnoreCase)
            ? request
            : CloneRequestWithLanguage(request, effectiveLang);

        // Use the lesser of caller limit, strategy max_results, and a hard cap of 50.
        var effectiveLimit = limit;

        foreach (var strategy in strategies)
        {
            if (!AllRequiredFieldsPresent(strategy, effectiveRequest))
                continue;

            // Strategies without a results_path return a single object — not useful
            // for multi-result search. Skip to the next strategy.
            if (string.IsNullOrEmpty(strategy.ResultsPath))
                continue;

            // Per-strategy cap.
            if (strategy.MaxResults > 0)
                effectiveLimit = Math.Min(effectiveLimit, strategy.MaxResults);

            try
            {
                var results = await ExecuteSearchStrategyAsync(strategy, effectiveRequest, effectiveLimit, ct)
                    .ConfigureAwait(false);

                if (results.Count > 0)
                    return results;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException)
            {
                _logger.LogWarning(ex,
                    "{Provider}/{Strategy}: search failed, trying next strategy",
                    Name, strategy.Name);
            }
        }

        // "Both" strategy: if the metadata-language pass found nothing, retry in English.
        if (_config.LanguageStrategy == LanguageStrategy.Both
            && !string.Equals(effectiveLang, "en", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("{Provider}: 'both' strategy — retrying search in English", Name);
            var englishRequest = CloneRequestWithLanguage(request, "en");

            foreach (var strategy in strategies)
            {
                if (!AllRequiredFieldsPresent(strategy, englishRequest))
                    continue;

                if (string.IsNullOrEmpty(strategy.ResultsPath))
                    continue;

                var strategyLimit = limit;
                if (strategy.MaxResults > 0)
                    strategyLimit = Math.Min(strategyLimit, strategy.MaxResults);

                try
                {
                    var results = await ExecuteSearchStrategyAsync(strategy, englishRequest, strategyLimit, ct)
                        .ConfigureAwait(false);

                    if (results.Count > 0)
                        return results;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException)
                {
                    _logger.LogWarning(ex,
                        "{Provider}/{Strategy}: English fallback search failed",
                        Name, strategy.Name);
                }
            }
        }

        return [];
    }

    /// <summary>
    /// Executes a search strategy and extracts multiple result items from the response array.
    /// </summary>
    private async Task<IReadOnlyList<SearchResultItem>> ExecuteSearchStrategyAsync(
        SearchStrategyConfig strategy,
        ProviderLookupRequest request,
        int limit,
        CancellationToken ct)
    {
        // Manual multi-result search — use the full requested limit.
        var url = BuildUrl(strategy, request, limit);
        _logger.LogInformation("{Provider}/{Strategy}: SEARCH {Url}", Name, strategy.Name, url);

            using var client = _httpFactory.CreateClient(_config.Name);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);

            // Apply bearer API key header if configured.
            if (_config.HttpClient is { ApiKeyDelivery: "bearer" }
                && !string.IsNullOrWhiteSpace(_config.HttpClient.ApiKey))
            {
                httpRequest.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", _config.HttpClient.ApiKey);
            }
            else if (_config.HttpClient is { ApiKeyDelivery: "basic" }
                && !string.IsNullOrWhiteSpace(_config.HttpClient.Username)
                && !string.IsNullOrWhiteSpace(_config.HttpClient.Password))
            {
                var credentials = Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(
                        $"{_config.HttpClient.Username}:{_config.HttpClient.Password}"));
                httpRequest.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Basic", credentials);
            }

            using var response = await _rateLimiter.ExecuteAsync(
                Name,
                _config.RateLimit,
                token => client.SendAsync(httpRequest, token),
                ct).ConfigureAwait(false);

            _logger.LogInformation(
                "{Provider}/{Strategy}: HTTP {StatusCode}",
                Name, strategy.Name, (int)response.StatusCode);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound
                && strategy.Tolerate404)
                return [];

            response.EnsureSuccessStatusCode();

            var json = await response.Content
                .ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>(cancellationToken: ct)
                .ConfigureAwait(false);

            if (json is null)
                return [];

            // Navigate to results array.
            var resultsNode = JsonPathEvaluator.Evaluate(json, strategy.ResultsPath!);
            if (resultsNode is not System.Text.Json.Nodes.JsonArray arr || arr.Count == 0)
                return [];

            var items = new List<SearchResultItem>();
            var count = Math.Min(arr.Count, limit);

            for (int i = 0; i < count; i++)
            {
                var resultNode = arr[i];
                if (resultNode is null)
                    continue;

                var item = ExtractSearchResultItem(resultNode, request, strategy);
                if (item is not null)
                    items.Add(item);
            }

            _logger.LogDebug(
                "{Provider}/{Strategy}: search returned {Count} items",
                Name, strategy.Name, items.Count);

            // Return all items — the caller (SearchService or HydrationPipeline)
            // handles ranking and selection. For the resolve tab, users need to
            // see multiple editions with different covers, narrators, and years.
            // For automated pipelines, the scoring service picks the best match.
            return items;
    }

    /// <summary>
    /// Extracts a <see cref="SearchResultItem"/> from a single JSON result object
    /// using the configured field mappings. Looks for title, description, year,
    /// cover/thumbnail, and a provider item ID.
    /// <para>
    /// The lookup request is used to compute a per-result match score so that the first
    /// result is not always scored identically to the tenth. For comics, series and issue
    /// number are stronger identity signals than issue-title text.
    /// </para>
    /// </summary>
    private SearchResultItem? ExtractSearchResultItem(
        System.Text.Json.Nodes.JsonNode resultNode,
        ProviderLookupRequest request,
        SearchStrategyConfig? strategy = null)
    {
        var filteredMappings = FilterMappingsByMediaType(_config.FieldMappings, request.MediaType);
        if (filteredMappings.Count == 0)
            return null;

        // When the strategy has release selection (e.g. MusicBrainz recordings with nested
        // releases), pick the best release so source-routed mappings resolve correctly.
        JsonNode? releaseNode = strategy?.ReleaseSelection is not null
            ? ApplyReleaseSelection(resultNode, strategy.ReleaseSelection)
            : null;

        string? title = null;
        string? author = null;
        string? description = null;
        string? year = null;
        string? thumbnailUrl = null;
        string? providerItemId = null;
        var extraFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in filteredMappings)
        {
            // Route to the correct source node (recording vs release) when configured.
            var sourceNode = mapping.Source?.ToLowerInvariant() switch
            {
                "release" => releaseNode ?? resultNode,
                _ => resultNode,
            };

            var node = JsonPathEvaluator.Evaluate(sourceNode, mapping.JsonPath);
            if (node is null)
            {
                _logger.LogDebug("{Provider}: mapping '{Key}' (path '{Path}') — node not found",
                    Name, mapping.ClaimKey, mapping.JsonPath);
                continue;
            }

            // Check condition if configured.
            if (mapping.Condition is not null && !PassesFilters(sourceNode, [mapping.Condition]))
            {
                _logger.LogDebug("{Provider}: mapping '{Key}' — condition not met", Name, mapping.ClaimKey);
                continue;
            }

            // Use ApplyTransform for array-aware extraction (array_join, etc.),
            // then fall back to GetStringValue for simple scalars.
            string? raw;
            if (!string.IsNullOrEmpty(mapping.Transform))
            {
                var values = ApplyTransform(node, mapping);
                raw = values.Count > 0 ? values[0] : null;
            }
            else
            {
                raw = JsonPathEvaluator.GetStringValue(node);
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                _logger.LogDebug("{Provider}: mapping '{Key}' (path '{Path}') — value is null or empty",
                    Name, mapping.ClaimKey, mapping.JsonPath);
                continue;
            }

            _logger.LogDebug("{Provider}: mapping '{Key}' ? '{Value}'",
                Name, mapping.ClaimKey, raw.Length > 80 ? raw[..80] + "…" : raw);

            switch (mapping.ClaimKey.ToLowerInvariant())
            {
                case "title":
                    title ??= raw;
                    break;
                case "author":
                    author ??= raw;
                    break;
                case "description":
                    description ??= raw;
                    break;
                case "year":
                    year ??= raw;
                    break;
                case "cover":
                    thumbnailUrl ??= raw;
                    break;
                // Provider-specific IDs for direct follow-up lookup.
                case BridgeIdKeys.Isbn:
                case BridgeIdKeys.Asin:
                case BridgeIdKeys.AppleBooksId:
                case BridgeIdKeys.GoodreadsId:
                case BridgeIdKeys.AudibleId:
                case BridgeIdKeys.TmdbId:
                case BridgeIdKeys.ImdbId:
                case BridgeIdKeys.ComicVineId:
                case BridgeIdKeys.MusicBrainzId:
                case BridgeIdKeys.SpotifyId:
                    providerItemId ??= raw;
                    break;
                default:
                    // Collect any other mapped fields (album, track_number, duration, etc.)
                    extraFields.TryAdd(mapping.ClaimKey, raw);
                    break;
            }
        }

        // Must have at least a title to be a valid result.
        if (string.IsNullOrWhiteSpace(title))
            return null;

        // Compute a per-result match score based on how closely the result's
        // title (and author) match the original search query.
        // This ensures result 1 scores higher than result 8 when the provider
        // returns them in relevance order but with identical field weights.
        var confidence = ComputeSearchResultConfidence(request, title, author, extraFields);

        _logger.LogInformation(
            "{Provider}: extracted result Title='{Title}' Author='{Author}' Year='{Year}' " +
            "HasDesc={HasDesc} HasCover={HasCover} ExtraFields={ExtraCount} Score={Score:P0}",
            Name, title, author ?? "—", year ?? "—",
            description is not null, thumbnailUrl is not null, extraFields.Count, confidence);

        return new SearchResultItem(
            Title:          title,
            Author:         author,
            Description:    description,
            Year:           year,
            ThumbnailUrl:   thumbnailUrl,
            ProviderItemId: providerItemId,
            Confidence:     confidence,
            ProviderName:   Name,
            ExtraFields:    extraFields.Count > 0 ? extraFields : null);
    }

    /// <summary>
    /// Computes a per-result match score (0.0–1.0) by comparing the search
    /// <paramref name="query"/> against the result's <paramref name="title"/>
    /// using word-level overlap similarity.
    ///
    /// <para>
    /// Algorithm:
    /// <list type="bullet">
    ///   <item>Tokenise both query and title into lowercase words (=2 chars).</item>
    ///   <item>Coverage = query words found in title / total query words.</item>
    ///   <item>Precision = title words found in query / total title words.</item>
    ///   <item>Score = harmonic mean (F1) of coverage and precision × 0.85.</item>
    ///   <item>+0.12 bonus for exact (normalised) title match.</item>
    ///   <item>+0.05 bonus if any author token appears in the query.</item>
    ///   <item>Minimum 0.05 when the result has a title but no query is given.</item>
    /// </list>
    /// </para>
    /// </summary>
    private static double ComputeSearchResultConfidence(
        ProviderLookupRequest request,
        string title,
        string? author,
        IReadOnlyDictionary<string, string> extraFields)
    {
        if (request.MediaType == MediaType.Comics)
        {
            var fileSeries = request.Series ?? request.Hints?.GetValueOrDefault(MetadataFieldConstants.Series);
            var fileIssue = GetComicIssueHint(request);
            var candidateSeries = extraFields.GetValueOrDefault(MetadataFieldConstants.Series);
            var candidateIssue = extraFields.GetValueOrDefault("issue_number")
                ?? extraFields.GetValueOrDefault(MetadataFieldConstants.SeriesPosition)
                ?? extraFields.GetValueOrDefault("issue");

            if (!string.IsNullOrWhiteSpace(fileSeries)
                && !string.IsNullOrWhiteSpace(fileIssue)
                && !string.IsNullOrWhiteSpace(candidateSeries)
                && !string.IsNullOrWhiteSpace(candidateIssue)
                && AreEquivalentComicText(fileSeries, candidateSeries)
                && AreEquivalentComicOrdinals(fileIssue, candidateIssue))
            {
                return 1.0;
            }
        }

        return ComputeQueryMatchScore(request.Title, title, author);
    }

    private static double ComputeQueryMatchScore(string? query, string? title, string? author)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(title))
            return 0.50; // No query context — neutral score.

        var queryTokens = TokenizeText(query);
        var titleTokens = TokenizeText(title);

        if (queryTokens.Count == 0 || titleTokens.Count == 0)
            return 0.50;

        // Exact normalised match ? perfect score.
        if (string.Equals(
                string.Join(' ', queryTokens.Order()),
                string.Join(' ', titleTokens.Order()),
                StringComparison.OrdinalIgnoreCase))
            return 1.0;

        // Coverage: fraction of query words that appear in the title.
        int coverageHits = queryTokens.Count(q => titleTokens.Contains(q));
        double coverage  = (double)coverageHits / queryTokens.Count;

        // Precision: fraction of title words that appear in the query.
        int precisionHits = titleTokens.Count(t => queryTokens.Contains(t));
        double precision  = (double)precisionHits / titleTokens.Count;

        // F1 (harmonic mean) scaled to 0.85 ceiling.
        double f1 = (coverage + precision) > 0
            ? 2.0 * coverage * precision / (coverage + precision)
            : 0.0;
        double score = f1 * 0.85;

        // Author tokens in query ? small bonus.
        if (!string.IsNullOrWhiteSpace(author))
        {
            var authorTokens = TokenizeText(author);
            bool authorInQuery = authorTokens.Any(a => queryTokens.Contains(a));
            if (authorInQuery)
                score += 0.05;
        }

        return Math.Clamp(score, 0.05, 1.0);
    }

    /// <summary>
    /// Tokenises text into a lowercase word set suitable for similarity comparison.
    /// Strips punctuation and filters out single-character tokens.
    /// </summary>
    private static HashSet<string> TokenizeText(string text)
        => [.. text.ToLowerInvariant()
            .Split([' ', ',', '.', '-', ':', ';', '\'', '"', '(', ')', '[', ']', '!', '?'],
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)];

    // -- Strategy execution --------------------------------------------------

    private async Task<IReadOnlyList<ProviderClaim>> ExecuteStrategyAsync(
        SearchStrategyConfig strategy,
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        // Automatic single-result match — request only as many results as we need.
        var fetchLimit = strategy.FetchLimit > 0 ? strategy.FetchLimit : 5;
        var url = BuildUrl(strategy, request, fetchLimit);
        _logger.LogDebug("{Provider}/{Strategy}: FETCH {Url}", Name, strategy.Name, url);

        // -- Response cache check ---------------------------------------------
        var cacheKey = BuildCacheKey(url);
        var cacheTtlHours = _config.CacheTtlHours ?? 168; // Default: 7 days

        if (_responseCache is not null)
        {
            var cached = await _responseCache.FindAsync(cacheKey, ct).ConfigureAwait(false);
            if (cached is not null)
            {
                _logger.LogDebug(
                    "{Provider}/{Strategy}: cache HIT for {Url}", Name, strategy.Name, url);

                var cachedJson = JsonNode.Parse(cached.ResponseJson);
                if (cachedJson is not null)
                {
                    var resultNode = await NavigateToResultAsync(cachedJson, strategy, request, ct)
                        .ConfigureAwait(false);
                    if (resultNode is not null)
                        return await ExtractAndValidateClaimsAsync(strategy, request, resultNode, ct)
                            .ConfigureAwait(false);
                }
            }
        }

        // -- HTTP call with provider-level rate limiting ----------------------
            using var client = _httpFactory.CreateClient(_config.Name);

            // ETag conditional revalidation for expired entries.
            string? existingEtag = null;
            if (_responseCache is not null)
            {
                existingEtag = await _responseCache.FindExpiredEtagAsync(cacheKey, ct)
                    .ConfigureAwait(false);
            }

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(existingEtag))
                httpRequest.Headers.IfNoneMatch.Add(
                    new System.Net.Http.Headers.EntityTagHeaderValue($"\"{existingEtag}\""));

            // Apply bearer API key header if configured.
            if (_config.HttpClient is { ApiKeyDelivery: "bearer" }
                && !string.IsNullOrWhiteSpace(_config.HttpClient.ApiKey))
            {
                httpRequest.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", _config.HttpClient.ApiKey);
            }
            // Apply HTTP Basic Authentication if configured.
            else if (_config.HttpClient is { ApiKeyDelivery: "basic" }
                && !string.IsNullOrWhiteSpace(_config.HttpClient.Username)
                && !string.IsNullOrWhiteSpace(_config.HttpClient.Password))
            {
                var credentials = Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(
                        $"{_config.HttpClient.Username}:{_config.HttpClient.Password}"));
                httpRequest.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Basic", credentials);
            }

            using var response = await _rateLimiter.ExecuteAsync(
                Name,
                _config.RateLimit,
                token => client.SendAsync(httpRequest, token),
                ct).ConfigureAwait(false);

            // ETag 304: cache is still valid — refresh expiry and use it.
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified
                && _responseCache is not null)
            {
                _logger.LogDebug(
                    "{Provider}/{Strategy}: 304 Not Modified — refreshing cache",
                    Name, strategy.Name);
                await _responseCache.RefreshExpiryAsync(cacheKey, cacheTtlHours, ct)
                    .ConfigureAwait(false);

                // Re-read the now-refreshed cached response.
                var refreshed = await _responseCache.FindAsync(cacheKey, ct)
                    .ConfigureAwait(false);
                if (refreshed is not null)
                {
                    var cachedJson = JsonNode.Parse(refreshed.ResponseJson);
                    if (cachedJson is not null)
                    {
                        var resultNode = await NavigateToResultAsync(cachedJson, strategy, request, ct)
                            .ConfigureAwait(false);
                        if (resultNode is not null)
                            return await ExtractAndValidateClaimsAsync(strategy, request, resultNode, ct)
                                .ConfigureAwait(false);
                    }
                }
                return [];
            }

            // Tolerate 404 for direct-lookup APIs (e.g. Audnexus).
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound
                && strategy.Tolerate404)
            {
                _logger.LogDebug(
                    "{Provider}/{Strategy}: 404 tolerated", Name, strategy.Name);
                return [];
            }

            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync(ct)
                .ConfigureAwait(false);

            // Cache the response.
            if (_responseCache is not null && !string.IsNullOrEmpty(responseBody))
            {
                var etag = response.Headers.ETag?.Tag?.Trim('"');
                var queryHash = ComputeSha256(url);
                await _responseCache.UpsertAsync(
                    cacheKey, _providerId.ToString(), queryHash,
                    responseBody, etag, cacheTtlHours, ct)
                    .ConfigureAwait(false);
            }

            var json = JsonNode.Parse(responseBody);
            if (json is null)
                return [];

            // Navigate to result object.
            var resultObj = await NavigateToResultAsync(json, strategy, request, ct)
                .ConfigureAwait(false);
            if (resultObj is null)
                return [];

            return await ExtractAndValidateClaimsAsync(strategy, request, resultObj, ct)
                .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ProviderClaim>> ExtractAndValidateClaimsAsync(
        SearchStrategyConfig strategy,
        ProviderLookupRequest request,
        JsonNode resultNode,
        CancellationToken ct)
    {
        IReadOnlyList<ProviderClaim> claims;
        if (strategy.ReleaseSelection is not null)
        {
            var releaseNode = ApplyReleaseSelection(resultNode, strategy.ReleaseSelection);
            claims = ExtractClaimsWithRelease(resultNode, releaseNode, request.MediaType);
        }
        else
        {
            claims = ExtractClaims(resultNode, request.MediaType);
        }

        claims = NormalizeClaimsForStrategy(strategy, request, claims);
        claims = EnrichComicVineCreatorClaims(claims, resultNode, request);

        if (!ClaimsMatchRequest(claims, request, strategy))
            return [];

        claims = await EnrichClaimsWithTmdbDetailsAsync(claims, resultNode, request, ct)
            .ConfigureAwait(false);
        claims = await EnrichClaimsWithComicVineVolumeAsync(claims, request, ct)
            .ConfigureAwait(false);

        return claims;
    }

    private async Task<IReadOnlyList<ProviderClaim>> EnrichClaimsWithComicVineVolumeAsync(
        IReadOnlyList<ProviderClaim> claims,
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        if (!string.Equals(Name, "comicvine", StringComparison.OrdinalIgnoreCase)
            || request.MediaType != MediaType.Comics)
        {
            return claims;
        }

        var volumeId = claims.FirstOrDefault(claim =>
            string.Equals(claim.Key, BridgeIdKeys.ComicVineVolumeId, StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(volumeId))
        {
            return claims;
        }

        try
        {
            var facts = await TryFetchComicVineVolumeFactsAsync(volumeId, request, ct)
                .ConfigureAwait(false);
            if (facts is null)
            {
                return claims;
            }

            var enriched = claims.ToList();
            AddComicVineVolumeClaim(enriched, MetadataFieldConstants.SequenceTotal, facts.IssueCount?.ToString(CultureInfo.InvariantCulture), 0.9);
            if (enriched.Any(claim => string.Equals(claim.Key, MetadataFieldConstants.SequenceTotal, StringComparison.OrdinalIgnoreCase)))
            {
                AddComicVineVolumeClaim(enriched, MetadataFieldConstants.SequenceTotalScope, "MainSequence", 0.9);
            }

            AddComicVineVolumeClaim(enriched, MetadataFieldConstants.SeriesStartYear, facts.StartYear?.ToString(CultureInfo.InvariantCulture), 0.85);
            AddComicVineVolumeClaim(enriched, MetadataFieldConstants.PublisherField, facts.Publisher, 0.8);
            return enriched;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation(
                ex,
                "{Provider}: ComicVine volume enrichment failed for volume {VolumeId}; issue-level claims will be used",
                Name,
                volumeId);
            return claims;
        }
    }

    private async Task<ComicVineVolumeFacts?> TryFetchComicVineVolumeFactsAsync(
        string? volumeId,
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(volumeId))
            return null;

        var baseUrl = ResolveBaseUrl(request);
        var apiKey = _config.HttpClient?.ApiKey;
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
            return null;

        var url = $"{baseUrl.TrimEnd('/')}/volume/4050-{Uri.EscapeDataString(volumeId)}/?api_key={Uri.EscapeDataString(apiKey)}&format=json";
        using var client = _httpFactory.CreateClient(_config.Name);
        var json = await _rateLimiter.ExecuteAsync(
            Name,
            _config.RateLimit,
            token => client.GetFromJsonAsync<JsonNode>(url, token),
            ct).ConfigureAwait(false);

        var volume = json?["results"];
        if (volume is null)
            return null;

        var issueCount = volume["count_of_issues"]?.GetValue<long?>() is { } count
            ? (int?)Convert.ToInt32(count, CultureInfo.InvariantCulture)
            : null;
        var startYear = TryExtractYear(volume["start_year"]?.GetValue<string>());
        var publisher = volume["publisher"]?["name"]?.GetValue<string>();
        return new ComicVineVolumeFacts(issueCount, startYear, publisher);
    }

    private async Task<ComicVineVolumeFacts?> TryFetchComicVineVolumeFactsForSelectionAsync(
        string? volumeId,
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        try
        {
            return await TryFetchComicVineVolumeFactsAsync(volumeId, request, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(
                ex,
                "{Provider}: ComicVine volume facts unavailable while scoring candidate volume {VolumeId}",
                Name,
                volumeId);
            return null;
        }
    }

    private static void AddComicVineVolumeClaim(
        List<ProviderClaim> claims,
        string key,
        string? value,
        double confidence)
    {
        if (string.IsNullOrWhiteSpace(value)
            || claims.Any(claim => string.Equals(claim.Key, key, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        claims.Add(new ProviderClaim(key, value, confidence));
    }

    private IReadOnlyList<ProviderClaim> EnrichComicVineCreatorClaims(
        IReadOnlyList<ProviderClaim> claims,
        JsonNode resultNode,
        ProviderLookupRequest request)
    {
        if (!string.Equals(Name, "comicvine", StringComparison.OrdinalIgnoreCase)
            || request.MediaType != MediaType.Comics)
        {
            return claims;
        }

        if (JsonPathEvaluator.Evaluate(resultNode, "person_credits") is not JsonArray credits
            || credits.Count == 0)
        {
            return claims;
        }

        var enriched = claims.ToList();
        foreach (var credit in credits)
        {
            if (credit is null)
                continue;

            var name = ExtractFirstString(credit, ["name", "person.name", "credited_name"]);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var role = ExtractFirstString(credit, [
                "role",
                "role.name",
                "credit_type",
                "type",
                "job",
                "person_role"
            ]);
            var targetKey = ResolveComicCreatorClaimKey(role);
            if (targetKey is null)
                continue;

            AddDistinctClaim(enriched, targetKey, name, 0.82);
        }

        return enriched;
    }

    private static string? ResolveComicCreatorClaimKey(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;

        var normalized = NormalizeComicText(role);
        if (normalized.Contains("writer", StringComparison.Ordinal)
            || normalized.Contains("script", StringComparison.Ordinal)
            || normalized.Contains("story", StringComparison.Ordinal))
        {
            return MetadataFieldConstants.Author;
        }

        if (normalized.Contains("artist", StringComparison.Ordinal)
            || normalized.Contains("pencil", StringComparison.Ordinal)
            || normalized.Contains("inker", StringComparison.Ordinal)
            || normalized.Contains("illustrator", StringComparison.Ordinal))
        {
            return MetadataFieldConstants.Illustrator;
        }

        return null;
    }

    private static void AddDistinctClaim(
        List<ProviderClaim> claims,
        string key,
        string? value,
        double confidence)
    {
        if (string.IsNullOrWhiteSpace(value)
            || claims.Any(claim =>
                string.Equals(claim.Key, key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(claim.Value, value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        claims.Add(new ProviderClaim(key, value, confidence));
    }

    private bool ClaimsMatchRequest(
        IReadOnlyList<ProviderClaim> claims,
        ProviderLookupRequest request,
        SearchStrategyConfig strategy)
    {
        if (request.MediaType == MediaType.Music && !MusicAlbumClaimsMatchRequest(claims, request, strategy))
            return false;

        if (string.IsNullOrWhiteSpace(request.Title))
            return true;

        var candidateTitle = claims.FirstOrDefault(c =>
            string.Equals(c.Key, MetadataFieldConstants.Title, StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(candidateTitle))
            return true;

        if (request.MediaType == MediaType.Comics && ComicClaimsMatchRequest(claims, request))
            return true;

        if (request.MediaType == MediaType.Comics && ComicVolumeClaimsMatchRequest(claims, request, strategy))
            return true;

        var titleScore = ComputeWordOverlap(
            CleanTitleForSearch(request.Title) ?? request.Title,
            CleanTitleForSearch(candidateTitle) ?? candidateTitle);
        if (titleScore >= 0.40)
            return true;

        var candidateAuthor = claims.FirstOrDefault(c =>
            string.Equals(c.Key, MetadataFieldConstants.Author, StringComparison.OrdinalIgnoreCase))?.Value;
        var authorScore = !string.IsNullOrWhiteSpace(request.Author) && !string.IsNullOrWhiteSpace(candidateAuthor)
            ? ComputeWordOverlap(request.Author, candidateAuthor)
            : 0.0;

        _logger.LogInformation(
            "{Provider}/{Strategy}: rejected mismatched result '{CandidateTitle}' by '{CandidateAuthor}' for requested '{Title}' by '{Author}' (title={TitleScore:F2}, author={AuthorScore:F2})",
            Name,
            strategy.Name,
            candidateTitle,
            candidateAuthor ?? "-",
            request.Title,
            request.Author ?? "-",
            titleScore,
            authorScore);

        return false;
    }

    private IReadOnlyList<ProviderClaim> NormalizeClaimsForStrategy(
        SearchStrategyConfig strategy,
        ProviderLookupRequest request,
        IReadOnlyList<ProviderClaim> claims)
    {
        if (!string.Equals(Name, "comicvine", StringComparison.OrdinalIgnoreCase)
            || request.MediaType != MediaType.Comics
            || (!strategy.Name.Contains("issue", StringComparison.OrdinalIgnoreCase)
                && !strategy.Name.Contains("volume", StringComparison.OrdinalIgnoreCase)))
        {
            return claims;
        }

        if (strategy.Name.Contains("volume", StringComparison.OrdinalIgnoreCase))
            return NormalizeComicVineVolumeClaims(claims, request);

        return claims
            .Where(claim => !string.Equals(claim.Key, MetadataFieldConstants.Description, StringComparison.OrdinalIgnoreCase))
            .Where(claim => ShouldKeepComicIssueClaim(claim, request))
            .ToList();
    }

    private static IReadOnlyList<ProviderClaim> NormalizeComicVineVolumeClaims(
        IReadOnlyList<ProviderClaim> claims,
        ProviderLookupRequest request)
    {
        var normalized = claims
            .Where(claim => !string.Equals(claim.Key, BridgeIdKeys.ComicVineId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var volumeId = claims.FirstOrDefault(claim =>
                string.Equals(claim.Key, BridgeIdKeys.ComicVineVolumeId, StringComparison.OrdinalIgnoreCase))?.Value
            ?? claims.FirstOrDefault(claim =>
                string.Equals(claim.Key, BridgeIdKeys.ComicVineId, StringComparison.OrdinalIgnoreCase))?.Value;
        AddDistinctClaim(normalized, BridgeIdKeys.ComicVineVolumeId, volumeId, 0.95);

        var series = request.Series
            ?? request.Hints?.GetValueOrDefault(MetadataFieldConstants.Series)
            ?? claims.FirstOrDefault(claim =>
                string.Equals(claim.Key, MetadataFieldConstants.Series, StringComparison.OrdinalIgnoreCase))?.Value
            ?? claims.FirstOrDefault(claim =>
                string.Equals(claim.Key, MetadataFieldConstants.Title, StringComparison.OrdinalIgnoreCase))?.Value;
        AddDistinctClaim(normalized, MetadataFieldConstants.Series, series, 0.9);

        var issue = GetComicIssueHint(request);
        if (!string.IsNullOrWhiteSpace(issue))
        {
            AddDistinctClaim(normalized, MetadataFieldConstants.IssueNumber, issue, 0.72);
            AddDistinctClaim(normalized, MetadataFieldConstants.SeriesPosition, issue, 0.72);
        }

        return normalized;
    }

    private static bool ShouldKeepComicIssueClaim(ProviderClaim claim, ProviderLookupRequest request)
    {
        if (!string.Equals(claim.Key, MetadataFieldConstants.IssueDescription, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(claim.Value))
        {
            return false;
        }

        var preferredLanguage = FirstNonBlank(request.FileLanguage, request.Language);
        var expectsEnglish = string.IsNullOrWhiteSpace(preferredLanguage)
            || preferredLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        return !expectsEnglish || !LooksNonEnglishDescription(claim.Value);
    }

    private bool MusicAlbumClaimsMatchRequest(
        IReadOnlyList<ProviderClaim> claims,
        ProviderLookupRequest request,
        SearchStrategyConfig strategy)
    {
        var requestedAlbum = GetRequestedAlbum(request);
        if (string.IsNullOrWhiteSpace(requestedAlbum))
            return true;

        var candidateAlbum = claims.FirstOrDefault(c =>
            string.Equals(c.Key, MetadataFieldConstants.Album, StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(candidateAlbum))
            return true;

        if (IsStrongAlbumMatch(requestedAlbum, candidateAlbum))
            return true;

        _logger.LogInformation(
            "{Provider}/{Strategy}: rejected music result from album '{CandidateAlbum}' for requested album '{RequestedAlbum}'",
            Name,
            strategy.Name,
            candidateAlbum,
            requestedAlbum);

        return false;
    }

    private static bool ComicClaimsMatchRequest(
        IReadOnlyList<ProviderClaim> claims,
        ProviderLookupRequest request)
    {
        var fileSeries = request.Series
            ?? request.Hints?.GetValueOrDefault(MetadataFieldConstants.Series);
        var fileIssue = GetComicIssueHint(request);
        if (string.IsNullOrWhiteSpace(fileSeries) || string.IsNullOrWhiteSpace(fileIssue))
            return false;

        var candidateSeries = claims.FirstOrDefault(c =>
            string.Equals(c.Key, MetadataFieldConstants.Series, StringComparison.OrdinalIgnoreCase))?.Value;
        var candidateIssue = claims.FirstOrDefault(c =>
                string.Equals(c.Key, "issue_number", StringComparison.OrdinalIgnoreCase))?.Value
            ?? claims.FirstOrDefault(c =>
                string.Equals(c.Key, MetadataFieldConstants.SeriesPosition, StringComparison.OrdinalIgnoreCase))?.Value
            ?? claims.FirstOrDefault(c =>
                string.Equals(c.Key, "issue", StringComparison.OrdinalIgnoreCase))?.Value;

        return !string.IsNullOrWhiteSpace(candidateSeries)
            && !string.IsNullOrWhiteSpace(candidateIssue)
            && AreEquivalentComicText(fileSeries, candidateSeries)
            && AreEquivalentComicOrdinals(fileIssue, candidateIssue);
    }

    private static bool ComicVolumeClaimsMatchRequest(
        IReadOnlyList<ProviderClaim> claims,
        ProviderLookupRequest request,
        SearchStrategyConfig strategy)
    {
        if (!strategy.Name.Contains("volume", StringComparison.OrdinalIgnoreCase))
            return false;

        var fileSeries = request.Series
            ?? request.Hints?.GetValueOrDefault(MetadataFieldConstants.Series);
        if (string.IsNullOrWhiteSpace(fileSeries))
            return false;

        var candidateSeries = claims.FirstOrDefault(c =>
                string.Equals(c.Key, MetadataFieldConstants.Series, StringComparison.OrdinalIgnoreCase))?.Value
            ?? claims.FirstOrDefault(c =>
                string.Equals(c.Key, MetadataFieldConstants.Title, StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(candidateSeries)
            || !AreEquivalentComicText(fileSeries, candidateSeries))
        {
            return false;
        }

        var volumeId = claims.FirstOrDefault(c =>
            string.Equals(c.Key, BridgeIdKeys.ComicVineVolumeId, StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(volumeId))
            return false;

        var supportingSignals = 0;
        var fileIssue = GetComicIssueHint(request);
        var requestedIssueNumber = TryParseLeadingInt(fileIssue);
        var sequenceTotal = TryParseLeadingInt(claims.FirstOrDefault(c =>
            string.Equals(c.Key, MetadataFieldConstants.SequenceTotal, StringComparison.OrdinalIgnoreCase))?.Value);
        if (requestedIssueNumber.HasValue
            && sequenceTotal.HasValue
            && sequenceTotal.Value >= requestedIssueNumber.Value)
        {
            supportingSignals++;
        }

        var requestedPublisher = request.Hints?.GetValueOrDefault(MetadataFieldConstants.PublisherField);
        var candidatePublisher = claims.FirstOrDefault(c =>
            string.Equals(c.Key, MetadataFieldConstants.PublisherField, StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrWhiteSpace(requestedPublisher)
            && !string.IsNullOrWhiteSpace(candidatePublisher)
            && ComputeWordOverlap(requestedPublisher, candidatePublisher) >= 0.75)
        {
            supportingSignals++;
        }

        var requestedYear = TryExtractYear(
            request.Year
            ?? ExtractYearFromTitle(request.Title)
            ?? request.Hints?.GetValueOrDefault("year"));
        var startYear = TryExtractYear(claims.FirstOrDefault(c =>
            string.Equals(c.Key, MetadataFieldConstants.SeriesStartYear, StringComparison.OrdinalIgnoreCase))?.Value);
        if (requestedYear.HasValue
            && startYear.HasValue
            && Math.Abs(requestedYear.Value - startYear.Value) <= 8)
        {
            supportingSignals++;
        }

        return supportingSignals >= 1;
    }

    private async Task<IReadOnlyList<ProviderClaim>> EnrichClaimsWithTmdbDetailsAsync(
        IReadOnlyList<ProviderClaim> claims,
        JsonNode resultNode,
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        var mediaType = request.MediaType;
        if (!string.Equals(Name, "tmdb", StringComparison.OrdinalIgnoreCase)
            || mediaType is not (MediaType.Movies or MediaType.TV)
            || string.IsNullOrWhiteSpace(_config.HttpClient?.ApiKey))
        {
            return claims;
        }

        var tmdbId = claims.FirstOrDefault(c =>
            string.Equals(c.Key, BridgeIdKeys.TmdbId, StringComparison.OrdinalIgnoreCase))?.Value
            ?? resultNode["id"]?.GetValue<long?>()?.ToString(CultureInfo.InvariantCulture)
            ?? resultNode["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(tmdbId))
            return claims;

        var endpoint = mediaType == MediaType.TV ? "tv" : "movie";
        var baseUrl = _config.Endpoints.GetValueOrDefault("api") ?? "https://api.themoviedb.org/3";
        var appendToResponse = mediaType == MediaType.TV ? "aggregate_credits,content_ratings" : "credits,release_dates";
        var url = $"{baseUrl.TrimEnd('/')}/{endpoint}/{Uri.EscapeDataString(tmdbId)}?language=en-US&append_to_response={appendToResponse}&api_key={Uri.EscapeDataString(_config.HttpClient.ApiKey)}";

        try
        {
            var detailCacheKey = BuildCacheKey(url);
            var cacheTtlHours = _config.CacheTtlHours ?? 168;
            JsonNode? details = null;

            if (_responseCache is not null)
            {
                var cached = await _responseCache.FindAsync(detailCacheKey, ct).ConfigureAwait(false);
                if (cached is not null)
                    details = JsonNode.Parse(cached.ResponseJson);
            }

            if (details is null)
            {
                using var client = _httpFactory.CreateClient(_config.Name);
                using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return claims;

                response.EnsureSuccessStatusCode();
                var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                details = JsonNode.Parse(responseBody);

                if (_responseCache is not null && !string.IsNullOrWhiteSpace(responseBody))
                {
                    var etag = response.Headers.ETag?.Tag?.Trim('"');
                    await _responseCache.UpsertAsync(
                        detailCacheKey,
                        _providerId.ToString(),
                        ComputeSha256(url),
                        responseBody,
                        etag,
                        cacheTtlHours,
                        ct).ConfigureAwait(false);
                }
            }

            if (details is null)
                return claims;

            var enriched = claims.ToList();
            AddIfMissing(enriched, MetadataFieldConstants.Description, details["overview"]?.GetValue<string>(), 0.85);
            AddIfMissing(enriched, MetadataFieldConstants.ShortDescription, details["overview"]?.GetValue<string>(), 0.84);
            AddIfMissing(enriched, MetadataFieldConstants.Tagline, details["tagline"]?.GetValue<string>(), 0.70);
            AddIfMissing(enriched, MetadataFieldConstants.Runtime, details["runtime"]?.GetValue<long?>()?.ToString(CultureInfo.InvariantCulture), 0.90);
            AddIfMissing(enriched, "content_rating", ExtractTmdbContentRating(details, mediaType), 0.88);
            if (mediaType == MediaType.Movies)
            {
                AddTmdbMovieCollectionClaims(enriched, details);
                await EnrichTmdbMovieCollectionSequenceAsync(enriched, details, request, ct)
                    .ConfigureAwait(false);
            }

            AddTmdbProductionClaims(enriched, details, mediaType);
            AddTmdbCastClaims(enriched, details, mediaType);
            AddTmdbCrewClaims(enriched, details, mediaType);

            return enriched;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "{Provider}: detail enrichment failed for {MediaType} id {TmdbId}", Name, mediaType, tmdbId);
            return claims;
        }
    }

    private static void AddIfMissing(List<ProviderClaim> claims, string key, string? value, double confidence)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (claims.Any(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.Value, value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        claims.Add(new ProviderClaim(key, value, confidence));
    }

    private static void AddTmdbMovieCollectionClaims(List<ProviderClaim> claims, JsonNode details)
    {
        var collection = details["belongs_to_collection"];
        if (collection is null)
            return;

        var collectionId = collection["id"]?.GetValue<long?>()?.ToString(CultureInfo.InvariantCulture)
            ?? collection["id"]?.GetValue<string>();
        var collectionName = collection["name"]?.GetValue<string>();

        AddIfMissing(claims, "tmdb_collection_id", collectionId, 1.0);
        AddIfMissing(claims, "tmdb_collection_name", collectionName, 0.94);
        AddIfMissing(claims, MetadataFieldConstants.Series, collectionName, 0.90);
    }

    private async Task EnrichTmdbMovieCollectionSequenceAsync(
        List<ProviderClaim> claims,
        JsonNode details,
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        var collection = details["belongs_to_collection"];
        if (collection is null)
            return;

        var collectionId = collection["id"]?.GetValue<long?>()?.ToString(CultureInfo.InvariantCulture)
            ?? collection["id"]?.GetValue<string>();
        var movieId = details["id"]?.GetValue<long?>()?.ToString(CultureInfo.InvariantCulture)
            ?? details["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(collectionId) || string.IsNullOrWhiteSpace(movieId))
            return;

        var baseUrl = _config.Endpoints.GetValueOrDefault("api") ?? ResolveBaseUrl(request);
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(_config.HttpClient?.ApiKey))
            return;

        var language = $"{request.Language.ToLowerInvariant()}-{request.Country.ToUpperInvariant()}";
        var url = $"{baseUrl.TrimEnd('/')}/collection/{Uri.EscapeDataString(collectionId)}?language={Uri.EscapeDataString(language)}&api_key={Uri.EscapeDataString(_config.HttpClient.ApiKey)}";

        try
        {
            var collectionDetails = await FetchJsonWithCacheAsync(url, ct).ConfigureAwait(false);
            var parts = collectionDetails?["parts"]?.AsArray()
                .Where(part => part is not null)
                .Select(part => new TmdbCollectionPart(
                    Id: part?["id"]?.GetValue<long?>()?.ToString(CultureInfo.InvariantCulture) ?? part?["id"]?.GetValue<string>() ?? string.Empty,
                    Title: FirstNonBlank(part?["title"]?.GetValue<string>(), part?["name"]?.GetValue<string>()) ?? string.Empty,
                    ReleaseDate: ParseTmdbReleaseDate(part?["release_date"]?.GetValue<string>())))
                .Where(part => !string.IsNullOrWhiteSpace(part.Id))
                .OrderBy(part => part.ReleaseDate ?? DateOnly.MaxValue)
                .ThenBy(part => part.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (parts is null or { Count: 0 })
                return;

            var position = parts.FindIndex(part => string.Equals(part.Id, movieId, StringComparison.OrdinalIgnoreCase));
            if (position < 0)
                return;

            AddIfMissing(claims, MetadataFieldConstants.SeriesPosition, (position + 1).ToString(CultureInfo.InvariantCulture), 0.90);
            AddIfMissing(claims, MetadataFieldConstants.SequenceTotal, parts.Count.ToString(CultureInfo.InvariantCulture), 0.90);
            AddIfMissing(claims, MetadataFieldConstants.SequenceTotalScope, SequenceCountScope.MainSequence.ToString(), 0.90);
            AddIfMissing(claims, MetadataFieldConstants.SequenceFormat, SequenceFormat.Standard.ToString(), 0.80);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(
                ex,
                "{Provider}: TMDB collection sequence enrichment failed for collection {CollectionId}",
                Name,
                collectionId);
        }
    }

    private async Task<JsonNode?> FetchJsonWithCacheAsync(string url, CancellationToken ct)
    {
        var cacheKey = BuildCacheKey(url);
        var cacheTtlHours = _config.CacheTtlHours ?? 168;

        if (_responseCache is not null)
        {
            var cached = await _responseCache.FindAsync(cacheKey, ct).ConfigureAwait(false);
            if (cached is not null)
                return JsonNode.Parse(cached.ResponseJson);
        }

        using var client = _httpFactory.CreateClient(_config.Name);
        using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (_responseCache is not null && !string.IsNullOrWhiteSpace(responseBody))
        {
            var etag = response.Headers.ETag?.Tag?.Trim('"');
            await _responseCache.UpsertAsync(
                cacheKey,
                _providerId.ToString(),
                ComputeSha256(url),
                responseBody,
                etag,
                cacheTtlHours,
                ct).ConfigureAwait(false);
        }

        return string.IsNullOrWhiteSpace(responseBody) ? null : JsonNode.Parse(responseBody);
    }

    private static DateOnly? ParseTmdbReleaseDate(string? value)
        => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    private static string? ExtractTmdbContentRating(JsonNode details, MediaType mediaType)
    {
        var results = mediaType == MediaType.TV
            ? details["content_ratings"]?["results"]?.AsArray()
            : details["release_dates"]?["results"]?.AsArray();
        if (results is null)
            return null;

        foreach (var country in new[] { "US", "GB", "CA", "AU" })
        {
            var countryNode = results.FirstOrDefault(node =>
                string.Equals(node?["iso_3166_1"]?.GetValue<string>(), country, StringComparison.OrdinalIgnoreCase));
            var rating = mediaType == MediaType.TV
                ? countryNode?["rating"]?.GetValue<string>()
                : countryNode?["release_dates"]?.AsArray()
                    .Select(node => node?["certification"]?.GetValue<string>())
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(rating))
                return rating;
        }

        return null;
    }
    private static void AddTmdbCastClaims(List<ProviderClaim> claims, JsonNode details, MediaType mediaType)
    {
        var castArray = mediaType == MediaType.TV
            ? details["aggregate_credits"]?["cast"]?.AsArray()
            : details["credits"]?["cast"]?.AsArray();

        if (castArray is null)
            return;

        foreach (var castNode in castArray
            .Where(node => node is not null)
            .OrderBy(node => node?["order"]?.GetValue<int?>() ?? int.MaxValue)
            .ThenBy(node => node?["name"]?.GetValue<string>() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Take(30))
        {
            var name = castNode?["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            claims.Add(new ProviderClaim(MetadataFieldConstants.CastMember, name, 0.90));
            AddIfPresent(claims, "cast_member_character", ExtractTmdbCharacterName(castNode, mediaType), 0.90);

            var tmdbPersonId = castNode?["id"]?.GetValue<long?>()?.ToString(CultureInfo.InvariantCulture)
                ?? castNode?["id"]?.GetValue<string>();
            AddIfPresent(claims, "cast_member_tmdb_id", tmdbPersonId, 0.92);

            var profilePath = castNode?["profile_path"]?.GetValue<string>();
            AddIfPresent(claims, "cast_member_profile_url", BuildTmdbProfileUrl(profilePath), 0.90);
        }
    }

    private static string? ExtractTmdbCharacterName(JsonNode? castNode, MediaType mediaType)
    {
        if (castNode is null)
            return null;

        if (mediaType == MediaType.TV)
        {
            var roles = castNode["roles"]?.AsArray()
                .Where(role => role is not null)
                .OrderBy(role => role?["episode_count"]?.GetValue<int?>() ?? 0)
                .Reverse()
                .Select(role => role?["character"]?.GetValue<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            if (roles is { Count: > 0 })
                return string.Join(" / ", roles);
        }

        return castNode["character"]?.GetValue<string>();
    }

    private static void AddIfPresent(List<ProviderClaim> claims, string key, string? value, double confidence)
    {
        if (!string.IsNullOrWhiteSpace(value))
            claims.Add(new ProviderClaim(key, value, confidence));
    }

    private static string? BuildTmdbProfileUrl(string? profilePath)
        => string.IsNullOrWhiteSpace(profilePath)
            ? null
            : $"https://image.tmdb.org/t/p/original/{profilePath.TrimStart('/')}";

    private static void AddTmdbProductionClaims(List<ProviderClaim> claims, JsonNode details, MediaType mediaType)
    {
        if (mediaType == MediaType.TV)
        {
            var network = details["networks"]?.AsArray()
                .Where(node => node is not null)
                .Select(node => new
                {
                    Name = node?["name"]?.GetValue<string>(),
                    LogoPath = node?["logo_path"]?.GetValue<string>(),
                })
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Name));

            AddIfMissing(claims, MetadataFieldConstants.Network, network?.Name, 0.88);
            AddIfMissing(claims, "network_logo_url", BuildTmdbProfileUrl(network?.LogoPath), 0.84);
        }

        var companies = details["production_companies"]?.AsArray()
            .Where(node => node is not null)
            .Select(node => new
            {
                Name = node?["name"]?.GetValue<string>(),
                LogoPath = node?["logo_path"]?.GetValue<string>(),
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Take(5)
            .ToList();

        if (companies is not { Count: > 0 })
            return;

        var studio = companies.First();
        AddIfMissing(claims, "studio", studio.Name, 0.88);
        AddIfMissing(claims, "studio_logo_url", BuildTmdbProfileUrl(studio.LogoPath), 0.84);
        AddIfMissing(claims, "production_company", string.Join("; ", companies.Select(item => item.Name)), 0.86);
    }

    private static void AddTmdbCrewClaims(List<ProviderClaim> claims, JsonNode details, MediaType mediaType)
    {
        var crewArray = mediaType == MediaType.TV
            ? details["aggregate_credits"]?["crew"]?.AsArray()
            : details["credits"]?["crew"]?.AsArray();

        if (crewArray is null)
            return;

        foreach (var crewNode in crewArray.Where(node => node is not null))
        {
            var name = crewNode?["name"]?.GetValue<string>();
            var role = ResolveTmdbCrewRole(crewNode);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(role))
                continue;

            var key = role.ToLowerInvariant() switch
            {
                "director" => "director",
                "screenwriter" => "screenwriter",
                "composer" => "composer",
                "producer" => "producer",
                _ => null,
            };
            if (key is null)
                continue;

            claims.Add(new ProviderClaim(key, name, 0.88));

            var tmdbPersonId = crewNode?["id"]?.GetValue<long?>()?.ToString(CultureInfo.InvariantCulture)
                ?? crewNode?["id"]?.GetValue<string>();
            AddIfPresent(claims, $"{key}_tmdb_id", tmdbPersonId, 0.92);

            var profilePath = crewNode?["profile_path"]?.GetValue<string>();
            AddIfPresent(claims, $"{key}_profile_url", BuildTmdbProfileUrl(profilePath), 0.90);
        }
    }

    private static string? ResolveTmdbCrewRole(JsonNode? crewNode)
    {
        var job = crewNode?["job"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(job))
        {
            job = crewNode?["jobs"]?.AsArray()
                .Select(node => node?["job"]?.GetValue<string>())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        if (string.IsNullOrWhiteSpace(job))
            return null;

        if (job.Contains("Director", StringComparison.OrdinalIgnoreCase))
            return "Director";
        if (job.Contains("Screenplay", StringComparison.OrdinalIgnoreCase)
            || job.Contains("Writer", StringComparison.OrdinalIgnoreCase)
            || job.Contains("Story", StringComparison.OrdinalIgnoreCase))
            return "Screenwriter";
        if (job.Contains("Composer", StringComparison.OrdinalIgnoreCase)
            || job.Contains("Music", StringComparison.OrdinalIgnoreCase))
            return "Composer";
        if (job.Contains("Producer", StringComparison.OrdinalIgnoreCase))
            return "Producer";

        return null;
    }

    /// <summary>
    /// Builds a cache key from the provider ID and the request URL hash.
    /// </summary>
    private string BuildCacheKey(string url) =>
        $"{_providerId}:{ComputeSha256(url)}";

    /// <summary>
    /// Computes a SHA-256 hash of the input string (for URL dedup).
    /// </summary>
    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // -- URL building --------------------------------------------------------

    private string BuildUrl(SearchStrategyConfig strategy, ProviderLookupRequest request, int? limitOverride = null)
    {
        var baseUrl = ResolveBaseUrl(request);
        var template = strategy.UrlTemplate;

        // Clean the title for search: strip trailing (YYYY) and SxxExx patterns.
        // For TV strategies, prefer ShowName (the series/show title) over Title
        // (which may be the episode title extracted from the filename).
        var isEpisodicStrategy = strategy.MediaTypes?.Contains("TV") == true;
        var rawTitle = isEpisodicStrategy
            && !string.IsNullOrWhiteSpace(request.ShowName)
            ? request.ShowName
            : request.Title;
        var searchTitle = CleanTitleForSearch(rawTitle) ?? rawTitle;
        var yearFromTitle = request.Year
            ?? ExtractYearFromTitle(request.Title)
            ?? request.Hints?.GetValueOrDefault("year");

        // Build {query} placeholder from query_template if specified.
        var query = string.Empty;
        if (!string.IsNullOrEmpty(strategy.QueryTemplate))
        {
            query = strategy.QueryTemplate;
            query = ReplacePlaceholder(query, "{title}", searchTitle, encode: false);
            query = ReplacePlaceholder(query, "{author}", request.Author, encode: false);
            query = ReplacePlaceholder(query, "{narrator}", request.Narrator, encode: false);
            query = ReplacePlaceholder(query, "{show_name}", request.ShowName, encode: false);
            query = ReplacePlaceholder(query, "{album}", request.Album, encode: false);
            query = ReplacePlaceholder(query, "{artist}", request.Artist, encode: false);
            query = ReplacePlaceholder(query, "{director}", request.Director, encode: false);
            query = ReplacePlaceholder(query, "{composer}", request.Composer, encode: false);
            // Remove dangling Lucene operators when optional fields are empty.
            // e.g. "{title} AND artist:{author}" ? "Bohemian Rhapsody AND artist:" when author is null
            // ? becomes "Bohemian Rhapsody" after cleanup.
            query = Regex.Replace(query, @"\s+AND\s+\w+:\s*$", string.Empty, RegexOptions.IgnoreCase);
            query = Regex.Replace(query, @"^\s*AND\s+\w+:\s*", string.Empty, RegexOptions.IgnoreCase);
            // Trim and collapse whitespace from unfilled placeholders.
            query = Regex.Replace(query.Trim(), @"\s+", " ");
        }

        // Replace all placeholders in the URL template.
        var url = template;
        url = ReplacePlaceholder(url, "{base_url}", baseUrl, encode: false);
        url = ReplacePlaceholder(url, "{query}", query, encode: true);
        url = ReplacePlaceholder(url, "{title}", searchTitle, encode: true);
        url = ReplacePlaceholder(url, "{author}", request.Author, encode: true);
        url = ReplacePlaceholder(url, "{isbn}", request.Isbn, encode: true);
        url = ReplacePlaceholder(url, "{asin}", request.Asin, encode: true);
        url = ReplacePlaceholder(url, "{narrator}", request.Narrator, encode: true);
        url = ReplacePlaceholder(url, "{apple_books_id}", request.AppleBooksId, encode: true);
        url = ReplacePlaceholder(url, "{audible_id}", request.AudibleId, encode: true);
        url = ReplacePlaceholder(url, "{tmdb_id}", request.TmdbId, encode: true);
        url = ReplacePlaceholder(url, "{imdb_id}", request.ImdbId, encode: true);
        url = ReplacePlaceholder(url, "{show_name}", request.ShowName, encode: true);
        url = ReplacePlaceholder(url, "{album}", request.Album, encode: true);
        url = ReplacePlaceholder(url, "{artist}", request.Artist, encode: true);
        url = ReplacePlaceholder(url, "{director}", request.Director, encode: true);
        url = ReplacePlaceholder(url, "{composer}", request.Composer, encode: true);
        url = ReplacePlaceholder(url, "{season_number}", request.SeasonNumber, encode: true);
        url = ReplacePlaceholder(url, "{episode_number}", request.EpisodeNumber, encode: true);
        url = ReplacePlaceholder(url, "{track_number}", request.TrackNumber, encode: true);
        url = ReplacePlaceholder(url, "{series}", request.Series, encode: true);
        url = ReplacePlaceholder(url, "{genre}", request.Genre, encode: true);
        url = ReplacePlaceholder(url, "{api_key}", _config.HttpClient?.ApiKey, encode: true);
        url = ReplacePlaceholder(url, "{lang}",    request.Language.ToLowerInvariant(), encode: true);
        url = ReplacePlaceholder(url, "{country}", request.Country.ToUpperInvariant(),  encode: true);
        url = ReplacePlaceholder(url, "{year}",    yearFromTitle ?? string.Empty, encode: true);
        url = ReplacePlaceholder(url, "{tvdb_id}", ResolveRequestField(request, BridgeIdKeys.TvdbId), encode: true);
        url = ReplacePlaceholder(url, "{musicbrainz_id}", ResolveRequestField(request, BridgeIdKeys.MusicBrainzId), encode: true);
        url = ReplacePlaceholder(
            url,
            "{musicbrainz_release_group_id}",
            ResolveRequestField(request, BridgeIdKeys.MusicBrainzReleaseGroupId),
            encode: true);
        url = ReplacePlaceholder(url, "{comic_vine_id}", ResolveRequestField(request, BridgeIdKeys.ComicVineId), encode: true);

        // {limit} — replaced with the caller-supplied override (fetch path uses fetch_limit,
        // search path uses the manual search limit). Falls back to max_results or 25.
        var resolvedLimit = limitOverride
            ?? (strategy.MaxResults > 0 ? strategy.MaxResults : 25);
        url = ReplacePlaceholder(url, "{limit}", resolvedLimit.ToString(), encode: false);

        if (request.PriorProviderBridgeIds is { Count: > 0 })
        {
            foreach (var (key, value) in request.PriorProviderBridgeIds)
            {
                var placeholder = $"{{{key}}}";
                if (url.Contains(placeholder, StringComparison.Ordinal) && !string.IsNullOrEmpty(value))
                    url = ReplacePlaceholder(url, placeholder, value, encode: true);
            }
        }

        // Generic hint-based placeholder resolution — any remaining {key} placeholders
        // are resolved from the Hints dictionary, enabling zero-code config additions.
        if (request.Hints is { Count: > 0 })
        {
            foreach (var (key, value) in request.Hints)
            {
                var placeholder = $"{{{key}}}";
                if (url.Contains(placeholder, StringComparison.Ordinal) && !string.IsNullOrEmpty(value))
                    url = ReplacePlaceholder(url, placeholder, value, encode: true);
            }
        }

        return url;
    }

    private string ResolveBaseUrl(ProviderLookupRequest request)
    {
        // Prefer BaseUrl from the harvesting service (populated from config endpoints).
        if (!string.IsNullOrEmpty(request.BaseUrl))
            return request.BaseUrl.TrimEnd('/');

        // Fall back to the first endpoint in the provider config.
        if (_config.Endpoints.Count > 0)
        {
            var first = _config.Endpoints.Values.First();
            return first.TrimEnd('/');
        }

        return string.Empty;
    }

    private static string ReplacePlaceholder(string template, string placeholder, string? value, bool encode)
    {
        if (!template.Contains(placeholder, StringComparison.Ordinal))
            return template;

        var replacement = value ?? string.Empty;
        if (encode && !string.IsNullOrEmpty(replacement))
            replacement = Uri.EscapeDataString(replacement);

        return template.Replace(placeholder, replacement, StringComparison.Ordinal);
    }

    /// <summary>
    /// Strips trailing year suffixes like "(2017)" and TV episode designations like "S01E01"
    /// from titles so that search APIs receive clean query strings.
    /// </summary>
    internal static string? CleanTitleForSearch(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return title;

        // Strip trailing (YYYY) — e.g. "Blade Runner 2049 (2017)" ? "Blade Runner 2049"
        var cleaned = Regex.Replace(title, @"\s*\(\d{4}\)\s*$", string.Empty);

        // Strip trailing SxxExx — e.g. "Breaking Bad S01E01" ? "Breaking Bad"
        cleaned = Regex.Replace(cleaned, @"\s*S\d{1,2}E\d{1,2}\s*$", string.Empty, RegexOptions.IgnoreCase);

        return cleaned.Trim();
    }

    /// <summary>
    /// Extracts a four-digit year from a trailing "(YYYY)" suffix if present.
    /// Returns null when no year suffix is found.
    /// </summary>
    internal static string? ExtractYearFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var match = Regex.Match(title, @"\((\d{4})\)\s*$");
        return match.Success ? match.Groups[1].Value : null;
    }

    // -- Result navigation ---------------------------------------------------

    private async Task<JsonNode?> NavigateToResultAsync(
        JsonNode json,
        SearchStrategyConfig strategy,
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        // If no results_path, treat the whole response as the result.
        if (string.IsNullOrEmpty(strategy.ResultsPath))
            return json;

        var resultsNode = JsonPathEvaluator.Evaluate(json, strategy.ResultsPath);
        if (resultsNode is not JsonArray arr || arr.Count == 0)
            return null;

        // Title + author validation: applies to ALL strategies (lookup and search).
        // Prevents wrong books from being accepted — e.g. study guides by different
        // authors, or an Apple ID lookup returning a completely different work.
        //
        // Strategy: prefer author-matched results. If no author match, fall back
        // to title-only matching (handles pen names where the listed author on
        // the retailer differs from the embedded author).
        var comicIssueResult = await TrySelectComicIssueResultAsync(arr, request, ct)
            .ConfigureAwait(false);
        if (comicIssueResult is not null)
            return comicIssueResult;

        if (ShouldApplyMusicAlbumGuard(strategy, request))
            return TrySelectMusicAlbumScopedResult(arr, request);

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            // Clean the query title for matching — strip "(YYYY)" and "SxxExx" so
            // word-overlap scoring isn't penalised by filename-derived suffixes.
            var cleanedQueryTitle = CleanTitleForSearch(request.Title) ?? request.Title;

            var titlePaths  = new[] { "trackName", "collectionName", "title", "name", "issue", "series.name", "series", "volumeName" };
            var authorPaths = new[] { "artistName", "author", "authors", "creator" };

            var applyDerivativeGuard = request.MediaType is MediaType.Books or MediaType.Audiobooks;
            var sourceLooksDerivative = applyDerivativeGuard
                && RetailCandidateQualityGuard.LooksDerivative(
                    request.Title,
                    genres: string.IsNullOrWhiteSpace(request.Genre) ? null : [request.Genre]);
            var scored = new List<(JsonNode Node, double TitleScore, double AuthorScore, bool Derivative)>();
            foreach (var node in arr)
            {
                if (node is null) continue;

                // Try all title paths and keep the best score. Comic providers
                // may expose both issue-level and series-level names, and the
                // series name can be the better match for a broad query.
                var bestTitleScore = 0.0;
                string? bestNodeTitle = null;
                foreach (var tp in titlePaths)
                {
                    var val = JsonPathEvaluator.Evaluate(node, tp);
                    if (val is null) continue;
                    var s = JsonPathEvaluator.GetStringValue(val);
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    var score = ComputeWordOverlap(cleanedQueryTitle, s);
                    if (score > bestTitleScore)
                    {
                        bestTitleScore = score;
                        bestNodeTitle = s;
                    }
                }
                var nodeAuthor = ExtractFirstString(node, authorPaths);

                if (string.IsNullOrWhiteSpace(bestNodeTitle)) continue;

                var titleScore  = bestTitleScore;
                var authorScore = !string.IsNullOrWhiteSpace(request.Author) && !string.IsNullOrWhiteSpace(nodeAuthor)
                    ? ComputeWordOverlap(request.Author, nodeAuthor)
                    : 0.0;

                var nodeDescription = ExtractFirstString(node, ["description", "shortDescription", "longDescription"]);
                var nodeGenre = ExtractFirstString(node, ["primaryGenreName", "genre", "genres"]);
                var derivative = applyDerivativeGuard
                    && !sourceLooksDerivative
                    && RetailCandidateQualityGuard.LooksDerivative(
                        bestNodeTitle,
                        nodeDescription,
                        string.IsNullOrWhiteSpace(nodeGenre) ? null : [nodeGenre]);

                scored.Add((node, titleScore, authorScore, derivative));
            }

            if (scored.Count == 0)
            {
                // No results had a recognisable title field — skip validation
                // and fall through to result_index selection rather than
                // rejecting all results from providers with non-standard schemas.
                var fallbackIndex = Math.Clamp(strategy.ResultIndex, 0, arr.Count - 1);
                return arr[fallbackIndex];
            }

            // Tier 1: prefer results where both author AND title match.
            var selectable = scored.Where(s => !s.Derivative).ToList();
            if (applyDerivativeGuard && !sourceLooksDerivative && selectable.Count == 0)
                return null;

            if (selectable.Count == 0)
                selectable = scored;

            var authorMatched = selectable.Where(s => s.AuthorScore >= 0.50).ToList();
            if (authorMatched.Count > 0)
                return authorMatched.OrderByDescending(s => s.TitleScore).First().Node;

            // Tier 2: no author match — fall back to title match (>= 0.40).
            // F1 >= 0.40 means at least moderate word overlap between query and candidate.
            // Short queries (e.g. "Batman") have low precision against longer candidate
            // titles (e.g. "Absolute Batman (2024) #1") but full coverage — 0.40 allows
            // these while still rejecting completely unrelated results.
            var bestByTitle = selectable.OrderByDescending(s => s.TitleScore).First();
            return bestByTitle.TitleScore >= 0.40 ? bestByTitle.Node : null;
        }

        var index = Math.Clamp(strategy.ResultIndex, 0, arr.Count - 1);
        return arr[index];
    }

    private async Task<JsonNode?> TrySelectComicIssueResultAsync(
        JsonArray arr,
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        if (!string.Equals(Name, "comicvine", StringComparison.OrdinalIgnoreCase)
            || request.MediaType != MediaType.Comics)
            return null;

        var fileSeries = request.Series
            ?? request.Hints?.GetValueOrDefault(MetadataFieldConstants.Series);
        var fileIssue = GetComicIssueHint(request);
        if (string.IsNullOrWhiteSpace(fileSeries) || string.IsNullOrWhiteSpace(fileIssue))
            return null;

        var cleanedQueryTitle = !string.IsNullOrWhiteSpace(request.Title)
            ? CleanTitleForSearch(request.Title) ?? request.Title
            : fileSeries;
        var requestedYear = TryExtractYear(
            request.Year
            ?? ExtractYearFromTitle(request.Title)
            ?? request.Hints?.GetValueOrDefault("year"));
        var requestedIssueNumber = TryParseLeadingInt(fileIssue);
        var matching = new List<ComicIssueCandidate>();

        foreach (var node in arr)
        {
            if (node is null)
                continue;

            var candidateSeries = ExtractFirstString(node,
                ["volume.name", "series.name", "series", "volumeName", "volume"]);
            var candidateIssue = ExtractFirstString(node,
                ["issue_number", "issueNumber", "number", "issue"]);

            if (string.IsNullOrWhiteSpace(candidateSeries)
                || string.IsNullOrWhiteSpace(candidateIssue)
                || !AreEquivalentComicText(fileSeries, candidateSeries)
                || !AreEquivalentComicOrdinals(fileIssue, candidateIssue))
            {
                continue;
            }

            matching.Add(BuildComicIssueCandidate(node, cleanedQueryTitle, requestedYear));
        }

        var runScopedIssue = await TryFetchComicVineIssueFromPreferredVolumeAsync(
                fileSeries,
                fileIssue,
                requestedYear,
                requestedIssueNumber,
                request,
                ct)
            .ConfigureAwait(false);
        if (runScopedIssue is not null)
        {
            var baseRunScopedCandidate = BuildComicIssueCandidate(runScopedIssue, cleanedQueryTitle, requestedYear);
            var runScopedCandidate = baseRunScopedCandidate with
            {
                BaseScore = baseRunScopedCandidate.BaseScore + 0.35
            };
            if (!matching.Any(candidate => SameComicVineIssue(candidate.Node, runScopedCandidate.Node)))
                matching.Add(runScopedCandidate);
        }

        if (matching.Count == 0)
            return null;

        if (matching.Count > 1)
        {
            var enriched = new List<ComicIssueCandidate>(matching.Count);
            foreach (var candidate in matching)
            {
                var facts = await TryFetchComicVineVolumeFactsForSelectionAsync(candidate.VolumeId, request, ct)
                    .ConfigureAwait(false);
                enriched.Add(candidate with
                {
                    VolumeStartYear = candidate.VolumeStartYear ?? facts?.StartYear,
                    VolumeIssueCount = facts?.IssueCount,
                    Publisher = FirstNonBlank(candidate.Publisher, facts?.Publisher)
                });
            }

            matching = enriched;
        }

        return matching
            .Select(item => item with
            {
                BaseScore = item.BaseScore
                    + ScoreVolumeStartYearProximity(requestedYear, item.VolumeStartYear)
                    + ScoreVolumeIssueCount(requestedIssueNumber, item.VolumeIssueCount)
                    + ScoreComicPublisherAffinity(request, item.Publisher)
            })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.VolumeIssueCount ?? 0)
            .ThenByDescending(item => item.VolumeStartYear ?? 0)
            .ThenBy(item => item.CandidateYear ?? int.MaxValue)
            .Select(item => item.Node)
            .FirstOrDefault();
    }

    private static ComicIssueCandidate BuildComicIssueCandidate(
        JsonNode node,
        string cleanedQueryTitle,
        int? requestedYear)
    {
        var candidateTitle = ExtractFirstString(node, ["name", "title", "issue"]);
        var titleScore = string.IsNullOrWhiteSpace(candidateTitle)
            ? 0.0
            : ComputeWordOverlap(cleanedQueryTitle, candidateTitle);
        var candidateDescription = ExtractFirstString(node, ["description", "deck", "shortDescription", "longDescription"]);
        var candidateYear = TryExtractYear(
            ExtractFirstString(node, ["cover_date", "store_date", "date_added", "start_year", "year"]));
        var yearScore = ScoreYearProximity(requestedYear, candidateYear);
        var languageScore = LooksNonEnglishDescription(candidateDescription) ? -0.25 : 0.03;
        var volumeId = ExtractFirstString(node, ["volume.id", "volumeId"]);
        var volumeStartYear = TryExtractYear(ExtractFirstString(node, ["volume.start_year", "volume.startYear"]));
        var publisher = ExtractFirstString(node, ["volume.publisher.name", "publisher.name", "publisher"]);

        return new ComicIssueCandidate(
            node,
            BaseScore: 1.0 + titleScore * 0.02 + yearScore + languageScore,
            CandidateYear: candidateYear,
            VolumeId: volumeId,
            VolumeStartYear: volumeStartYear,
            VolumeIssueCount: null,
            Publisher: publisher);
    }

    private async Task<JsonNode?> TryFetchComicVineIssueFromPreferredVolumeAsync(
        string fileSeries,
        string fileIssue,
        int? requestedYear,
        int? requestedIssueNumber,
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        if (!requestedIssueNumber.HasValue)
            return null;

        try
        {
            var volumes = await TrySelectComicVineVolumesAsync(fileSeries, requestedYear, requestedIssueNumber, request, ct)
                .ConfigureAwait(false);
            if (volumes.Count == 0)
                return null;

            var baseUrl = ResolveBaseUrl(request);
            var apiKey = _config.HttpClient?.ApiKey;
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
                return null;

            foreach (var volume in volumes)
            {
                var volumeId = volume.VolumeId;
                var issueUrl = $"{baseUrl.TrimEnd('/')}/issues/?api_key={Uri.EscapeDataString(apiKey)}&filter=volume:{Uri.EscapeDataString(volumeId)},issue_number:{Uri.EscapeDataString(requestedIssueNumber.Value.ToString(CultureInfo.InvariantCulture))}&format=json";
                var issueJson = await FetchComicVineJsonAsync(issueUrl, ct).ConfigureAwait(false);
                var issues = issueJson?["results"]?.AsArray();
                if (issues is null)
                    continue;

                var issue = issues
                    .Where(candidate => candidate is not null)
                    .FirstOrDefault(candidate =>
                    {
                        var candidateIssue = ExtractFirstString(candidate!, ["issue_number", "issueNumber", "number", "issue"]);
                        var candidateVolumeId = ExtractFirstString(candidate!, ["volume.id", "volumeId"]);
                        return !string.IsNullOrWhiteSpace(candidateIssue)
                            && AreEquivalentComicOrdinals(fileIssue, candidateIssue)
                            && string.Equals(candidateVolumeId, volumeId, StringComparison.OrdinalIgnoreCase);
                    });
                if (issue is not null)
                    return issue;
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(
                ex,
                "{Provider}: ComicVine run-scoped issue lookup failed for {Series} #{Issue}",
                Name,
                fileSeries,
                fileIssue);
            return null;
        }
    }

    private async Task<ComicVineVolumeSearchCandidate?> TrySelectComicVineVolumeAsync(
        string fileSeries,
        int? requestedYear,
        int? requestedIssueNumber,
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        var candidates = await TrySelectComicVineVolumesAsync(
                fileSeries,
                requestedYear,
                requestedIssueNumber,
                request,
                ct)
            .ConfigureAwait(false);
        return candidates.FirstOrDefault();
    }

    private async Task<IReadOnlyList<ComicVineVolumeSearchCandidate>> TrySelectComicVineVolumesAsync(
        string fileSeries,
        int? requestedYear,
        int? requestedIssueNumber,
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        var baseUrl = ResolveBaseUrl(request);
        var apiKey = _config.HttpClient?.ApiKey;
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
            return [];

        var url = $"{baseUrl.TrimEnd('/')}/search/?api_key={Uri.EscapeDataString(apiKey)}&query={Uri.EscapeDataString(fileSeries)}&resources=volume&limit=25&format=json";
        var json = await FetchComicVineJsonAsync(url, ct).ConfigureAwait(false);
        var volumes = json?["results"]?.AsArray();
        if (volumes is null)
            return [];

        var scored = new List<ComicVineVolumeSearchCandidate>();
        foreach (var volume in volumes)
        {
            if (volume is null)
                continue;

            var name = ExtractFirstString(volume, ["name", "title"]);
            if (string.IsNullOrWhiteSpace(name) || !AreEquivalentComicText(fileSeries, name))
                continue;

            var volumeId = ExtractFirstString(volume, ["id"]);
            if (string.IsNullOrWhiteSpace(volumeId))
                continue;

            var issueCount = TryParseLeadingInt(ExtractFirstString(volume, ["count_of_issues", "issue_count"]));
            if (requestedIssueNumber.HasValue && issueCount.HasValue && issueCount.Value < requestedIssueNumber.Value)
                continue;

            var startYear = TryExtractYear(ExtractFirstString(volume, ["start_year", "year"]));
            var publisher = ExtractFirstString(volume, ["publisher.name", "publisher"]);
            var score = 1.0
                + ScoreVolumeStartYearProximity(requestedYear, startYear)
                + ScoreVolumeIssueCount(requestedIssueNumber, issueCount)
                + ScoreComicPublisherAffinity(request, publisher)
                + ScoreLikelyOriginalComicRun(issueCount);

            scored.Add(new ComicVineVolumeSearchCandidate(volumeId, score, issueCount, startYear, publisher));
        }

        return scored
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.IssueCount ?? 0)
            .ThenBy(candidate => candidate.StartYear ?? int.MaxValue)
            .Take(6)
            .ToList();
    }

    private async Task<JsonNode?> FetchComicVineJsonAsync(string url, CancellationToken ct)
    {
        using var client = _httpFactory.CreateClient(_config.Name);
        return await _rateLimiter.ExecuteAsync(
            Name,
            _config.RateLimit,
            token => client.GetFromJsonAsync<JsonNode>(url, token),
            ct).ConfigureAwait(false);
    }

    private static bool SameComicVineIssue(JsonNode left, JsonNode right)
    {
        var leftId = ExtractFirstString(left, ["id"]);
        var rightId = ExtractFirstString(right, ["id"]);
        if (!string.IsNullOrWhiteSpace(leftId) && !string.IsNullOrWhiteSpace(rightId))
            return string.Equals(leftId, rightId, StringComparison.OrdinalIgnoreCase);

        var leftVolume = ExtractFirstString(left, ["volume.id", "volumeId"]);
        var rightVolume = ExtractFirstString(right, ["volume.id", "volumeId"]);
        var leftIssue = ExtractFirstString(left, ["issue_number", "issueNumber", "number", "issue"]);
        var rightIssue = ExtractFirstString(right, ["issue_number", "issueNumber", "number", "issue"]);
        return !string.IsNullOrWhiteSpace(leftVolume)
            && !string.IsNullOrWhiteSpace(rightVolume)
            && !string.IsNullOrWhiteSpace(leftIssue)
            && !string.IsNullOrWhiteSpace(rightIssue)
            && string.Equals(leftVolume, rightVolume, StringComparison.OrdinalIgnoreCase)
            && AreEquivalentComicOrdinals(leftIssue, rightIssue);
    }

    private static bool ShouldApplyMusicAlbumGuard(SearchStrategyConfig strategy, ProviderLookupRequest request)
        => request.MediaType == MediaType.Music
           && !string.IsNullOrWhiteSpace(GetRequestedAlbum(request))
           && strategy.Name.StartsWith("music", StringComparison.OrdinalIgnoreCase);

    private static JsonNode? TrySelectMusicAlbumScopedResult(JsonArray arr, ProviderLookupRequest request)
    {
        var requestedAlbum = GetRequestedAlbum(request);
        if (string.IsNullOrWhiteSpace(requestedAlbum))
            return null;

        var requestedTitle = CleanTitleForSearch(request.Title) ?? request.Title;
        var requestedArtist = request.Artist ?? request.Author ?? request.Composer;
        var scored = new List<(JsonNode Node, double AlbumScore, double TitleScore, double ArtistScore)>();

        foreach (var node in arr)
        {
            if (node is null)
                continue;

            var candidateAlbum = ExtractFirstString(node, ["collectionName", "album", "release.title"]);
            if (string.IsNullOrWhiteSpace(candidateAlbum) || !IsStrongAlbumMatch(requestedAlbum, candidateAlbum))
                continue;

            var candidateTitle = ExtractFirstString(node, ["trackName", "title", "name"]);
            var titleScore = !string.IsNullOrWhiteSpace(requestedTitle) && !string.IsNullOrWhiteSpace(candidateTitle)
                ? ComputeWordOverlap(requestedTitle, candidateTitle)
                : 0.0;
            if (!string.IsNullOrWhiteSpace(candidateTitle) && titleScore < 0.40)
                continue;

            var candidateArtist = ExtractFirstString(node, ["artistName", "artist", "author"]);
            var artistScore = !string.IsNullOrWhiteSpace(requestedArtist) && !string.IsNullOrWhiteSpace(candidateArtist)
                ? ComputeWordOverlap(requestedArtist, candidateArtist)
                : 0.0;

            scored.Add((node, ComputeWordOverlap(requestedAlbum, candidateAlbum), titleScore, artistScore));
        }

        return scored
            .OrderByDescending(item => item.TitleScore)
            .ThenByDescending(item => item.ArtistScore)
            .ThenByDescending(item => item.AlbumScore)
            .Select(item => item.Node)
            .FirstOrDefault();
    }

    private static string? GetRequestedAlbum(ProviderLookupRequest request)
        => request.Album
           ?? request.Hints?.GetValueOrDefault(MetadataFieldConstants.Album);

    private static bool IsStrongAlbumMatch(string? requestedAlbum, string? candidateAlbum)
    {
        if (string.IsNullOrWhiteSpace(requestedAlbum) || string.IsNullOrWhiteSpace(candidateAlbum))
            return false;

        return RetailTextSimilarity.AreEquivalentNames(requestedAlbum, candidateAlbum)
               || ComputeWordOverlap(requestedAlbum, candidateAlbum) >= 0.92;
    }

    private static string? GetComicIssueHint(ProviderLookupRequest request)
        => request.Hints?.GetValueOrDefault("issue_number")
            ?? request.Hints?.GetValueOrDefault(MetadataFieldConstants.SeriesPosition)
            ?? request.Hints?.GetValueOrDefault("issue");

    private static bool AreEquivalentComicText(string left, string right)
        => string.Equals(NormalizeComicText(left), NormalizeComicText(right), StringComparison.Ordinal);

    private static string NormalizeComicText(string value)
    {
        var chars = StripDiacritics(value)
            .Replace("&", " and ", StringComparison.Ordinal)
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .ToArray();

        return string.Join(' ', new string(chars)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool AreEquivalentComicOrdinals(string left, string right)
    {
        if (int.TryParse(ExtractLeadingDigits(left), out var leftNumber)
            && int.TryParse(ExtractLeadingDigits(right), out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return string.Equals(left.TrimStart('0'), right.TrimStart('0'), StringComparison.OrdinalIgnoreCase)
            || string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractLeadingDigits(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var match = Regex.Match(value.Trim(), @"^\D*0*(\d+)");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static int? TryExtractYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var match = Regex.Match(value, @"(?<!\d)(19|20)\d{2}(?!\d)");
        return match.Success && int.TryParse(match.Value, out var year) ? year : null;
    }

    private static int? TryParseLeadingInt(string? value)
        => int.TryParse(ExtractLeadingDigits(value ?? string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static double ScoreYearProximity(int? requestedYear, int? candidateYear)
    {
        if (!requestedYear.HasValue || !candidateYear.HasValue)
            return 0;

        var delta = Math.Abs(requestedYear.Value - candidateYear.Value);
        return delta switch
        {
            0 => 0.18,
            1 => 0.10,
            <= 3 => 0.04,
            _ => -0.08,
        };
    }

    private static double ScoreVolumeStartYearProximity(int? requestedYear, int? volumeStartYear)
    {
        if (!volumeStartYear.HasValue)
            return 0;

        if (!requestedYear.HasValue)
            return volumeStartYear.Value >= 2000 ? 0.03 : 0;

        var delta = Math.Abs(requestedYear.Value - volumeStartYear.Value);
        return delta switch
        {
            0 => 0.36,
            1 => 0.22,
            <= 3 => 0.08,
            <= 8 => -0.18,
            _ => -0.45,
        };
    }

    private static double ScoreVolumeIssueCount(int? requestedIssueNumber, int? volumeIssueCount)
    {
        if (!volumeIssueCount.HasValue)
            return 0;

        if (requestedIssueNumber.HasValue && volumeIssueCount.Value < requestedIssueNumber.Value)
            return -0.50;

        if (volumeIssueCount.Value <= 1)
            return -0.05;

        return Math.Min(0.10, Math.Log10(volumeIssueCount.Value) * 0.05);
    }

    private static double ScoreLikelyOriginalComicRun(int? volumeIssueCount)
    {
        if (!volumeIssueCount.HasValue || volumeIssueCount.Value <= 0)
            return 0;

        return Math.Min(0.18, Math.Log10(volumeIssueCount.Value) * 0.09);
    }

    private static double ScoreComicPublisherAffinity(ProviderLookupRequest request, string? publisher)
    {
        var requestedPublisher = request.Hints?.GetValueOrDefault(MetadataFieldConstants.PublisherField);
        if (string.IsNullOrWhiteSpace(requestedPublisher) || string.IsNullOrWhiteSpace(publisher))
            return 0;

        return ComputeWordOverlap(requestedPublisher, publisher) >= 0.75 ? 0.12 : -0.03;
    }

    private static bool LooksNonEnglishDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = $" {StripDiacritics(value).ToLowerInvariant()} ";
        var markers = new[]
        {
            " der ", " die ", " das ", " und ", " eine ", " einem ", " einen ",
            " von ", " mit ", " nicht ", " fur ", " uber ", " ist ", " sich "
        };

        return markers.Count(marker => normalized.Contains(marker, StringComparison.Ordinal)) >= 3;
    }

    /// <summary>
    /// Extracts the first non-empty string value from a JSON node by trying multiple paths.
    /// </summary>
    private static string? ExtractFirstString(JsonNode node, string[] paths)
    {
        foreach (var path in paths)
        {
            var val = JsonPathEvaluator.Evaluate(node, path);
            if (val is not null)
            {
                var s = JsonPathEvaluator.GetStringValue(val);
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }
        return null;
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    /// <summary>
    /// Word-overlap similarity (0.0–1.0). Compares normalized word sets,
    /// returning harmonic mean of coverage and precision (F1 score).
    /// </summary>
    private static double ComputeWordOverlap(string query, string candidate)
    {
        var qWords = StripDiacritics(query).ToLowerInvariant()
            .Split([' ', ',', '.', '-', ':', ';', '\'', '"', '(', ')', '[', ']'],
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)
            .ToHashSet();

        var cWords = StripDiacritics(candidate).ToLowerInvariant()
            .Split([' ', ',', '.', '-', ':', ';', '\'', '"', '(', ')', '[', ']'],
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)
            .ToHashSet();

        if (qWords.Count == 0 || cWords.Count == 0) return 0.0;

        var coverage  = (double)qWords.Count(w => cWords.Contains(w)) / qWords.Count;
        var precision = (double)cWords.Count(w => qWords.Contains(w)) / cWords.Count;

        if (coverage + precision == 0) return 0.0;
        return 2 * coverage * precision / (coverage + precision);
    }

    /// <summary>
    /// Strips diacritical marks from text — e.g. "Shogun" ? "Shogun", "Für Elise" ? "Fur Elise".
    /// Uses Unicode decomposition to separate base characters from combining marks.
    /// </summary>
    private static string StripDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // -- Nested release selection --------------------------------------------

    /// <summary>
    /// Selects the best sub-result from a nested array within the matched result node.
    /// Used for MusicBrainz-style APIs where a recording contains multiple releases
    /// and the adapter needs to pick the best one (e.g. original studio album with artwork).
    /// </summary>
    private JsonNode? ApplyReleaseSelection(JsonNode parentNode, ReleaseSelectionConfig config)
    {
        var nested = JsonPathEvaluator.Evaluate(parentNode, config.Path);
        if (nested is not JsonArray arr || arr.Count == 0)
        {
            _logger.LogDebug("{Provider}: release selection — no nested array at '{Path}'", Name, config.Path);
            return null;
        }

        // Apply hard filters.
        var candidates = arr
            .Where(n => n is not null && PassesFilters(n, config.Filters))
            .ToList();

        _logger.LogDebug(
            "{Provider}: release selection — {Total} nested items, {Filtered} pass filters",
            Name, arr.Count, candidates.Count);

        // Fallback: if no candidates match primary filters, try fallback types in order.
        if (candidates.Count == 0 && config.FallbackTypes is { Count: > 0 })
        {
            foreach (var fallbackType in config.FallbackTypes)
            {
                candidates = arr
                    .Where(n => n is not null
                        && MatchesJsonPath(n, "release-group.primary-type", fallbackType)
                        && MatchesJsonPath(n, "status", "Official"))
                    .ToList();

                if (candidates.Count > 0)
                {
                    _logger.LogDebug(
                        "{Provider}: release selection — using fallback type '{Type}' ({Count} candidates)",
                        Name, fallbackType, candidates.Count);
                    break;
                }
            }
        }

        if (candidates.Count == 0)
        {
            _logger.LogDebug("{Provider}: release selection — no candidates after filtering", Name);
            return null;
        }

        // Sort by configured sort fields.
        if (config.Sort is { Count: > 0 })
        {
            candidates.Sort((a, b) =>
            {
                foreach (var sort in config.Sort!)
                {
                    var aVal = JsonPathEvaluator.GetStringValue(
                        JsonPathEvaluator.Evaluate(a!, sort.Path)) ?? "";
                    var bVal = JsonPathEvaluator.GetStringValue(
                        JsonPathEvaluator.Evaluate(b!, sort.Path)) ?? "";
                    var cmp = string.Compare(aVal, bVal, StringComparison.OrdinalIgnoreCase);
                    if (sort.Direction.Equals("desc", StringComparison.OrdinalIgnoreCase))
                        cmp = -cmp;
                    if (cmp != 0) return cmp;
                }
                return 0;
            });
        }

        // Soft preferences: among candidates, prefer those matching prefer conditions.
        if (config.Prefer is { Count: > 0 } && candidates.Count > 1)
        {
            var preferred = candidates.Where(c => PassesFilters(c!, config.Prefer)).ToList();
            if (preferred.Count > 0)
            {
                _logger.LogDebug(
                    "{Provider}: release selection — {Count} candidates match soft preferences",
                    Name, preferred.Count);
                return preferred[0];
            }
        }

        return candidates[0];
    }

    /// <summary>
    /// Returns <c>true</c> when the node passes ALL filters in the list.
    /// An empty or null filter list is a pass.
    /// </summary>
    private static bool PassesFilters(JsonNode node, List<SelectionFilter>? filters)
    {
        if (filters is null || filters.Count == 0) return true;

        foreach (var filter in filters)
        {
            var val = JsonPathEvaluator.Evaluate(node, filter.Path);
            if (val is null) return false;

            if (filter.EqualsValue.HasValue)
            {
                var strVal = JsonPathEvaluator.GetStringValue(val) ?? "";
                var expected = filter.EqualsValue.Value;

                switch (expected.ValueKind)
                {
                    case System.Text.Json.JsonValueKind.True:
                        if (!strVal.Equals("true", StringComparison.OrdinalIgnoreCase))
                            return false;
                        break;
                    case System.Text.Json.JsonValueKind.False:
                        if (!strVal.Equals("false", StringComparison.OrdinalIgnoreCase))
                            return false;
                        break;
                    case System.Text.Json.JsonValueKind.String:
                        if (!strVal.Equals(expected.GetString(), StringComparison.OrdinalIgnoreCase))
                            return false;
                        break;
                    default:
                        if (!strVal.Equals(expected.GetRawText(), StringComparison.OrdinalIgnoreCase))
                            return false;
                        break;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Quick helper: checks if a JSON path on a node equals an expected string value.
    /// </summary>
    private static bool MatchesJsonPath(JsonNode node, string path, string expected)
    {
        var val = JsonPathEvaluator.Evaluate(node, path);
        if (val is null) return false;
        var str = JsonPathEvaluator.GetStringValue(val);
        return string.Equals(str, expected, StringComparison.OrdinalIgnoreCase);
    }

    // -- Claim extraction ----------------------------------------------------

    /// <summary>
    /// Extracts claims using both the top-level result node (recording) and the selected
    /// nested sub-result (release). Each field mapping's <see cref="FieldMappingConfig.Source"/>
    /// determines which node to extract from. Mappings with a <see cref="FieldMappingConfig.Condition"/>
    /// only emit a claim when the condition is met on the source node.
    /// </summary>
    private IReadOnlyList<ProviderClaim> ExtractClaimsWithRelease(
        JsonNode recordingNode, JsonNode? releaseNode, MediaType mediaType = MediaType.Unknown)
    {
        var mappings = FilterMappingsByMediaType(_config.FieldMappings, mediaType);
        if (mappings.Count == 0)
            return [];

        var claims = new List<ProviderClaim>();

        foreach (var mapping in mappings)
        {
            // Route to the correct source node.
            var sourceNode = mapping.Source?.ToLowerInvariant() switch
            {
                "release" => releaseNode,
                "recording" => recordingNode,
                _ => recordingNode, // default: top-level result
            };

            if (sourceNode is null)
                continue;

            // Check condition before extracting (e.g. only emit cover when artwork exists).
            if (mapping.Condition is not null && !PassesFilters(sourceNode, [mapping.Condition]))
                continue;

            var node = JsonPathEvaluator.Evaluate(sourceNode, mapping.JsonPath);
            if (node is null)
                continue;

            var values = ApplyTransform(node, mapping);
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    claims.Add(new ProviderClaim(mapping.ClaimKey, value, mapping.Confidence));
            }
        }

        _logger.LogDebug(
            "{Provider}: extracted {Count} claims from recording+release nodes",
            Name, claims.Count);

        return claims;
    }

    private IReadOnlyList<ProviderClaim> ExtractClaims(JsonNode resultNode, MediaType mediaType = MediaType.Unknown)
    {
        var mappings = FilterMappingsByMediaType(_config.FieldMappings, mediaType);
        if (mappings.Count == 0)
            return [];

        var claims = new List<ProviderClaim>();

        foreach (var mapping in mappings)
        {
            var node = JsonPathEvaluator.Evaluate(resultNode, mapping.JsonPath);
            if (node is null)
                continue;

            var values = ApplyTransform(node, mapping);
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    claims.Add(new ProviderClaim(mapping.ClaimKey, value, mapping.Confidence));
            }
        }

        return claims;
    }

    /// <summary>
    /// Apply the configured transform to the extracted JSON node.
    /// Returns one or more string values (most transforms return exactly one).
    /// </summary>
    private static IReadOnlyList<string> ApplyTransform(JsonNode node, FieldMappingConfig mapping)
    {
        var transformName = mapping.Transform;
        var args = mapping.TransformArgs;

        // Handle transforms that operate on JSON arrays specially.
        if (JsonPathEvaluator.IsArray(node))
            return HandleArrayTransform(node, transformName, args);

        // Scalar value path.
        var raw = JsonPathEvaluator.GetStringValue(node);
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var transformed = !string.IsNullOrEmpty(args)
            ? ValueTransformCatalog.Apply(transformName, raw, args)
            : ValueTransformCatalog.Apply(transformName, raw);

        return string.IsNullOrWhiteSpace(transformed) ? [] : [transformed];
    }

    /// <summary>
    /// Handles transforms that expect a JSON array as input:
    /// <c>prefer_isbn13</c>, <c>array_join</c>, <c>array_nested_join</c>.
    /// </summary>
    private static IReadOnlyList<string> HandleArrayTransform(
        JsonNode node, string? transformName, string? args)
    {
        var values = JsonPathEvaluator.GetArrayValues(node);

        return transformName switch
        {
            // prefer_isbn13 handles both string arrays and object arrays internally.
            "prefer_isbn13" => PreferIsbn13(values, node),
            "array_join" => values.Count > 0 ? [string.Join(args ?? ", ", values)] : [],
            "array_nested_join" => HandleNestedJoin(node, args),
            _ => values.Count > 0 ? [values[0]] : []
        };
    }

    /// <summary>
    /// Prefer a 13-character element (ISBN-13), falling back to first non-empty.
    /// Handles both plain string arrays and object arrays with
    /// <c>type</c>/<c>identifier</c> fields.
    /// </summary>
    private static IReadOnlyList<string> PreferIsbn13(IReadOnlyList<string> values, JsonNode node)
    {
        // Check if this is an array of typed identifier objects.
        if (node is JsonArray arr && arr.Count > 0 && arr[0] is JsonObject)
        {
            string? isbn13 = null;
            string? fallback = null;

            foreach (var element in arr)
            {
                if (element is not JsonObject obj) continue;
                var type = JsonPathEvaluator.GetStringValue(obj["type"]);
                var identifier = JsonPathEvaluator.GetStringValue(obj["identifier"]);
                if (string.IsNullOrWhiteSpace(identifier)) continue;

                if (string.Equals(type, "ISBN_13", StringComparison.OrdinalIgnoreCase))
                    isbn13 = identifier;

                fallback ??= identifier;
            }

            var result = isbn13 ?? fallback;
            return string.IsNullOrWhiteSpace(result) ? [] : [result];
        }

        // Plain string array (Open Library style: ["9780441172719", "0441172717"]).
        var isbn13Plain = values.FirstOrDefault(v => v.Length == 13);
        var resultPlain = isbn13Plain ?? values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        return string.IsNullOrWhiteSpace(resultPlain) ? [] : [resultPlain];
    }

    /// <summary>
    /// From an array of objects, extract a named field from each element and join.
    /// Args = the field name to extract (e.g. <c>"name"</c>).
    /// </summary>
    private static IReadOnlyList<string> HandleNestedJoin(JsonNode node, string? fieldName)
    {
        if (node is not JsonArray arr || string.IsNullOrEmpty(fieldName))
            return [];

        var extracted = new List<string>();
        foreach (var element in arr)
        {
            if (element is null) continue;
            var child = JsonPathEvaluator.Evaluate(element, fieldName);
            var str = JsonPathEvaluator.GetStringValue(child);
            if (!string.IsNullOrWhiteSpace(str))
                extracted.Add(str);
        }

        return extracted.Count > 0 ? [string.Join(", ", extracted)] : [];
    }

    // -- Required field check ------------------------------------------------

    private static readonly HashSet<string> GenericTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "unknown", "untitled", "no title", "title", "book", "audiobook",
        "track", "album", "episode", "movie", "video"
    };

    private static bool AllRequiredFieldsPresent(
        SearchStrategyConfig strategy, ProviderLookupRequest request)
    {
        foreach (var field in strategy.RequiredFields)
        {
            var value = ResolveRequestField(request, field);
            if (string.IsNullOrWhiteSpace(value))
                return false;
            if (value.Length < 3 || GenericTerms.Contains(value.Trim()))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Map a field name string to the corresponding property on the lookup request.
    /// </summary>
    private static string? ResolveRequestField(ProviderLookupRequest request, string fieldName)
    {
        var direct = fieldName.ToLowerInvariant() switch
        {
            "title" => request.Title,
            "author" => request.Author,
            "narrator" => request.Narrator,
            "artist" => request.Artist,
            "album" => request.Album,
            "series" => request.Series,
            BridgeIdKeys.Isbn => request.Isbn,
            BridgeIdKeys.Asin => request.Asin,
            BridgeIdKeys.AppleBooksId => request.AppleBooksId,
            BridgeIdKeys.AudibleId => request.AudibleId,
            BridgeIdKeys.TmdbId => request.TmdbId,
            BridgeIdKeys.ImdbId => request.ImdbId,
            "person_name" => request.PersonName,
            _ => (string?)null
        };

        // In Sequential pipeline mode, check bridge IDs from prior providers
        // when the direct request property is empty.
        if (direct is not null)
            return direct;

        if (request.PriorProviderBridgeIds?.TryGetValue(fieldName.ToLowerInvariant(), out var bridgeValue) == true)
            return bridgeValue;

        // Fall back to the hints dictionary for any remaining fields (year,
        // series_position, etc.) so config-driven URL templates can reference
        // arbitrary claim keys without code changes.
        if (request.Hints?.TryGetValue(fieldName.ToLowerInvariant(), out var hintValue) == true
            && !string.IsNullOrWhiteSpace(hintValue))
            return hintValue;

        return null;
    }

    // -- Media type filtering ------------------------------------------------

    /// <summary>
    /// Filters search strategies by the request's media type.
    /// Strategies with no <c>media_types</c> filter are always included (universal).
    /// Strategies with a <c>media_types</c> list are only included if the request's
    /// media type matches one of the listed values.
    /// </summary>
    private static List<SearchStrategyConfig>? FilterStrategiesByMediaType(
        List<SearchStrategyConfig>? strategies, MediaType mediaType)
    {
        if (strategies is null or { Count: 0 })
            return strategies;

        // Unknown = wildcard — return all strategies.
        if (mediaType == MediaType.Unknown)
            return strategies;

        var mediaTypeStr = mediaType.ToString();
        return strategies
            .Where(s => s.MediaTypes is null or { Count: 0 }
                     || s.MediaTypes.Any(mt =>
                            string.Equals(mt, mediaTypeStr, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>
    /// Filters field mappings by the request's media type.
    /// Mappings with no <c>media_types</c> filter are always included (universal).
    /// Unknown media type = wildcard (return all).
    /// </summary>
    private static List<FieldMappingConfig> FilterMappingsByMediaType(
        List<FieldMappingConfig>? mappings, MediaType mediaType)
    {
        if (mappings is null or { Count: 0 })
            return [];

        // Unknown = wildcard — return all mappings.
        if (mediaType == MediaType.Unknown)
            return mappings;

        var mediaTypeStr = mediaType.ToString();
        return mappings
            .Where(m => m.MediaTypes is null or { Count: 0 }
                     || m.MediaTypes.Any(mt =>
                            string.Equals(mt, mediaTypeStr, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    // -- Helpers --------------------------------------------------------------

    private static HashSet<T> ParseEnumSet<T>(List<string>? values) where T : struct, Enum
    {
        if (values is null or { Count: 0 })
            return [];

        var set = new HashSet<T>();
        foreach (var val in values)
        {
            if (Enum.TryParse<T>(val, ignoreCase: true, out var parsed))
                set.Add(parsed);
        }

        return set;
    }

    // -- Language strategy ---------------------------------------------------

    /// <summary>
    /// Resolves the effective language for API queries based on the provider's language strategy.
    /// <list type="bullet">
    ///   <item><see cref="LanguageStrategy.Source"/>: always English (provider has poor localization).</item>
    ///   <item><see cref="LanguageStrategy.Localized"/>: use the request's metadata language.</item>
    ///   <item><see cref="LanguageStrategy.Both"/>: primary pass uses metadata language, fallback to English
    ///         is handled by the caller after the primary pass returns empty.</item>
    /// </list>
    /// </summary>
    private string ResolveEffectiveLanguage(ProviderLookupRequest request) =>
        _config.LanguageStrategy switch
        {
            LanguageStrategy.Localized => request.Language,
            LanguageStrategy.Both      => request.Language, // Primary pass uses metadata lang
            _                          => "en",             // Source: always English
        };

    /// <summary>
    /// Creates a shallow copy of a <see cref="ProviderLookupRequest"/> with a different language.
    /// Required because <see cref="ProviderLookupRequest"/> is a sealed class (not a record),
    /// so the <c>with</c> expression is unavailable.
    /// </summary>
    private static ProviderLookupRequest CloneRequestWithLanguage(ProviderLookupRequest source, string language) =>
        new()
        {
            EntityId       = source.EntityId,
            EntityType     = source.EntityType,
            MediaType      = source.MediaType,
            Title          = source.Title,
            Author         = source.Author,
            Year           = source.Year,
            Narrator       = source.Narrator,
            Asin           = source.Asin,
            Isbn           = source.Isbn,
            AppleBooksId   = source.AppleBooksId,
            AudibleId      = source.AudibleId,
            TmdbId         = source.TmdbId,
            ImdbId         = source.ImdbId,
            PersonName     = source.PersonName,
            PersonRole     = source.PersonRole,
            PreResolvedQid = source.PreResolvedQid,
            Hints          = source.Hints,
            BaseUrl        = source.BaseUrl,
            SparqlBaseUrl  = source.SparqlBaseUrl,
            Language       = language,
            FileLanguage   = source.FileLanguage,
            Country        = source.Country,
            HydrationPass  = source.HydrationPass,
        };

    private sealed record ComicVineVolumeFacts(
        int? IssueCount,
        int? StartYear,
        string? Publisher);

    private sealed record ComicIssueCandidate(
        JsonNode Node,
        double BaseScore,
        int? CandidateYear,
        string? VolumeId,
        int? VolumeStartYear,
        int? VolumeIssueCount,
        string? Publisher)
    {
        public double Score => BaseScore;
    }

    private sealed record ComicVineVolumeSearchCandidate(
        string VolumeId,
        double Score,
        int? IssueCount,
        int? StartYear,
        string? Publisher);

    private sealed record TmdbCollectionPart(
        string Id,
        string Title,
        DateOnly? ReleaseDate);
}
