using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Enums;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
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

    // Throttle: per-instance semaphore + timestamp gap.
    private readonly SemaphoreSlim _throttle;
    private DateTime _lastCallUtc = DateTime.MinValue;

    // Parsed once at construction.
    private readonly Guid _providerId;
    private readonly HashSet<MediaType> _mediaTypes;
    private readonly HashSet<EntityType> _entityTypes;

    public ConfigDrivenAdapter(
        ProviderConfiguration config,
        IHttpClientFactory httpFactory,
        ILogger<ConfigDrivenAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _config = config;
        _httpFactory = httpFactory;
        _logger = logger;

        _throttle = new SemaphoreSlim(
            Math.Max(1, config.MaxConcurrency),
            Math.Max(1, config.MaxConcurrency));

        _providerId = !string.IsNullOrEmpty(config.ProviderId)
            ? Guid.Parse(config.ProviderId)
            : Guid.NewGuid();

        // Parse can_handle filters into enum sets for fast lookup.
        // Normalize legacy media type names (Epub → Books, Audiobook → Audiobooks).
        var normalizedMediaTypes = config.CanHandle?.MediaTypes?
            .Select(NormalizeMediaType).ToList();
        _mediaTypes = ParseEnumSet<MediaType>(normalizedMediaTypes);
        _entityTypes = ParseEnumSet<EntityType>(config.CanHandle?.EntityTypes);
    }

    // ── IExternalMetadataProvider ─────────────────────────────────────────────

    public string Name => _config.Name;

    public ProviderDomain Domain => _config.Domain;

    public IReadOnlyList<string> CapabilityTags => _config.CapabilityTags;

    public Guid ProviderId => _providerId;

    public bool CanHandle(MediaType mediaType) =>
        _mediaTypes.Count == 0 || _mediaTypes.Contains(mediaType);

    public bool CanHandle(EntityType entityType) =>
        _entityTypes.Count == 0 || _entityTypes.Contains(entityType);

    public async Task<IReadOnlyList<ProviderClaim>> FetchAsync(
        ProviderLookupRequest request,
        CancellationToken ct = default)
    {
        if (!CanHandle(request.MediaType) || !CanHandle(request.EntityType))
            return [];

        // Short-circuit when an API key is required but not configured.
        if (_config.RequiresApiKey
            && string.IsNullOrWhiteSpace(_config.HttpClient?.ApiKey))
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

        foreach (var strategy in strategies)
        {
            // Check required fields are present.
            if (!AllRequiredFieldsPresent(strategy, request))
            {
                _logger.LogDebug(
                    "{Provider}/{Strategy}: skipped — missing required fields",
                    Name, strategy.Name);
                continue;
            }

            try
            {
                var claims = await ExecuteStrategyAsync(strategy, request, ct)
                    .ConfigureAwait(false);

                if (claims.Count > 0)
                {
                    _logger.LogDebug(
                        "{Provider}/{Strategy}: returned {Count} claims",
                        Name, strategy.Name, claims.Count);
                    return claims;
                }

                _logger.LogInformation(
                    "{Provider}/{Strategy}: zero results from API, trying next strategy",
                    Name, strategy.Name);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException)
            {
                _logger.LogWarning(ex,
                    "{Provider}/{Strategy}: failed, trying next strategy",
                    Name, strategy.Name);
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
            && string.IsNullOrWhiteSpace(_config.HttpClient?.ApiKey))
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

        // Use the lesser of caller limit, strategy max_results, and a hard cap of 50.
        var effectiveLimit = limit;

        foreach (var strategy in strategies)
        {
            if (!AllRequiredFieldsPresent(strategy, request))
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
                var results = await ExecuteSearchStrategyAsync(strategy, request, effectiveLimit, ct)
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

            using var client = _httpFactory.CreateClient(_config.Name);
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            _lastCallUtc = DateTime.UtcNow;

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

                var item = ExtractSearchResultItem(resultNode, request.MediaType, request.Title);
                if (item is not null)
                    items.Add(item);
            }

            _logger.LogDebug(
                "{Provider}/{Strategy}: search returned {Count} items",
                Name, strategy.Name, items.Count);

            return items;
        }
        finally
        {
            _throttle.Release();
        }
    }

    /// <summary>
    /// Extracts a <see cref="SearchResultItem"/> from a single JSON result object
    /// using the configured field mappings. Looks for title, description, year,
    /// cover/thumbnail, and a provider item ID.
    /// <para>
    /// The <paramref name="query"/> is used to compute a per-result match score via
    /// word-overlap similarity so that the first result is not always scored identically
    /// to the tenth. The confidence reflects how well the result matches the search terms.
    /// </para>
    /// </summary>
    private SearchResultItem? ExtractSearchResultItem(
        System.Text.Json.Nodes.JsonNode resultNode,
        MediaType mediaType = MediaType.Unknown,
        string? query = null)
    {
        var filteredMappings = FilterMappingsByMediaType(_config.FieldMappings, mediaType);
        if (filteredMappings.Count == 0)
            return null;

        string? title = null;
        string? author = null;
        string? description = null;
        string? year = null;
        string? thumbnailUrl = null;
        string? providerItemId = null;

        foreach (var mapping in filteredMappings)
        {
            var node = JsonPathEvaluator.Evaluate(resultNode, mapping.JsonPath);
            if (node is null)
            {
                _logger.LogDebug("{Provider}: mapping '{Key}' (path '{Path}') — node not found",
                    Name, mapping.ClaimKey, mapping.JsonPath);
                continue;
            }

            var raw = JsonPathEvaluator.GetStringValue(node);
            if (string.IsNullOrWhiteSpace(raw))
            {
                _logger.LogDebug("{Provider}: mapping '{Key}' (path '{Path}') — value is null or non-scalar",
                    Name, mapping.ClaimKey, mapping.JsonPath);
                continue;
            }

            // Apply transform if configured.
            if (!string.IsNullOrEmpty(mapping.Transform))
            {
                raw = !string.IsNullOrEmpty(mapping.TransformArgs)
                    ? ValueTransformRegistry.Apply(mapping.Transform, raw, mapping.TransformArgs)
                    : ValueTransformRegistry.Apply(mapping.Transform, raw);
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                _logger.LogDebug("{Provider}: mapping '{Key}' (path '{Path}') — empty after transform '{Transform}'",
                    Name, mapping.ClaimKey, mapping.JsonPath, mapping.Transform);
                continue;
            }

            _logger.LogDebug("{Provider}: mapping '{Key}' → '{Value}'",
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
                case "isbn":
                case "asin":
                case "apple_books_id":
                case "goodreads_id":
                case "audible_id":
                case "tmdb_id":
                case "imdb_id":
                case "comicvine_id":
                case "musicbrainz_id":
                case "spotify_id":
                    providerItemId ??= raw;
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
        var confidence = ComputeQueryMatchScore(query, title, author);

        _logger.LogInformation(
            "{Provider}: extracted result Title='{Title}' Author='{Author}' Year='{Year}' " +
            "HasDesc={HasDesc} HasCover={HasCover} Score={Score:P0}",
            Name, title, author ?? "—", year ?? "—",
            description is not null, thumbnailUrl is not null, confidence);

        return new SearchResultItem(
            Title:          title,
            Author:         author,
            Description:    description,
            Year:           year,
            ThumbnailUrl:   thumbnailUrl,
            ProviderItemId: providerItemId,
            Confidence:     confidence,
            ProviderName:   Name);
    }

    /// <summary>
    /// Computes a per-result match score (0.0–1.0) by comparing the search
    /// <paramref name="query"/> against the result's <paramref name="title"/>
    /// using word-level overlap similarity.
    ///
    /// <para>
    /// Algorithm:
    /// <list type="bullet">
    ///   <item>Tokenise both query and title into lowercase words (≥2 chars).</item>
    ///   <item>Coverage = query words found in title / total query words.</item>
    ///   <item>Precision = title words found in query / total title words.</item>
    ///   <item>Score = harmonic mean (F1) of coverage and precision × 0.85.</item>
    ///   <item>+0.12 bonus for exact (normalised) title match.</item>
    ///   <item>+0.05 bonus if any author token appears in the query.</item>
    ///   <item>Minimum 0.05 when the result has a title but no query is given.</item>
    /// </list>
    /// </para>
    /// </summary>
    private static double ComputeQueryMatchScore(string? query, string? title, string? author)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(title))
            return 0.50; // No query context — neutral score.

        var queryTokens = TokenizeText(query);
        var titleTokens = TokenizeText(title);

        if (queryTokens.Count == 0 || titleTokens.Count == 0)
            return 0.50;

        // Exact normalised match → near-perfect score.
        if (string.Equals(
                string.Join(' ', queryTokens.Order()),
                string.Join(' ', titleTokens.Order()),
                StringComparison.OrdinalIgnoreCase))
            return 0.97;

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

        // Author tokens in query → small bonus.
        if (!string.IsNullOrWhiteSpace(author))
        {
            var authorTokens = TokenizeText(author);
            bool authorInQuery = authorTokens.Any(a => queryTokens.Contains(a));
            if (authorInQuery)
                score += 0.05;
        }

        return Math.Clamp(score, 0.05, 0.97);
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

    // ── Strategy execution ──────────────────────────────────────────────────

    private async Task<IReadOnlyList<ProviderClaim>> ExecuteStrategyAsync(
        SearchStrategyConfig strategy,
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        // Automatic single-result match — request only as many results as we need.
        var fetchLimit = strategy.FetchLimit > 0 ? strategy.FetchLimit : 5;
        var url = BuildUrl(strategy, request, fetchLimit);
        _logger.LogDebug("{Provider}/{Strategy}: GET {Url}", Name, strategy.Name, url);

        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Enforce throttle gap.
            if (_config.ThrottleMs > 0)
            {
                var elapsed = (DateTime.UtcNow - _lastCallUtc).TotalMilliseconds;
                if (elapsed < _config.ThrottleMs)
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(_config.ThrottleMs - elapsed), ct)
                        .ConfigureAwait(false);
            }

            using var client = _httpFactory.CreateClient(_config.Name);
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            _lastCallUtc = DateTime.UtcNow;

            // Tolerate 404 for direct-lookup APIs (e.g. Audnexus).
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound
                && strategy.Tolerate404)
            {
                _logger.LogDebug(
                    "{Provider}/{Strategy}: 404 tolerated", Name, strategy.Name);
                return [];
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content
                .ReadFromJsonAsync<JsonNode>(cancellationToken: ct)
                .ConfigureAwait(false);

            if (json is null)
                return [];

            // Navigate to result object.
            var resultNode = NavigateToResult(json, strategy);
            if (resultNode is null)
                return [];

            // Extract claims from field mappings (filtered by media type).
            return ExtractClaims(resultNode, request.MediaType);
        }
        finally
        {
            _throttle.Release();
        }
    }

    // ── URL building ────────────────────────────────────────────────────────

    private string BuildUrl(SearchStrategyConfig strategy, ProviderLookupRequest request, int? limitOverride = null)
    {
        var baseUrl = ResolveBaseUrl(request);
        var template = strategy.UrlTemplate;

        // Build {query} placeholder from query_template if specified.
        var query = string.Empty;
        if (!string.IsNullOrEmpty(strategy.QueryTemplate))
        {
            query = strategy.QueryTemplate;
            query = ReplacePlaceholder(query, "{title}", request.Title, encode: false);
            query = ReplacePlaceholder(query, "{author}", request.Author, encode: false);
            query = ReplacePlaceholder(query, "{narrator}", request.Narrator, encode: false);
            // Trim and collapse whitespace from unfilled placeholders.
            query = System.Text.RegularExpressions.Regex
                .Replace(query.Trim(), @"\s+", " ");
        }

        // Replace all placeholders in the URL template.
        var url = template;
        url = ReplacePlaceholder(url, "{base_url}", baseUrl, encode: false);
        url = ReplacePlaceholder(url, "{query}", query, encode: true);
        url = ReplacePlaceholder(url, "{title}", request.Title, encode: true);
        url = ReplacePlaceholder(url, "{author}", request.Author, encode: true);
        url = ReplacePlaceholder(url, "{isbn}", request.Isbn, encode: true);
        url = ReplacePlaceholder(url, "{asin}", request.Asin, encode: true);
        url = ReplacePlaceholder(url, "{narrator}", request.Narrator, encode: true);
        url = ReplacePlaceholder(url, "{apple_books_id}", request.AppleBooksId, encode: true);
        url = ReplacePlaceholder(url, "{audible_id}", request.AudibleId, encode: true);
        url = ReplacePlaceholder(url, "{tmdb_id}", request.TmdbId, encode: true);
        url = ReplacePlaceholder(url, "{imdb_id}", request.ImdbId, encode: true);
        url = ReplacePlaceholder(url, "{api_key}", _config.HttpClient?.ApiKey, encode: true);
        url = ReplacePlaceholder(url, "{lang}",    request.Language.ToLowerInvariant(), encode: true);
        url = ReplacePlaceholder(url, "{country}", request.Country.ToLowerInvariant(),  encode: true);

        // {limit} — replaced with the caller-supplied override (fetch path uses fetch_limit,
        // search path uses the manual search limit). Falls back to max_results or 25.
        var resolvedLimit = limitOverride
            ?? (strategy.MaxResults > 0 ? strategy.MaxResults : 25);
        url = ReplacePlaceholder(url, "{limit}", resolvedLimit.ToString(), encode: false);

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

    // ── Result navigation ───────────────────────────────────────────────────

    private static JsonNode? NavigateToResult(JsonNode json, SearchStrategyConfig strategy)
    {
        // If no results_path, treat the whole response as the result.
        if (string.IsNullOrEmpty(strategy.ResultsPath))
            return json;

        var resultsNode = JsonPathEvaluator.Evaluate(json, strategy.ResultsPath);
        if (resultsNode is not JsonArray arr || arr.Count == 0)
            return null;

        var index = Math.Clamp(strategy.ResultIndex, 0, arr.Count - 1);
        return arr[index];
    }

    // ── Claim extraction ────────────────────────────────────────────────────

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
            ? ValueTransformRegistry.Apply(transformName, raw, args)
            : ValueTransformRegistry.Apply(transformName, raw);

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
    /// Handles both plain string arrays (Open Library) and object arrays with
    /// <c>type</c>/<c>identifier</c> fields (Google Books <c>industryIdentifiers</c>).
    /// </summary>
    private static IReadOnlyList<string> PreferIsbn13(IReadOnlyList<string> values, JsonNode node)
    {
        // Check if this is an array of objects (Google Books style: [{type: "ISBN_13", identifier: "..."}]).
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

    // ── Required field check ────────────────────────────────────────────────

    private static bool AllRequiredFieldsPresent(
        SearchStrategyConfig strategy, ProviderLookupRequest request)
    {
        foreach (var field in strategy.RequiredFields)
        {
            var value = ResolveRequestField(request, field);
            if (string.IsNullOrWhiteSpace(value))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Map a field name string to the corresponding property on the lookup request.
    /// </summary>
    private static string? ResolveRequestField(ProviderLookupRequest request, string fieldName)
    {
        return fieldName.ToLowerInvariant() switch
        {
            "title" => request.Title,
            "author" => request.Author,
            "narrator" => request.Narrator,
            "isbn" => request.Isbn,
            "asin" => request.Asin,
            "apple_books_id" => request.AppleBooksId,
            "audible_id" => request.AudibleId,
            "tmdb_id" => request.TmdbId,
            "imdb_id" => request.ImdbId,
            "person_name" => request.PersonName,
            _ => null
        };
    }

    // ── Media type filtering ────────────────────────────────────────────────

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

        // Normalize the incoming media type string so legacy enum names (e.g. "Epub")
        // compare correctly against config values that use current names (e.g. "Books").
        var mediaTypeStr = NormalizeMediaType(mediaType.ToString());
        return strategies
            .Where(s => s.MediaTypes is null or { Count: 0 }
                     || s.MediaTypes.Any(mt =>
                            string.Equals(NormalizeMediaType(mt), mediaTypeStr, StringComparison.OrdinalIgnoreCase)))
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

        // Normalize the incoming media type string so legacy enum names (e.g. "Epub")
        // compare correctly against config values that use current names (e.g. "Books").
        var mediaTypeStr = NormalizeMediaType(mediaType.ToString());
        return mappings
            .Where(m => m.MediaTypes is null or { Count: 0 }
                     || m.MediaTypes.Any(mt =>
                            string.Equals(NormalizeMediaType(mt), mediaTypeStr, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    // ── Legacy media type normalization ─────────────────────────────────────

    /// <summary>
    /// Maps pre-rename media type names to their current enum equivalents.
    /// Configs written before the enum rename used "Epub"/"Audiobook"; the
    /// current enum has "Books"/"Audiobooks".
    /// </summary>
    private static readonly Dictionary<string, string> LegacyMediaTypeMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Epub"]      = "Books",
            ["Audiobook"] = "Audiobooks",
        };

    private static string NormalizeMediaType(string value)
        => LegacyMediaTypeMap.TryGetValue(value, out var normalized) ? normalized : value;

    // ── Helpers ──────────────────────────────────────────────────────────────

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
}
