using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
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
/// Retail search: calls the relevant retail providers (filtered by media type) via
/// SearchAsync to return title/cover matches without requiring a Wikidata QID.
/// </summary>
public sealed class SearchService : ISearchService
{
    private readonly IReadOnlyList<IExternalMetadataProvider> _providers;
    private readonly IConfigurationLoader _configLoader;
    private readonly IFuzzyMatchingService _fuzzy;
    private readonly IRetailMatchScoringService _retailScoring;
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
        IFuzzyMatchingService fuzzy,
        IRetailMatchScoringService retailScoring,
        ILogger<SearchService> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(configLoader);
        ArgumentNullException.ThrowIfNull(fuzzy);
        ArgumentNullException.ThrowIfNull(retailScoring);
        ArgumentNullException.ThrowIfNull(logger);

        var providerList = providers.ToList();

        _providers      = providerList;
        _configLoader   = configLoader;
        _fuzzy          = fuzzy;
        _retailScoring  = retailScoring;
        _logger         = logger;
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
        var providerEndpoints = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pc in provConfigs)
            providerEndpoints[pc.Name] = new Dictionary<string, string>(pc.Endpoints, StringComparer.OrdinalIgnoreCase);

        try
        {
            // Search Wikidata via Reconciliation API.
            // When local_author is provided, append it to the query for disambiguation.
            // The Wikidata wbsearchentities API uses the full query string for matching —
            // property constraints (P50) only filter/re-score results after the initial search,
            // so "Die Verwandlung" alone won't find the Kafka novella, but
            // "Die Verwandlung Franz Kafka" will.
            var searchQuery = request.Query;
            if (!string.IsNullOrWhiteSpace(request.LocalAuthor)
                && !searchQuery.Contains(request.LocalAuthor, StringComparison.OrdinalIgnoreCase))
            {
                searchQuery = $"{searchQuery} {request.LocalAuthor}";
            }

            var providerRequest = new ProviderLookupRequest
            {
                EntityId   = Guid.NewGuid(),
                EntityType = EntityType.Work,
                MediaType  = mediaType,
                Title      = searchQuery,
                Author     = request.LocalAuthor,
                BaseUrl    = GetProviderBaseUrl(wikidataProvider.Name, providerEndpoints),
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
                    qidCandidate, request.Query, mediaType, retailProviders, providerEndpoints, ct)
                    .ConfigureAwait(false);

                candidates.Add(enriched);
            }

            // If local context provided, score and re-rank candidates using
            // the unified retail match scoring service (same as pipeline).
            if (!string.IsNullOrWhiteSpace(request.LocalTitle))
            {
                var fileHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["title"] = request.LocalTitle,
                };
                if (!string.IsNullOrWhiteSpace(request.LocalAuthor))
                    fileHints["author"] = request.LocalAuthor;
                if (!string.IsNullOrWhiteSpace(request.LocalYear))
                    fileHints["year"] = request.LocalYear;

                foreach (var c in candidates)
                {
                    var scores = _retailScoring.ScoreCandidate(
                        fileHints, c.Label, c.Author, c.Year, mediaType);
                    c.MatchScores = ToFieldMatchResult(scores);
                }
                candidates = candidates.OrderByDescending(c => c.MatchScores?.CompositeScore ?? 0.0).ToList();
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
        var providerEndpoints = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pc in provConfigs)
            providerEndpoints[pc.Name] = new Dictionary<string, string>(pc.Endpoints, StringComparer.OrdinalIgnoreCase);

        var candidates = new List<RetailCandidate>();

        // Call each retail provider in parallel
        var tasks = retailProviders.Select(p =>
            SearchProviderAsync(p, request.Query, mediaType, providerEndpoints, request.MaxCandidates, request.SearchFields, ct));

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        foreach (var providerResults in results)
            candidates.AddRange(providerResults);

        // ── Unified retail match scoring ─────────────────────────────────────
        // Score each candidate against file metadata using the same service
        // the pipeline uses, ensuring consistent confidence numbers everywhere.
        if (!string.IsNullOrWhiteSpace(request.LocalTitle))
        {
            var fileHints = BuildFileHints(request);

            foreach (var c in candidates)
            {
                var extMeta = new CandidateExtendedMetadata
                {
                    Description = c.Description,
                    Genres = c.ExtraFields.TryGetValue("genre", out var g) ? [g] : null,
                    Language = c.ExtraFields.GetValueOrDefault("language"),
                };
                var scores = _retailScoring.ScoreCandidate(
                    fileHints, c.Title, c.Author, c.Year, mediaType,
                    extendedMetadata: extMeta);

                c.MatchScores = ToFieldMatchResult(scores);
                c.CompositeScore = scores.CompositeScore;
            }

            candidates = candidates.OrderByDescending(c => c.CompositeScore).ToList();
        }
        else
        {
            // No local context — composite equals provider confidence
            foreach (var c in candidates)
                c.CompositeScore = c.Confidence;
        }

        return new SearchRetailResult(candidates, request.Query, request.MediaType);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<UniverseCandidate> EnrichCandidateAsync(
        QidCandidate candidate,
        string originalQuery,
        MediaType mediaType,
        IReadOnlyList<IExternalMetadataProvider> retailProviders,
        Dictionary<string, Dictionary<string, string>> providerEndpoints,
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
                        BaseUrl    = GetProviderBaseUrl(provider.Name, providerEndpoints),
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
                            BaseUrl    = GetProviderBaseUrl(provider.Name, providerEndpoints),
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
        Dictionary<string, Dictionary<string, string>> providerEndpoints,
        int maxCandidates,
        IReadOnlyDictionary<string, string>? searchFields,
        CancellationToken ct)
    {
        try
        {
            // When structured search fields are provided, extract title/author
            // for the provider query template (e.g. "{title} {author}").
            // Falls back to the combined query string when fields are absent.
            string? fieldTitle = null, fieldAuthor = null;
            string? fieldShowName = null, fieldAlbum = null, fieldArtist = null;
            string? fieldDirector = null, fieldComposer = null, fieldGenre = null;
            if (searchFields is { Count: > 0 })
            {
                searchFields.TryGetValue("title", out fieldTitle);
                // Also check show_name and podcast_name as title sources
                if (fieldTitle is null)
                {
                    if (!searchFields.TryGetValue("show_name", out fieldTitle))
                        searchFields.TryGetValue("podcast_name", out fieldTitle);
                }
                if (!searchFields.TryGetValue("author", out fieldAuthor))
                    searchFields.TryGetValue("artist", out fieldAuthor);
                searchFields.TryGetValue("show_name", out fieldShowName);
                searchFields.TryGetValue("album", out fieldAlbum);
                searchFields.TryGetValue("artist", out fieldArtist);
                searchFields.TryGetValue("director", out fieldDirector);
                searchFields.TryGetValue("composer", out fieldComposer);
                searchFields.TryGetValue("genre", out fieldGenre);
            }

            var providerRequest = new ProviderLookupRequest
            {
                EntityId   = Guid.NewGuid(),
                EntityType = EntityType.Work,
                MediaType  = mediaType,
                Title      = fieldTitle ?? query,
                Author     = fieldAuthor,
                ShowName   = fieldShowName,
                Album      = fieldAlbum,
                Artist     = fieldArtist,
                Director   = fieldDirector,
                Composer   = fieldComposer,
                Genre      = fieldGenre,
                BaseUrl    = GetProviderBaseUrl(provider.Name, providerEndpoints),
                Language   = "en",
                Country    = "us",
                Hints      = searchFields is { Count: > 0 }
                    ? new Dictionary<string, string>(searchFields)
                    : null,
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
                ExtraFields    = r.ExtraFields ?? new Dictionary<string, string>(),
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
        var allConfigs = _configLoader.LoadAllProviders();

        return _providers
            .Where(p =>
            {
                if (ExcludedFromRetail.Contains(p.Name)) return false;
                if (!p.CanHandle(mediaType) || !p.CanHandle(EntityType.Work)) return false;

                // Respect the enabled flag from provider configuration
                var cfg = allConfigs.FirstOrDefault(c =>
                    string.Equals(c.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                return cfg is null || cfg.Enabled;
            })
            .ToList();
    }

    private static string GetProviderBaseUrl(
        string providerName,
        Dictionary<string, Dictionary<string, string>> providerEndpoints)
    {
        if (providerEndpoints.TryGetValue(providerName, out var endpoints))
        {
            if (endpoints.TryGetValue(providerName, out var url))
                return url.TrimEnd('/');
            if (endpoints.TryGetValue("api", out var apiUrl))
                return apiUrl.TrimEnd('/');
            if (endpoints.Count > 0)
                return endpoints.Values.First().TrimEnd('/');
        }
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

    /// <summary>
    /// Builds file hint dictionary from a retail search request for use with
    /// <see cref="IRetailMatchScoringService"/>.
    /// </summary>
    private static Dictionary<string, string> BuildFileHints(SearchRetailRequest request)
    {
        var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(request.LocalTitle))
            hints["title"] = request.LocalTitle;
        if (!string.IsNullOrWhiteSpace(request.LocalAuthor))
            hints["author"] = request.LocalAuthor;
        if (!string.IsNullOrWhiteSpace(request.LocalYear))
            hints["year"] = request.LocalYear;

        // Merge any additional file hints (narrator, series, publisher, etc.)
        if (request.FileHints is { Count: > 0 })
        {
            foreach (var (k, v) in request.FileHints)
            {
                hints.TryAdd(k, v);
            }
        }
        return hints;
    }

    /// <summary>
    /// Maps <see cref="FieldMatchScores"/> (from RetailMatchScoringService) to
    /// <see cref="FieldMatchResult"/> (used by search result display).
    /// </summary>
    private static FieldMatchResult ToFieldMatchResult(FieldMatchScores scores)
    {
        return new FieldMatchResult
        {
            TitleScore      = scores.TitleScore,
            AuthorScore     = scores.AuthorScore,
            YearScore       = scores.YearScore,
            FormatScore     = scores.FormatScore,
            CoverScore      = scores.CoverArtScore,
            CompositeScore  = scores.CompositeScore,
            TitleVerdict    = ToVerdict(scores.TitleScore),
            AuthorVerdict   = scores.AuthorScore < 0 ? FieldMatchVerdict.NotAvailable : ToVerdict(scores.AuthorScore),
            YearVerdict     = scores.YearScore < 0 ? FieldMatchVerdict.NotAvailable : ToVerdict(scores.YearScore),
            FormatVerdict   = ToVerdict(scores.FormatScore),
            CoverVerdict    = scores.CoverArtScore < 0 ? FieldMatchVerdict.NotAvailable : ToVerdict(scores.CoverArtScore),
        };
    }

    private static FieldMatchVerdict ToVerdict(double score) => score switch
    {
        >= 0.95 => FieldMatchVerdict.Exact,
        >= 0.70 => FieldMatchVerdict.Close,
        _       => FieldMatchVerdict.Mismatch,
    };
}
