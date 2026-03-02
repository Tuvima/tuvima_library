using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Tanaste.Domain.Enums;
using Tanaste.Providers.Contracts;
using Tanaste.Providers.Models;
using Tanaste.Storage.Models;

namespace Tanaste.Providers.Adapters;

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
        _mediaTypes = ParseEnumSet<MediaType>(config.CanHandle?.MediaTypes);
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

        var strategies = _config.SearchStrategies?
            .OrderBy(s => s.Priority)
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

                _logger.LogDebug(
                    "{Provider}/{Strategy}: no claims produced, trying next",
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

    // ── Strategy execution ──────────────────────────────────────────────────

    private async Task<IReadOnlyList<ProviderClaim>> ExecuteStrategyAsync(
        SearchStrategyConfig strategy,
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        var url = BuildUrl(strategy, request);
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

            // Extract claims from field mappings.
            return ExtractClaims(resultNode);
        }
        finally
        {
            _throttle.Release();
        }
    }

    // ── URL building ────────────────────────────────────────────────────────

    private string BuildUrl(SearchStrategyConfig strategy, ProviderLookupRequest request)
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

    private IReadOnlyList<ProviderClaim> ExtractClaims(JsonNode resultNode)
    {
        if (_config.FieldMappings is null or { Count: 0 })
            return [];

        var claims = new List<ProviderClaim>();

        foreach (var mapping in _config.FieldMappings)
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
