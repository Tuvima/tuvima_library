using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
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
/// Config-driven adapters are loaded from <c>config.example/providers/</c> and
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

    // ── Wikidata — TaskCanceledException (simulated timeout) ─────────────────
    // TODO: Phase 3 - Wikidata fallback test disabled (WikidataAdapter removed in SPARQL cleanup)
    // Will be replaced with ReconciliationAdapter fallback test in Phase 3
    // [Fact]
    // public async Task Wikidata_Returns_Empty_On_Timeout() { ... }

    // ── Config loading ───────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static ProviderConfiguration LoadExampleConfig(string providerName)
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "config.example", "providers", $"{providerName}.json");
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
        var services = new ServiceCollection();
        services.AddHttpClient(clientName)
                .ConfigurePrimaryHttpMessageHandler(() => handler);
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IHttpClientFactory>();
    }

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
    public ProviderSlotConfiguration LoadSlots() => new();
    public void SaveSlots(ProviderSlotConfiguration slots) { }
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
