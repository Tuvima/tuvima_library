using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Orchestrates user-triggered metadata search across Wikidata (Universe mode)
/// and retail providers (Retail mode).
///
/// Universe search: calls retail providers to fetch cover art for QID candidates.
///
/// TODO: Phase 3 - WikidataAdapter.ResolveCandidatesAsync will be replaced with
/// ReconciliationAdapter when dotNetRDF SPARQL infrastructure is in place.
///
/// Retail search: calls the relevant retail providers (filtered by media type) via
/// SearchAsync to return title/cover matches without requiring a Wikidata QID.
/// </summary>
public sealed class SearchService : ISearchService
{
    // TODO: Phase 3 - Replace with IReconciliationAdapter when available
    // private readonly IReconciliationAdapter _reconciliationAdapter;
    private readonly IReadOnlyList<IExternalMetadataProvider> _providers;
    private readonly IConfigurationLoader _configLoader;
    private readonly ILogger<SearchService> _logger;

    // Providers that should not be used for retail search
    private static readonly HashSet<string> ExcludedFromRetail = new(StringComparer.OrdinalIgnoreCase)
    {
        "wikidata", "wikipedia"
    };

    /// <summary>
    /// Media-type-to-description-keyword mapping for post-filtering Wikidata candidates.
    /// Candidates whose Description does not contain any of these keywords are removed.
    /// </summary>
    private static readonly Dictionary<string, string[]> MediaTypeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Epub"]      = ["novel", "book", "literary work", "written work", "light novel", "short story collection", "novella", "poetry collection", "anthology"],
        ["Audiobook"]  = ["novel", "book", "literary work", "written work", "audiobook", "light novel", "novella", "poetry collection", "anthology"],
        ["Books"]      = ["novel", "book", "literary work", "written work", "light novel", "short story collection", "novella", "poetry collection", "anthology"],
        ["Movies"]     = ["film", "movie", "animated film", "short film", "documentary film"],
        ["TV"]         = ["television series", "web series", "animated series", "miniseries", "television film"],
        ["Music"]      = ["album", "single", "musical work", "song", "extended play"],
        ["Comics"]     = ["comic book", "manga", "graphic novel", "comic book series", "manhwa"],
        ["Podcasts"]   = ["podcast", "podcast series", "podcast episode"],
    };

    public SearchService(
        IEnumerable<IExternalMetadataProvider> providers,
        IConfigurationLoader configLoader,
        ILogger<SearchService> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(configLoader);
        ArgumentNullException.ThrowIfNull(logger);

        var providerList = providers.ToList();

        // TODO: Phase 3 - WikidataAdapter resolved here for ResolveCandidatesAsync;
        // will be replaced with ReconciliationAdapter injection

        _providers    = providerList;
        _configLoader = configLoader;
        _logger       = logger;
    }

    /// <inheritdoc/>
    public async Task<SearchUniverseResult> SearchUniverseAsync(
        SearchUniverseRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return new SearchUniverseResult([], request.Query, request.MediaType);

        var mediaType = ParseMediaType(request.MediaType);

        // Find the Wikidata Reconciliation provider
        var wikidataProvider = _providers.FirstOrDefault(
            p => p.Name.Contains("wikidata", StringComparison.OrdinalIgnoreCase)
              || p.Name.Contains("reconciliation", StringComparison.OrdinalIgnoreCase));

        if (wikidataProvider is null)
        {
            _logger.LogWarning("No Wikidata/Reconciliation provider registered. Universe search unavailable.");
            return new SearchUniverseResult([], request.Query, request.MediaType);
        }

        // Build endpoint map for cover art enrichment
        var provConfigs = _configLoader.LoadAllProviders();
        var endpointMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pc in provConfigs)
            foreach (var (key, url) in pc.Endpoints)
                endpointMap.TryAdd(key, url);

        try
        {
            // Search Wikidata via Reconciliation API
            var providerRequest = new ProviderLookupRequest
            {
                EntityId   = Guid.NewGuid(),
                EntityType = EntityType.Work,
                MediaType  = mediaType,
                Title      = request.Query,
                BaseUrl    = GetProviderBaseUrl(wikidataProvider.Name, endpointMap),
                Language   = "en",
                Country    = "us",
            };

            var searchResults = await wikidataProvider.SearchAsync(
                providerRequest, request.MaxCandidates, ct).ConfigureAwait(false);

            if (searchResults.Count == 0)
                return new SearchUniverseResult([], request.Query, request.MediaType);

            // Collect retail providers for cover art enrichment
            var retailProviders = GetRetailProviders(mediaType);
            var candidates = new List<UniverseCandidate>();

            foreach (var result in searchResults)
            {
                // Skip items without a valid QID
                var qid = result.ProviderItemId ?? "";
                if (string.IsNullOrEmpty(qid) || !qid.StartsWith('Q'))
                    continue;

                var qidCandidate = new QidCandidate
                {
                    Qid            = qid,
                    Label          = result.Title,
                    Description    = result.Description,
                    ResolutionTier = "title_search",
                };

                // Enrich with cover art from retail providers
                var enriched = await EnrichCandidateAsync(
                    qidCandidate, request.Query, mediaType, retailProviders, endpointMap, ct)
                    .ConfigureAwait(false);

                candidates.Add(enriched);
            }

            return new SearchUniverseResult(candidates, request.Query, request.MediaType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Universe search failed for query '{Query}'", request.Query);
            return new SearchUniverseResult([], request.Query, request.MediaType);
        }
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
        string? coverUrl     = null;
        string? retailAuthor = null;

        // Try to get cover art from retail providers — waterfall through all, then retry with original query
        if (retailProviders.Count > 0)
        {
            // First pass: try each provider with the Wikidata label (precise)
            foreach (var provider in retailProviders)
            {
                if (coverUrl is not null) break;
                try
                {
                    var retailRequest = new ProviderLookupRequest
                    {
                        EntityId   = Guid.NewGuid(),
                        EntityType = EntityType.Work,
                        MediaType  = mediaType,
                        Title      = candidate.Label,
                        BaseUrl    = GetProviderBaseUrl(provider.Name, endpointMap),
                        Language   = "en",
                        Country    = "us",
                    };

                    var results = await provider.SearchAsync(retailRequest, 1, ct).ConfigureAwait(false);
                    if (results.Count > 0 && !string.IsNullOrEmpty(results[0].ThumbnailUrl))
                    {
                        coverUrl = results[0].ThumbnailUrl;
                        // Also capture author from retail provider (pen name friendly)
                        retailAuthor ??= results[0].Author;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "Retail cover art enrichment failed for candidate {Qid} from provider {Provider}",
                        candidate.Qid, provider.Name);
                }
            }

            // Second pass: retry with original search query if no cover found
            if (coverUrl is null
                && !string.Equals(candidate.Label, originalQuery, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var provider in retailProviders)
                {
                    if (coverUrl is not null) break;
                    try
                    {
                        var retailRequest = new ProviderLookupRequest
                        {
                            EntityId   = Guid.NewGuid(),
                            EntityType = EntityType.Work,
                            MediaType  = mediaType,
                            Title      = originalQuery,
                            BaseUrl    = GetProviderBaseUrl(provider.Name, endpointMap),
                            Language   = "en",
                            Country    = "us",
                        };

                        var results = await provider.SearchAsync(retailRequest, 1, ct).ConfigureAwait(false);
                        if (results.Count > 0 && !string.IsNullOrEmpty(results[0].ThumbnailUrl))
                        {
                            coverUrl = results[0].ThumbnailUrl;
                            retailAuthor ??= results[0].Author;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex,
                            "Retail cover art fallback failed for candidate {Qid} from provider {Provider}",
                            candidate.Qid, provider.Name);
                    }
                }
            }
        }

        // Parse year from the Wikidata description heuristically
        // (e.g. "1965 novel by Frank Herbert" → year="1965")
        string? year = ExtractYearFromDescription(candidate.Description);

        return new UniverseCandidate
        {
            Qid               = candidate.Qid,
            Label             = candidate.Label,
            Description       = candidate.Description,
            Year              = year,
            Author            = retailAuthor ?? ExtractAuthorFromDescription(candidate.Description),
            CoverUrl          = coverUrl,
            ResolutionTier    = candidate.ResolutionTier,
            Confidence        = EstimateConfidence(candidate.ResolutionTier),
            BridgeIds         = new Dictionary<string, string>(),
            MediaType         = mediaType.ToString(),
            MediaTypeMetadata = BuildMediaTypeMetadata(candidate, mediaType, retailAuthor),
        };
    }

    private static IReadOnlyDictionary<string, string>? BuildMediaTypeMetadata(
        QidCandidate candidate, MediaType mediaType, string? retailAuthor)
    {
        return mediaType switch
        {
            MediaType.Books or MediaType.Audiobooks => new Dictionary<string, string>
            {
                ["author"] = retailAuthor ?? ExtractAuthorFromDescription(candidate.Description) ?? string.Empty,
            },
            _ => null,
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

    private static MediaType ParseMediaType(string? mediaTypeStr)
    {
        if (string.IsNullOrWhiteSpace(mediaTypeStr)) return MediaType.Unknown;
        var normalized = mediaTypeStr.Trim() switch
        {
            "Epub"      => "Books",
            "Audiobook" => "Audiobooks",
            "Comics"    => "Comic",
            _           => mediaTypeStr.Trim(),
        };
        return Enum.TryParse<MediaType>(normalized, ignoreCase: true, out var mt) ? mt : MediaType.Unknown;
    }

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
