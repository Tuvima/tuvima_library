using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Orchestrates user-triggered metadata search across Wikidata (Universe mode)
/// and retail providers (Retail mode).
///
/// Universe search: calls WikidataAdapter.ResolveCandidatesAsync to get QID candidates,
/// then concurrently calls retail providers to fetch cover art for the top candidates.
///
/// Retail search: calls the relevant retail providers (filtered by media type) via
/// SearchAsync to return title/cover matches without requiring a Wikidata QID.
/// </summary>
public sealed class SearchService : ISearchService
{
    private readonly WikidataAdapter _wikidataAdapter;
    private readonly IEnumerable<IExternalMetadataProvider> _providers;
    private readonly IConfigurationLoader _configLoader;
    private readonly ILogger<SearchService> _logger;

    // Providers that should not be used for retail search
    private static readonly HashSet<string> ExcludedFromRetail = new(StringComparer.OrdinalIgnoreCase)
    {
        "wikidata", "wikipedia"
    };

    public SearchService(
        WikidataAdapter wikidataAdapter,
        IEnumerable<IExternalMetadataProvider> providers,
        IConfigurationLoader configLoader,
        ILogger<SearchService> logger)
    {
        ArgumentNullException.ThrowIfNull(wikidataAdapter);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(configLoader);
        ArgumentNullException.ThrowIfNull(logger);
        _wikidataAdapter = wikidataAdapter;
        _providers       = providers;
        _configLoader    = configLoader;
        _logger          = logger;
    }

    /// <inheritdoc/>
    public async Task<SearchUniverseResult> SearchUniverseAsync(
        SearchUniverseRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return new SearchUniverseResult([], request.Query, request.MediaType);

        // Build endpoint map from all provider configs (same pattern as HydrationPipelineService)
        var provConfigs = _configLoader.LoadAllProviders();
        var endpointMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pc in provConfigs)
            foreach (var (key, url) in pc.Endpoints)
                endpointMap.TryAdd(key, url);

        var sparqlBaseUrl = endpointMap.TryGetValue("wikidata_sparql", out var sparql) ? sparql : null;
        var baseUrl       = endpointMap.TryGetValue("wikidata_api",    out var api)    ? api    : "https://www.wikidata.org/w/api.php";

        // Resolve MediaType from string
        var mediaType = ParseMediaType(request.MediaType);

        // Build the lookup request for Wikidata
        var lookupRequest = new ProviderLookupRequest
        {
            EntityId      = Guid.NewGuid(),
            EntityType    = EntityType.Work,
            MediaType     = mediaType,
            Title         = request.Query,
            BaseUrl       = baseUrl,
            SparqlBaseUrl = sparqlBaseUrl,
            Language      = "en",
            Country       = "us",
        };

        // Step 1: Get QID candidates from Wikidata
        IReadOnlyList<QidCandidate> qidCandidates;
        try
        {
            qidCandidates = await _wikidataAdapter.ResolveCandidatesAsync(
                lookupRequest, request.MaxCandidates, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Wikidata candidate search failed for query '{Query}'", request.Query);
            qidCandidates = [];
        }

        if (qidCandidates.Count == 0)
            return new SearchUniverseResult([], request.Query, request.MediaType);

        // Step 2: Enrich top candidates with cover art from retail providers (max 3 to limit API load)
        var topCandidates = qidCandidates.Take(Math.Min(3, request.MaxCandidates)).ToList();
        var retailProviders = GetRetailProviders(mediaType);

        var enrichmentTasks = topCandidates.Select(c =>
            EnrichCandidateAsync(c, request.Query, mediaType, retailProviders, endpointMap, ct));

        var enriched = await Task.WhenAll(enrichmentTasks).ConfigureAwait(false);

        // Add remaining candidates (without enrichment) for completeness
        var allCandidates = enriched.ToList();
        foreach (var remaining in qidCandidates.Skip(topCandidates.Count))
        {
            allCandidates.Add(new UniverseCandidate
            {
                Qid            = remaining.Qid,
                Label          = remaining.Label,
                Description    = remaining.Description,
                ResolutionTier = remaining.ResolutionTier,
                Confidence     = EstimateConfidence(remaining.ResolutionTier),
                BridgeIds      = new Dictionary<string, string>(),
            });
        }

        return new SearchUniverseResult(allCandidates, request.Query, request.MediaType);
    }

    /// <inheritdoc/>
    public async Task<SearchRetailResult> SearchRetailAsync(
        SearchRetailRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return new SearchRetailResult([], request.Query, request.MediaType);

        var mediaType      = ParseMediaType(request.MediaType);
        var retailProviders = GetRetailProviders(mediaType);

        if (retailProviders.Count == 0)
            return new SearchRetailResult([], request.Query, request.MediaType);

        // Build endpoint map for URLs/country/language
        var provConfigs = _configLoader.LoadAllProviders();
        var endpointMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pc in provConfigs)
            foreach (var (key, url) in pc.Endpoints)
                endpointMap.TryAdd(key, url);

        var candidates = new List<RetailCandidate>();

        // Call each retail provider in parallel
        var tasks = retailProviders.Select(p =>
            SearchProviderAsync(p, request.Query, mediaType, endpointMap, request.MaxCandidates, ct));

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        foreach (var providerResults in results)
            candidates.AddRange(providerResults);

        return new SearchRetailResult(candidates, request.Query, request.MediaType);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<UniverseCandidate> EnrichCandidateAsync(
        QidCandidate candidate,
        string originalQuery,
        MediaType mediaType,
        IReadOnlyList<IExternalMetadataProvider> retailProviders,
        Dictionary<string, string> endpointMap,
        CancellationToken ct)
    {
        string? coverUrl = null;

        // Try to get cover art from the first retail provider that returns a result
        if (retailProviders.Count > 0)
        {
            try
            {
                var retailRequest = new ProviderLookupRequest
                {
                    EntityId   = Guid.NewGuid(),
                    EntityType = EntityType.Work,
                    MediaType  = mediaType,
                    Title      = candidate.Label, // Use the Wikidata label for precision
                    BaseUrl    = GetProviderBaseUrl(retailProviders[0].Name, endpointMap),
                    Language   = "en",
                    Country    = "us",
                };

                var results = await retailProviders[0].SearchAsync(retailRequest, 1, ct).ConfigureAwait(false);
                if (results.Count > 0)
                    coverUrl = results[0].ThumbnailUrl;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Retail cover art enrichment failed for candidate {Qid}", candidate.Qid);
            }
        }

        // Parse year and author from the Wikidata description heuristically
        // (e.g. "1965 novel by Frank Herbert" → year="1965", author="Frank Herbert")
        string? year   = ExtractYearFromDescription(candidate.Description);
        string? author = ExtractAuthorFromDescription(candidate.Description);

        return new UniverseCandidate
        {
            Qid            = candidate.Qid,
            Label          = candidate.Label,
            Description    = candidate.Description,
            Year           = year,
            Author         = author,
            CoverUrl       = coverUrl,
            ResolutionTier = candidate.ResolutionTier,
            Confidence     = EstimateConfidence(candidate.ResolutionTier),
            BridgeIds      = new Dictionary<string, string>(),
        };
    }

    private async Task<IEnumerable<RetailCandidate>> SearchProviderAsync(
        IExternalMetadataProvider provider,
        string query,
        MediaType mediaType,
        Dictionary<string, string> endpointMap,
        int maxCandidates,
        CancellationToken ct)
    {
        try
        {
            var providerRequest = new ProviderLookupRequest
            {
                EntityId   = Guid.NewGuid(),
                EntityType = EntityType.Work,
                MediaType  = mediaType,
                Title      = query,
                BaseUrl    = GetProviderBaseUrl(provider.Name, endpointMap),
                Language   = "en",
                Country    = "us",
            };

            var results = await provider.SearchAsync(providerRequest, maxCandidates, ct).ConfigureAwait(false);

            return results.Select(r => new RetailCandidate
            {
                ProviderId     = provider.ProviderId.ToString(),
                ProviderName   = provider.Name,
                ProviderItemId = r.ProviderItemId,
                Title          = r.Title,
                Year           = r.Year,
                Author         = r.Author,
                Description    = r.Description,
                CoverUrl       = r.ThumbnailUrl,
                Confidence     = r.Confidence,
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Retail search failed for provider '{Provider}', query '{Query}'",
                provider.Name, query);
            return [];
        }
    }

    private IReadOnlyList<IExternalMetadataProvider> GetRetailProviders(MediaType mediaType)
    {
        return _providers
            .Where(p => !ExcludedFromRetail.Contains(p.Name)
                     && p.CanHandle(mediaType)
                     && p.CanHandle(EntityType.Work))
            .ToList();
    }

    private static string GetProviderBaseUrl(
        string providerName,
        Dictionary<string, string> endpointMap)
    {
        // Try provider-specific URL key first, then fall back to empty
        if (endpointMap.TryGetValue(providerName, out var url)) return url;
        if (endpointMap.TryGetValue($"{providerName}_api", out var apiUrl)) return apiUrl;
        // Apple Books uses the iTunes Search API
        if (providerName.Contains("apple", StringComparison.OrdinalIgnoreCase))
            return endpointMap.TryGetValue("apple_books", out var ab) ? ab : "https://itunes.apple.com";
        return string.Empty;
    }

    private static MediaType ParseMediaType(string? mediaTypeStr) =>
        Enum.TryParse<MediaType>(mediaTypeStr, ignoreCase: true, out var mt) ? mt : MediaType.Unknown;

    private static double EstimateConfidence(string? tier) => tier switch
    {
        "bridge"            => 0.95,
        "structured_sparql" => 0.75,
        "title_search"      => 0.55,
        _                   => 0.50,
    };

    /// <summary>Heuristic: extract a 4-digit year from a Wikidata description like "1965 novel by...".</summary>
    private static string? ExtractYearFromDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(description, @"\b(1[5-9]\d{2}|20[0-2]\d)\b");
        return match.Success ? match.Value : null;
    }

    /// <summary>Heuristic: extract author from "... by Author Name" pattern.</summary>
    private static string? ExtractAuthorFromDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(
            description, @"\bby\s+([A-Z][a-zA-Z\s\-\.]{2,40}?)(?:\s*[,;(]|$)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}
