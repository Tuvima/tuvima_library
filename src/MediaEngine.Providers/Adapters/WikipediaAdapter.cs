using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Enums;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;

namespace MediaEngine.Providers.Adapters;

/// <summary>
/// Retrieves human-readable descriptions from Wikipedia via its REST API.
///
/// This is a Stage 2 (Context Match) adapter. It requires a Wikidata QID
/// from Stage 1 to resolve the Wikipedia article title via sitelinks. If no
/// QID is available, it returns an empty list.
///
/// Two-step flow:
///   1. Call Wikidata wbgetentities API with the QID and props=sitelinks to
///      get the Wikipedia article title for the requested language (falls back
///      to English).
///   2. Call the Wikipedia REST API /page/summary/{title} to fetch the
///      plain-text extract.
///
/// Emits two claims:
///   - description (confidence 0.85) the article extract, truncated to the
///     configured max character length.
///   - description_source (confidence 1.0) attribution string.
///
/// Named HttpClient: wikipedia_api.
/// </summary>
public sealed class WikipediaAdapter : IExternalMetadataProvider
{
    /// <summary>
    /// Stable provider GUID. Written to metadata_claims.provider_id.
    /// </summary>
    public static readonly Guid AdapterProviderId
        = new("b4000004-d000-4000-8000-000000000005");

    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfigurationLoader _configLoader;
    private readonly ILogger<WikipediaAdapter> _logger;

    private DateTime _lastCallUtc = DateTime.MinValue;
    private static readonly TimeSpan ThrottleGap = TimeSpan.FromMilliseconds(100);

    public WikipediaAdapter(
        IHttpClientFactory httpFactory,
        IConfigurationLoader configLoader,
        ILogger<WikipediaAdapter> logger)
    {
        _httpFactory = httpFactory;
        _configLoader = configLoader;
        _logger = logger;
    }

    // IExternalMetadataProvider

    public string Name => "wikipedia";

    public ProviderDomain Domain => ProviderDomain.Universal;

    public IReadOnlyList<string> CapabilityTags { get; } = ["description"];

    public Guid ProviderId => AdapterProviderId;

    public bool CanHandle(MediaType mediaType) => true;

    public bool CanHandle(EntityType entityType) =>
        entityType is EntityType.MediaAsset or EntityType.Work or EntityType.Person;

    // Core fetch

    public async Task<IReadOnlyList<ProviderClaim>> FetchAsync(
        ProviderLookupRequest request,
        CancellationToken ct = default)
    {
        var qid = request.PreResolvedQid;
        if (string.IsNullOrWhiteSpace(qid))
        {
            _logger.LogDebug("WikipediaAdapter: No QID provided, skipping");
            return [];
        }

        try
        {
            var lang = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language;

            // Step 1: Resolve article title from Wikidata sitelinks
            var articleTitle = await GetSitelinkTitleAsync(qid, lang, ct);
            if (string.IsNullOrWhiteSpace(articleTitle))
            {
                _logger.LogDebug(
                    "WikipediaAdapter: No {Lang}wiki sitelink for {Qid}",
                    lang, qid);
                return await FetchWikidataShortDescriptionAsync(qid, request, ct);
            }

            // Step 2: Fetch summary from Wikipedia REST API
            var summary = await FetchSummaryAsync(articleTitle, lang, ct);
            if (string.IsNullOrWhiteSpace(summary))
            {
                _logger.LogDebug(
                    "WikipediaAdapter: Empty summary for {Title} ({Qid})",
                    articleTitle, qid);
                return await FetchWikidataShortDescriptionAsync(qid, request, ct);
            }

            // Truncate to configured max length
            var maxChars = LoadMaxChars();
            if (summary.Length > maxChars)
                summary = string.Concat(summary.AsSpan(0, maxChars), "\u2026");

            _logger.LogInformation(
                "WikipediaAdapter: Fetched {Len}-char description for {Qid} ({Title})",
                summary.Length, qid, articleTitle);

            return
            [
                new ProviderClaim("description", summary, 0.85),
                new ProviderClaim("description_source", "Wikipedia (CC BY-SA 4.0)", 1.0),
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "WikipediaAdapter: Failed for QID {Qid}", qid);
            return [];
        }
    }

    // Sitelink resolution

    /// <summary>
    /// Calls the Wikidata wbgetentities API to look up the sitelink for the
    /// given language wiki. Falls back to English if the requested language
    /// sitelink does not exist.
    /// </summary>
    private async Task<string?> GetSitelinkTitleAsync(
        string qid, string lang, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("wikipedia_api");

        var url = "https://www.wikidata.org/w/api.php"
                + "?action=wbgetentities&ids=" + Uri.EscapeDataString(qid)
                + "&props=sitelinks&sitefilter=" + lang + "wiki|enwiki"
                + "&format=json";

        var json = await ThrottledGetAsync(client, url, ct);
        if (json is null) return null;

        var entities = json["entities"];
        var entity = entities?[qid];
        var sitelinks = entity?["sitelinks"];
        if (sitelinks is null) return null;

        // Prefer requested language, fall back to English
        var siteKey = lang + "wiki";
        var sitelink = sitelinks[siteKey] ?? sitelinks["enwiki"];

        return sitelink?["title"]?.GetValue<string>();
    }

    // Wikipedia REST API

    /// <summary>
    /// Calls the Wikipedia REST API /page/summary/{title} to fetch the
    /// plain-text extract of an article.
    /// </summary>
    private async Task<string?> FetchSummaryAsync(
        string articleTitle, string lang, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("wikipedia_api");

        var encodedTitle = Uri.EscapeDataString(articleTitle.Replace(' ', '_'));
        var url = "https://" + lang + ".wikipedia.org/api/rest_v1/page/summary/" + encodedTitle;

        var json = await ThrottledGetAsync(client, url, ct);
        if (json is null) return null;

        // The REST API returns { "type": "standard", "extract": "..." }
        var extractType = json["type"]?.GetValue<string>();
        if (extractType == "disambiguation")
        {
            _logger.LogDebug(
                "WikipediaAdapter: Article is a disambiguation page, skipping");
            return null;
        }

        return json["extract"]?.GetValue<string>();
    }

    // Throttled HTTP

    private async Task<JsonNode?> ThrottledGetAsync(
        HttpClient client, string url, CancellationToken ct)
    {
        // Simple throttle to avoid hammering Wikipedia/Wikidata
        var elapsed = DateTime.UtcNow - _lastCallUtc;
        if (elapsed < ThrottleGap)
            await Task.Delay(ThrottleGap - elapsed, ct);

        _lastCallUtc = DateTime.UtcNow;

        using var response = await client.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("WikipediaAdapter: 404 for {Url}", url);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<JsonNode>(stream, cancellationToken: ct);
    }

    // Wikidata short description fallback

    /// <summary>
    /// Called when the main Wikipedia flow produced no description. Fetches
    /// the Wikidata short description via wbgetentities?props=descriptions.
    /// Returns two claims at confidence 0.70 (lower than Wikipedia's 0.85)
    /// or an empty list if the call fails or the description is absent.
    /// </summary>
    private async Task<IReadOnlyList<ProviderClaim>> FetchWikidataShortDescriptionAsync(
        string qid,
        ProviderLookupRequest request,
        CancellationToken ct)
    {
        try
        {
            var client = _httpFactory.CreateClient("wikipedia_api");

            // Use a configured Wikidata API base URL when available (same
            // pattern as WikidataAdapter), otherwise fall back to the
            // canonical public endpoint already used by GetSitelinkTitleAsync.
            var apiBase = string.IsNullOrWhiteSpace(request.SparqlBaseUrl)
                ? "https://www.wikidata.org/w/api.php"
                : request.SparqlBaseUrl.TrimEnd('/').Replace("/sparql", "/w/api.php");

            var url = apiBase
                    + "?action=wbgetentities&ids=" + Uri.EscapeDataString(qid)
                    + "&props=descriptions&languages=en&format=json";

            var json = await ThrottledGetAsync(client, url, ct);
            if (json is null) return [];

            var description = json["entities"]?[qid]?["descriptions"]?["en"]?["value"]
                ?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(description))
            {
                _logger.LogDebug(
                    "WikipediaAdapter: No Wikidata short description for {Qid}", qid);
                return [];
            }

            _logger.LogInformation(
                "WikipediaAdapter: Using Wikidata short description for {Qid}", qid);

            return
            [
                new ProviderClaim("description", description, 0.70),
                new ProviderClaim("description_source", "Wikidata (short description)", 0.70),
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(
                ex, "WikipediaAdapter: Wikidata short description fallback failed for {Qid}", qid);
            return [];
        }
    }

    // Config

    private int LoadMaxChars()
    {
        try
        {
            return _configLoader.LoadHydration().WikipediaDescriptionMaxChars;
        }
        catch
        {
            return 1000; // compiled default
        }
    }
}
