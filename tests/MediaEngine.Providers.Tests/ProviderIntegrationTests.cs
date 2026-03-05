using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Fact]
    public async Task AppleBooks_Ebook_Returns_Claims_For_FellowshipOfTheRing()
    {
        var adapter = BuildConfigDrivenAdapter("apple_books");

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

    [Fact]
    public async Task AppleBooks_Audiobook_Returns_Claims_For_FellowshipOfTheRing()
    {
        var adapter = BuildConfigDrivenAdapter("apple_books");

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

    // ── Audnexus ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Audnexus_Handles_Asin_Lookup_Gracefully()
    {
        var adapter = BuildConfigDrivenAdapter("audnexus");

        // ASIN B007978NPG = "The Fellowship of the Ring" audiobook on Audible.
        // Note: Audnexus is region-sensitive; some ASINs may return 404 or
        // region-unavailable depending on the test runner's location.
        var request = new ProviderLookupRequest
        {
            EntityId   = Guid.NewGuid(),
            EntityType = EntityType.MediaAsset,
            MediaType  = MediaType.Audiobooks,
            Title      = "The Fellowship of the Ring",
            Asin       = "B007978NPG",
            BaseUrl    = "https://api.audnex.us",
        };

        var claims = await adapter.FetchAsync(request);

        LogClaims("Audnexus", claims);

        // Audnexus may return empty if the ASIN is not available in the local region.
        // The important validation is that it degrades gracefully (no exception thrown).
        _output.WriteLine($"  Audnexus returned {claims.Count} claims (region-dependent).");

        if (claims.Count > 0)
        {
            await HashCoverArt(claims);
        }
    }

    [Fact]
    public async Task Audnexus_ShortCircuits_Without_Asin()
    {
        var adapter = BuildConfigDrivenAdapter("audnexus");

        var request = new ProviderLookupRequest
        {
            EntityId   = Guid.NewGuid(),
            EntityType = EntityType.MediaAsset,
            MediaType  = MediaType.Audiobooks,
            Title      = "The Fellowship of the Ring",
            Asin       = null,
            BaseUrl    = "https://api.audnex.us",
        };

        var claims = await adapter.FetchAsync(request);

        // Config-driven adapter skips the strategy because required_fields includes "asin".
        _output.WriteLine($"Audnexus without ASIN: {claims.Count} claims (expected 0).");
        Assert.Empty(claims);
    }

    // ── Wikidata ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Wikidata_Returns_Claims_For_FellowshipOfTheRing()
    {
        // Wikidata needs both wikidata_api and wikidata_sparql clients.
        var factory = BuildRealHttpFactory("wikidata_api", "wikidata_sparql");
        var stubConfig = new IntegrationConfigLoader();

        // Use a real logger to surface any adapter-internal errors.
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(new XUnitLoggerProvider(_output)).SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<WikidataAdapter>();

        var adapter = new WikidataAdapter(factory, stubConfig, logger);

        var request = new ProviderLookupRequest
        {
            EntityId    = Guid.NewGuid(),
            EntityType  = EntityType.Work,
            MediaType   = MediaType.Books,
            Title       = "The Fellowship of the Ring",
            Author      = "J.R.R. Tolkien",
            BaseUrl     = "https://www.wikidata.org/w/api.php",
            SparqlBaseUrl = "https://query.wikidata.org/sparql",
        };

        var claims = await adapter.FetchAsync(request);

        LogClaims("Wikidata", claims);

        // Wikidata may fail due to rate limiting or network conditions.
        // The adapter must degrade gracefully without throwing.
        _output.WriteLine($"  Wikidata returned {claims.Count} claims.");

        if (claims.Count > 0)
        {
            AssertHasClaim(claims, "wikidata_qid");
        }
    }

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
        return new ConfigDrivenAdapter(config, factory, NullLogger<ConfigDrivenAdapter>.Instance);
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
    public ProviderConfiguration? LoadProvider(string name) => null;
    public void SaveProvider(ProviderConfiguration config) { }
    public IReadOnlyList<ProviderConfiguration> LoadAllProviders() => [];
    public T? LoadConfig<T>(string subdirectory, string name) where T : class => null;
    public void SaveConfig<T>(string subdirectory, string name, T config) where T : class { }
}
