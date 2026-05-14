using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;
#pragma warning disable CS0618 // suppress obsolete warnings in test stubs

namespace MediaEngine.Providers.Tests;

/// <summary>
/// Verifies that all external metadata adapters degrade gracefully:
/// they return an empty claim list on network failure rather than throwing.
///
/// Config-driven adapters are loaded from <c>config/providers/</c> and
/// wired to stub HTTP handlers that inject predetermined responses (error status,
/// empty body, or timeout) without touching the network.
///
/// Wikidata remains a coded adapter — its fallback test uses the typed class directly.
///
/// Spec: Phase 9 – External Metadata Adapters § Graceful Failure.
/// </summary>
public sealed class AdapterFallbackTests
{
    // ── Apple Books — HTTP 503 ────────────────────────────────────────────────

    [Fact]
    public async Task AppleBooks_Returns_Empty_On_HttpError()
    {
        // Arrange: load config, wire stub returning HTTP 503.
        var config = LoadExampleConfig("apple_api");
        var factory = BuildFactory(config.Name, HttpStatusCode.ServiceUnavailable);
        var adapter = new ConfigDrivenAdapter(
            config, factory, NullLogger<ConfigDrivenAdapter>.Instance, NullProviderHealthMonitor.Instance);

        var request = new ProviderLookupRequest
        {
            EntityId   = Guid.NewGuid(),
            EntityType = EntityType.MediaAsset,
            MediaType  = MediaType.Books,
            Title      = "Dune",
            Author     = "Frank Herbert",
            BaseUrl    = "https://itunes.apple.com",
        };

        // Act
        var claims = await adapter.FetchAsync(request);

        // Assert: empty list, no exception.
        Assert.Empty(claims);
    }

    [Fact]
    public async Task ComicVine_FetchAsync_PrefersIssueSearch_WhenTitleIsPresent()
    {
        var config = LoadExampleConfig("comicvine");
        config.HttpClient ??= new HttpClientConfig();
        config.HttpClient.ApiKey = "test-key";

        var requestedUrls = new List<string>();
        var issueResponse = """
            {
              "results": [
                {
                  "name": "Batman Year One Part 1",
                  "issue_number": "1",
                  "id": 712097,
                  "cover_date": "1987-02-01",
                  "volume": { "name": "Batman" },
                  "image": { "original_url": "https://example.test/batman-year-one.jpg" }
                }
              ]
            }
            """;

        var volumeResponse = """
            {
              "results": [
                {
                  "name": "Batman: Year One",
                  "id": 112492,
                  "start_year": "1988"
                }
              ]
            }
            """;

        var factory = BuildFactory(
            config.Name,
            new RoutingStubHttpMessageHandler(request =>
            {
                var url = request.RequestUri?.ToString() ?? string.Empty;
                requestedUrls.Add(url);

                var body = url.Contains("resources=issue", StringComparison.OrdinalIgnoreCase)
                    ? issueResponse
                    : volumeResponse;

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
            }));

        var adapter = new ConfigDrivenAdapter(
            config, factory, NullLogger<ConfigDrivenAdapter>.Instance, NullProviderHealthMonitor.Instance);

        var request = new ProviderLookupRequest
        {
            EntityId   = Guid.NewGuid(),
            EntityType = EntityType.MediaAsset,
            MediaType  = MediaType.Comics,
            Title      = "Batman: Year One Part 1",
            Series     = "Batman",
            BaseUrl    = "https://comicvine.gamespot.com/api",
        };

        var claims = await adapter.FetchAsync(request);

        Assert.NotEmpty(requestedUrls);
        Assert.Contains("resources=issue", requestedUrls[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(claims, c => string.Equals(c.Key, MetadataFieldConstants.Title, StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.Value, "Batman Year One Part 1", StringComparison.Ordinal));
        Assert.Contains(claims, c => string.Equals(c.Key, "issue_number", StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.Value, "1", StringComparison.Ordinal));
    }

    // ── Config loading ───────────────────────────────────────────────────────

    [Fact]
    public async Task Tmdb_MovieSearch_StoresShortDescriptionButDoesNotSetGenericLanguage()
    {
        var config = LoadExampleConfig("tmdb");
        config.HttpClient ??= new HttpClientConfig();
        config.HttpClient.ApiKey = "test-key";

        var requestedUrls = new List<string>();
        var factory = BuildFactory(
            config.Name,
            new RoutingStubHttpMessageHandler(request =>
            {
                var url = request.RequestUri?.ToString() ?? string.Empty;
                requestedUrls.Add(url);

                if (url.Contains("/search/movie?", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse("""
                        {
                          "results": [
                            {
                              "id": 129,
                              "title": "Spirited Away",
                              "overview": "An English TMDB overview.",
                              "release_date": "2001-07-20",
                              "poster_path": "/poster.jpg",
                              "vote_average": 8.5,
                              "original_language": "ja"
                            }
                          ]
                        }
                        """);
                }

                if (url.Contains("/movie/129?", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse("""
                        {
                          "id": 129,
                          "overview": "An English TMDB detail overview.",
                          "tagline": "The tunnel led somewhere unexpected.",
                          "runtime": 125
                        }
                        """);
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }));

        var adapter = new ConfigDrivenAdapter(
            config, factory, NullLogger<ConfigDrivenAdapter>.Instance, NullProviderHealthMonitor.Instance);

        var claims = await adapter.FetchAsync(new ProviderLookupRequest
        {
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.MediaAsset,
            MediaType = MediaType.Movies,
            Title = "Spirited Away",
            Language = "en",
            Country = "US",
        });

        Assert.Contains(requestedUrls, url => url.Contains("language=en-US", StringComparison.Ordinal));
        Assert.Contains(claims, c => c.Key == MetadataFieldConstants.ShortDescription
            && c.Value == "An English TMDB overview.");
        Assert.Contains(claims, c => c.Key == MetadataFieldConstants.OriginalLanguage
            && c.Value == "ja");
        Assert.DoesNotContain(claims, c => c.Key == MetadataFieldConstants.Language);
    }

    [Fact]
    public async Task Tmdb_MovieSearch_PreservesCastProfileHints()
    {
        var config = LoadExampleConfig("tmdb");
        config.HttpClient ??= new HttpClientConfig();
        config.HttpClient.ApiKey = "test-key";

        var factory = BuildFactory(
            config.Name,
            new RoutingStubHttpMessageHandler(request =>
            {
                var url = request.RequestUri?.ToString() ?? string.Empty;

                if (url.Contains("/search/movie?", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse("""
                        {
                          "results": [
                            {
                              "id": 1001,
                              "title": "Test Movie",
                              "overview": "Overview",
                              "release_date": "2024-01-01"
                            }
                          ]
                        }
                        """);
                }

                if (url.Contains("/movie/1001?", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse("""
                        {
                          "id": 1001,
                          "overview": "Detail overview",
                          "credits": {
                            "cast": [
                              {
                                "id": 12345,
                                "name": "Cosmo Jarvis",
                                "character": "John Blackthorne",
                                "order": 0,
                                "profile_path": "/cosmo.jpg"
                              }
                            ],
                            "crew": [
                              {
                                "id": 98765,
                                "name": "Jane Director",
                                "job": "Director",
                                "profile_path": "/jane.jpg"
                              }
                            ]
                          },
                          "production_companies": [
                            {
                              "name": "FX Productions",
                              "logo_path": "/fx.png"
                            }
                          ]
                        }
                        """);
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }));

        var adapter = new ConfigDrivenAdapter(
            config, factory, NullLogger<ConfigDrivenAdapter>.Instance, NullProviderHealthMonitor.Instance);

        var claims = await adapter.FetchAsync(new ProviderLookupRequest
        {
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.MediaAsset,
            MediaType = MediaType.Movies,
            Title = "Test Movie",
            Language = "en",
            Country = "US",
        });

        Assert.Contains(claims, c => c.Key == MetadataFieldConstants.CastMember
            && c.Value == "Cosmo Jarvis");
        Assert.Contains(claims, c => c.Key == "cast_member_tmdb_id"
            && c.Value == "12345");
        Assert.Contains(claims, c => c.Key == "cast_member_profile_url"
            && c.Value == "https://image.tmdb.org/t/p/original/cosmo.jpg");
        Assert.Contains(claims, c => c.Key == "cast_member_character"
            && c.Value == "John Blackthorne");
        Assert.Contains(claims, c => c.Key == "director"
            && c.Value == "Jane Director");
        Assert.Contains(claims, c => c.Key == "director_tmdb_id"
            && c.Value == "98765");
        Assert.Contains(claims, c => c.Key == "director_profile_url"
            && c.Value == "https://image.tmdb.org/t/p/original/jane.jpg");
        Assert.Contains(claims, c => c.Key == "studio"
            && c.Value == "FX Productions");
        Assert.Contains(claims, c => c.Key == "studio_logo_url"
            && c.Value == "https://image.tmdb.org/t/p/original/fx.png");
        Assert.Contains(claims, c => c.Key == "production_company"
            && c.Value == "FX Productions");
    }

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static ProviderConfiguration LoadExampleConfig(string providerName)
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "config", "providers", $"{providerName}.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ProviderConfiguration>(json, s_jsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize config: {providerName}");
    }

    private static string FindRepoRoot()
    {
        var dir = Path.GetDirectoryName(typeof(AdapterFallbackTests).Assembly.Location);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not find repository root (.git directory)");
    }

    // ── Stub HTTP helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Builds an <see cref="IHttpClientFactory"/> that routes the named client
    /// through a stub handler returning <paramref name="statusCode"/>.
    /// </summary>
    private static IHttpClientFactory BuildFactory(
        string clientName,
        HttpStatusCode statusCode,
        Action<HttpRequestMessage>? onRequest = null)
    {
        var handler = new StubHttpMessageHandler(statusCode, onRequest);
        return BuildFactory(clientName, handler);
    }

    private static IHttpClientFactory BuildFactory(
        string clientName,
        HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(clientName)
                .ConfigurePrimaryHttpMessageHandler(() => handler);
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IHttpClientFactory>();
    }

    private static HttpResponseMessage JsonResponse(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    /// <summary>
    /// Builds an <see cref="IHttpClientFactory"/> whose named client throws
    /// <see cref="TaskCanceledException"/> on every request.
    /// </summary>
    private static IHttpClientFactory BuildTimeoutFactory(string clientName)
    {
        var handler  = new TimeoutStubHttpMessageHandler();
        var services = new ServiceCollection();
        services.AddHttpClient(clientName)
                .ConfigurePrimaryHttpMessageHandler(() => handler);
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IHttpClientFactory>();
    }
}

// ── Stub HTTP handlers ────────────────────────────────────────────────────────

/// <summary>
/// Returns a fixed <see cref="HttpStatusCode"/> with an empty body for every request.
/// </summary>
file sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly Action<HttpRequestMessage>? _onRequest;

    public StubHttpMessageHandler(
        HttpStatusCode statusCode,
        Action<HttpRequestMessage>? onRequest = null)
    {
        _statusCode = statusCode;
        _onRequest  = onRequest;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _onRequest?.Invoke(request);
        return Task.FromResult(new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(string.Empty),
        });
    }
}

/// <summary>
/// Throws <see cref="TaskCanceledException"/> on every request to simulate a timeout.
/// </summary>
file sealed class TimeoutStubHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => throw new TaskCanceledException("Simulated HTTP timeout in test.");
}

/// <summary>
/// Routes requests to a caller-supplied responder so tests can return different
/// payloads for different URLs without touching the network.
/// </summary>
file sealed class RoutingStubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public RoutingStubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => Task.FromResult(_responder(request));
}

/// <summary>
/// Minimal <see cref="IConfigurationLoader"/> stub for adapter tests.
/// Returns defaults for all methods — no file I/O.
/// </summary>
file sealed class StubConfigurationLoader : IConfigurationLoader
{
    public CoreConfiguration LoadCore() => new();
    public void SaveCore(CoreConfiguration config) { }
    public ScoringSettings LoadScoring() => new();
    public void SaveScoring(ScoringSettings settings) { }
    public MaintenanceSettings LoadMaintenance() => new();
    public void SaveMaintenance(MaintenanceSettings settings) { }
    public HydrationSettings LoadHydration() => new();
    public void SaveHydration(HydrationSettings settings) { }
    public PipelineConfiguration LoadPipelines() => new();
    public void SavePipelines(PipelineConfiguration config) { }
    public DisambiguationSettings LoadDisambiguation() => new();
    public void SaveDisambiguation(DisambiguationSettings settings) { }
    public MediaTypeConfiguration LoadMediaTypes() => new();
    public void SaveMediaTypes(MediaTypeConfiguration config) { }
    public TranscodingSettings LoadTranscoding() => new();
    public void SaveTranscoding(TranscodingSettings settings) { }
    public FieldPriorityConfiguration LoadFieldPriorities() => new();
    public void SaveFieldPriorities(FieldPriorityConfiguration config) { }
    public LibrariesConfiguration LoadLibraries() => new();
    public ProviderConfiguration? LoadProvider(string name) => null;
    public void SaveProvider(ProviderConfiguration config) { }
    public IReadOnlyList<ProviderConfiguration> LoadAllProviders() => [];
    public T? LoadConfig<T>(string subdirectory, string name) where T : class => null;
    public void SaveConfig<T>(string subdirectory, string name, T config) where T : class { }
    public T? LoadAi<T>() where T : class => default;
    public void SaveAi<T>(T settings) where T : class { }
    public PaletteConfiguration LoadPalette() => new();
    public void SavePalette(PaletteConfiguration palette) { }
}

/// <summary>No-op QID label repository for adapter tests.</summary>
file sealed class NoOpQidLabelRepository : IQidLabelRepository
{
    public Task<string?> GetLabelAsync(string qid, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task<IReadOnlyDictionary<string, string>> GetLabelsAsync(IEnumerable<string> qids, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    public Task UpsertAsync(string qid, string label, string? description, string? entityType, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpsertBatchAsync(IReadOnlyList<QidLabel> labels, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<QidLabel>> GetLabelDetailsAsync(IEnumerable<string> qids, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<QidLabel>>([]);
    public Task<IReadOnlyList<QidLabel>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<QidLabel>>([]);
}

/// <summary>No-op resolver cache for adapter tests.</summary>
file sealed class NoOpResolverCacheRepository : IResolverCacheRepository
{
    public Task<ResolverCacheEntry?> FindAsync(string cacheKey, CancellationToken ct = default) => Task.FromResult<ResolverCacheEntry?>(null);
    public Task UpsertAsync(ResolverCacheEntry entry, CancellationToken ct = default) => Task.CompletedTask;
    public Task<int> PurgeExpiredAsync(CancellationToken ct = default) => Task.FromResult(0);
}

/// <summary>No-op provider response cache for adapter tests.</summary>
file sealed class NoOpProviderResponseCacheRepository : IProviderResponseCacheRepository
{
    public Task<CachedResponse?> FindAsync(string cacheKey, CancellationToken ct = default) => Task.FromResult<CachedResponse?>(null);
    public Task UpsertAsync(string cacheKey, string providerId, string queryHash, string responseJson, string? etag, int ttlHours, CancellationToken ct = default) => Task.CompletedTask;
    public Task<string?> FindExpiredEtagAsync(string cacheKey, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task RefreshExpiryAsync(string cacheKey, int ttlHours, CancellationToken ct = default) => Task.CompletedTask;
    public Task<int> PurgeExpiredAsync(CancellationToken ct = default) => Task.FromResult(0);
    public Task<int> ClearAllAsync(CancellationToken ct = default) => Task.FromResult(0);
    public Task<CacheStats> GetStatsAsync(CancellationToken ct = default) => Task.FromResult(new CacheStats(0, 0, null));
}
