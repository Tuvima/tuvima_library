using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;
using Xunit.Abstractions;

namespace MediaEngine.Providers.Tests;

/// <summary>
/// Integration tests that call real provider APIs with known test data.
///
/// Test data: "The Fellowship of the Ring" by J.R.R. Tolkien (ISBN: 9780547928210)
/// Each test validates that the adapter returns at least one claim from the live API.
///
/// Config-driven adapters are loaded from <c>config.example/providers/</c> to verify
/// that the JSON config files correctly drive the universal adapter against live APIs.
///
/// Wikidata remains a coded adapter (SPARQL cannot be expressed as URL templates).
///
/// Cover art: downloaded as bytes, SHA-256 hashed, hash logged — images discarded.
/// These tests are intentionally separated from unit tests via the Integration trait.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProviderIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public ProviderIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ── Apple Books (Ebook) ──────────────────────────────────────────────────

    [Fact(Skip = "Requires live Apple API network access. Run locally with: dotnet test --filter Category=Integration")]
    public async Task AppleBooks_Ebook_Returns_Claims_For_FellowshipOfTheRing()
    {
        var adapter = BuildConfigDrivenAdapter("apple_api");

        var request = new ProviderLookupRequest
        {
            EntityId   = Guid.NewGuid(),
            EntityType = EntityType.MediaAsset,
            MediaType  = MediaType.Books,
            Title      = "The Fellowship of the Ring",
            Author     = "J.R.R. Tolkien",
            BaseUrl    = "https://itunes.apple.com",
        };

        var claims = await adapter.FetchAsync(request);

        LogClaims("Apple Books (Ebook)", claims);
        Assert.NotEmpty(claims);

        // Expect at least title and cover.
        AssertHasClaim(claims, "title");

        // Hash cover art if present.
        await HashCoverArt(claims);
    }

    // ── Apple Books (Audiobook) ──────────────────────────────────────────────

    [Fact(Skip = "Requires live Apple API network access. Run locally with: dotnet test --filter Category=Integration")]
    public async Task AppleBooks_Audiobook_Returns_Claims_For_FellowshipOfTheRing()
    {
        var adapter = BuildConfigDrivenAdapter("apple_api");

        var request = new ProviderLookupRequest
        {
            EntityId   = Guid.NewGuid(),
            EntityType = EntityType.MediaAsset,
            MediaType  = MediaType.Audiobooks,
            Title      = "The Fellowship of the Ring",
            Author     = "J.R.R. Tolkien",
            BaseUrl    = "https://itunes.apple.com",
        };

        var claims = await adapter.FetchAsync(request);

        LogClaims("Apple Books (Audiobook)", claims);
        Assert.NotEmpty(claims);

        // Audiobook results may not include title (iTunes returns cover + description).
        await HashCoverArt(claims);
    }

    // ── Open Library ─────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenLibrary_Returns_Claims_For_FellowshipOfTheRing()
    {
        var adapter = BuildConfigDrivenAdapter("open_library");

        var request = new ProviderLookupRequest
        {
            EntityId   = Guid.NewGuid(),
            EntityType = EntityType.MediaAsset,
            MediaType  = MediaType.Books,
            Title      = "The Fellowship of the Ring",
            Author     = "J.R.R. Tolkien",
            Isbn       = "9780547928210",
            BaseUrl    = "https://openlibrary.org",
        };

        var claims = await adapter.FetchAsync(request);

        LogClaims("Open Library", claims);
        Assert.NotEmpty(claims);

        // Expect title, author, isbn, year.
        AssertHasClaim(claims, "title");
        AssertHasClaim(claims, "author");
        AssertHasClaim(claims, "isbn");

        await HashCoverArt(claims);
    }

    // ── Open Library (ISBN-first search) ─────────────────────────────────────

    [Fact]
    public async Task OpenLibrary_ISBN_Search_Returns_Claims()
    {
        var adapter = BuildConfigDrivenAdapter("open_library");

        var request = new ProviderLookupRequest
        {
            EntityId   = Guid.NewGuid(),
            EntityType = EntityType.MediaAsset,
            MediaType  = MediaType.Books,
            Title      = "The Fellowship of the Ring",
            Isbn       = "9780547928210",
            BaseUrl    = "https://openlibrary.org",
        };

        var claims = await adapter.FetchAsync(request);

        LogClaims("Open Library (ISBN)", claims);
        Assert.NotEmpty(claims);
    }

    // ── Google Books ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GoogleBooks_Handles_Lookup_Gracefully()
    {
        var adapter = BuildConfigDrivenAdapter("google_books");

        var request = new ProviderLookupRequest
        {
            EntityId   = Guid.NewGuid(),
            EntityType = EntityType.MediaAsset,
            MediaType  = MediaType.Books,
            Title      = "The Fellowship of the Ring",
            Author     = "J.R.R. Tolkien",
            Isbn       = "9780547928210",
            BaseUrl    = "https://www.googleapis.com/books/v1",
        };

        var claims = await adapter.FetchAsync(request);

        LogClaims("Google Books", claims);

        // Google Books may return 429 (rate limit exceeded) when the anonymous
        // quota is exhausted. The adapter degrades gracefully in this case.
        _output.WriteLine($"  Google Books returned {claims.Count} claims (may be rate-limited).");

        if (claims.Count > 0)
        {
            AssertHasClaim(claims, "title");
            AssertHasClaim(claims, "author");
            await HashCoverArt(claims);
        }
    }

    // ── Wikidata ─────────────────────────────────────────────────────────────
    // TODO: Phase 3 - Wikidata integration test disabled (WikidataAdapter removed in SPARQL cleanup)
    // Will be replaced with ReconciliationAdapter integration test in Phase 3
    // [Fact]
    // public async Task Wikidata_Returns_Claims_For_FellowshipOfTheRing() { ... }

    // ── Search tests (multi-result SearchAsync) ─────────────────────────────

    [Fact(Skip = "Requires live Apple API network access. Run locally with: dotnet test --filter Category=Integration")]
    public async Task AppleBooks_Search_Returns_Results()
    {
        var adapter = BuildConfigDrivenAdapter("apple_api");

        var request = new ProviderLookupRequest
        {
            EntityId   = Guid.NewGuid(),
            EntityType = EntityType.MediaAsset,
            MediaType  = MediaType.Books,
            Title      = "The Fellowship of the Ring",
            Author     = "J.R.R. Tolkien",
            BaseUrl    = "https://itunes.apple.com",
        };

        var results = await adapter.SearchAsync(request, limit: 10);

        _output.WriteLine($"Apple Books Search: {results.Count} results.");
        foreach (var r in results)
            _output.WriteLine($"  [{r.ProviderName}] \"{r.Title}\" by {r.Author} ({r.Year})");

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.Title)));
    }

    [Fact]
    public async Task OpenLibrary_Search_Returns_Results()
    {
        var adapter = BuildConfigDrivenAdapter("open_library");

        var request = new ProviderLookupRequest
        {
            EntityId   = Guid.NewGuid(),
            EntityType = EntityType.MediaAsset,
            MediaType  = MediaType.Books,
            Title      = "The Fellowship of the Ring",
            Author     = "J.R.R. Tolkien",
            BaseUrl    = "https://openlibrary.org",
        };

        var results = await adapter.SearchAsync(request, limit: 10);

        _output.WriteLine($"Open Library Search: {results.Count} results.");
        foreach (var r in results)
            _output.WriteLine($"  [{r.ProviderName}] \"{r.Title}\" by {r.Author} ({r.Year})");

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.Title)));
    }

    [Fact]
    public async Task GoogleBooks_Search_Returns_Results()
    {
        var adapter = BuildConfigDrivenAdapter("google_books");

        var request = new ProviderLookupRequest
        {
            EntityId   = Guid.NewGuid(),
            EntityType = EntityType.MediaAsset,
            MediaType  = MediaType.Books,
            Title      = "The Fellowship of the Ring",
            Author     = "J.R.R. Tolkien",
            BaseUrl    = "https://www.googleapis.com/books/v1",
        };

        var results = await adapter.SearchAsync(request, limit: 10);

        _output.WriteLine($"Google Books Search: {results.Count} results (may be rate-limited).");
        foreach (var r in results)
            _output.WriteLine($"  [{r.ProviderName}] \"{r.Title}\" by {r.Author} ({r.Year})");

        // Google Books may return 429 — assert graceful degradation.
        if (results.Count > 0)
            Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.Title)));
    }

    // TODO: Phase 3 - Wikidata_Search_Returns_QidClaims test disabled (WikidataAdapter removed in SPARQL cleanup)
    // Will be replaced with ReconciliationAdapter search test in Phase 3
    // [Fact]
    // public async Task Wikidata_Search_Returns_QidClaims() { ... }

    // ── Config-driven adapter builder ───────────────────────────────────────

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Loads a <see cref="ProviderConfiguration"/> from <c>config.example/providers/{name}.json</c>
    /// and creates a <see cref="ConfigDrivenAdapter"/> with a real HTTP client factory.
    /// Proves that the JSON config files correctly drive the universal adapter.
    /// </summary>
    private static ConfigDrivenAdapter BuildConfigDrivenAdapter(string configName)
    {
        var config = LoadExampleConfig(configName);
        var factory = BuildRealHttpFactory(config.Name);
        return new ConfigDrivenAdapter(config, factory, NullLogger<ConfigDrivenAdapter>.Instance, NullProviderHealthMonitor.Instance);
    }

    /// <summary>
    /// Deserialises a provider config from the checked-in example files.
    /// </summary>
    private static ProviderConfiguration LoadExampleConfig(string providerName)
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "config.example", "providers", $"{providerName}.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ProviderConfiguration>(json, s_jsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize config: {providerName}");
    }

    /// <summary>
    /// Walks up from the test assembly location to find the repository root (.git directory).
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = Path.GetDirectoryName(typeof(ProviderIntegrationTests).Assembly.Location);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not find repository root (.git directory)");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void LogClaims(string providerName, IReadOnlyList<ProviderClaim> claims)
    {
        _output.WriteLine($"\n═══ {providerName} — {claims.Count} claims ═══");
        foreach (var c in claims)
        {
            var displayValue = c.Value.Length > 120 ? c.Value[..120] + "…" : c.Value;
            _output.WriteLine($"  [{c.Key}] = \"{displayValue}\" (confidence: {c.Confidence:F2})");
        }
        _output.WriteLine("");
    }

    private static void AssertHasClaim(IReadOnlyList<ProviderClaim> claims, string key)
    {
        Assert.Contains(claims, c =>
            string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private async Task HashCoverArt(IReadOnlyList<ProviderClaim> claims)
    {
        var coverClaim = claims.FirstOrDefault(c =>
            string.Equals(c.Key, "cover", StringComparison.OrdinalIgnoreCase));

        if (coverClaim is null)
        {
            _output.WriteLine("  [cover art] No cover claim returned.");
            return;
        }

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "Tuvima Library/IntegrationTest");

            var bytes = await http.GetByteArrayAsync(coverClaim.Value);
            var hash = SHA256.HashData(bytes);
            var hexHash = Convert.ToHexString(hash).ToLowerInvariant();

            _output.WriteLine($"  [cover art] URL: {coverClaim.Value}");
            _output.WriteLine($"  [cover art] Size: {bytes.Length:N0} bytes");
            _output.WriteLine($"  [cover art] SHA-256: {hexHash}");
            // Image bytes discarded — not stored.
        }
        catch (Exception ex)
        {
            _output.WriteLine($"  [cover art] Download failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds an <see cref="IHttpClientFactory"/> with one or more named clients
    /// pointing at real API endpoints (no stubs). User-Agent header set for
    /// polite crawling.
    /// </summary>
    private static IHttpClientFactory BuildRealHttpFactory(params string[] clientNames)
    {
        var services = new ServiceCollection();
        foreach (var name in clientNames)
        {
            services.AddHttpClient(name, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.Add("User-Agent", "Tuvima Library/IntegrationTest (mailto:test@tuvima.dev)");
            });
        }
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IHttpClientFactory>();
    }
}

/// <summary>
/// Bridges xUnit's <see cref="ITestOutputHelper"/> to <see cref="ILoggerProvider"/>
/// so adapter-internal log messages are captured in test output.
/// </summary>
file sealed class XUnitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _output;
    public XUnitLoggerProvider(ITestOutputHelper output) => _output = output;
    public ILogger CreateLogger(string categoryName) => new XUnitLogger(_output, categoryName);
    public void Dispose() { }
}

file sealed class XUnitLogger : ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly string _category;
    public XUnitLogger(ITestOutputHelper output, string category)
    {
        _output = output;
        _category = category;
    }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try { _output.WriteLine($"  [{logLevel}] {_category}: {formatter(state, exception)}"); }
        catch { /* output might be disposed */ }
    }
}

/// <summary>
/// Minimal <see cref="IConfigurationLoader"/> for integration tests.
/// Returns null for universe config so the Wikidata adapter uses compiled defaults.
/// </summary>
file sealed class IntegrationConfigLoader : IConfigurationLoader
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

/// <summary>No-op QID label repository for integration tests.</summary>
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

/// <summary>No-op resolver cache for integration tests.</summary>
file sealed class NoOpResolverCacheRepository : IResolverCacheRepository
{
    public Task<ResolverCacheEntry?> FindAsync(string cacheKey, CancellationToken ct = default) => Task.FromResult<ResolverCacheEntry?>(null);
    public Task UpsertAsync(ResolverCacheEntry entry, CancellationToken ct = default) => Task.CompletedTask;
    public Task<int> PurgeExpiredAsync(CancellationToken ct = default) => Task.FromResult(0);
}

/// <summary>No-op provider response cache for integration tests.</summary>
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
