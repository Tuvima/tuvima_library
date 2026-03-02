using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tanaste.Domain.Enums;
using Tanaste.Providers.Adapters;
using Tanaste.Providers.Models;
using Tanaste.Storage.Contracts;
using Tanaste.Storage.Models;

namespace Tanaste.Providers.Tests;

/// <summary>
/// Verifies that all three external metadata adapters degrade gracefully:
/// they return an empty claim list on network failure rather than throwing.
///
/// Each test uses a <see cref="StubHttpMessageHandler"/> that injects a
/// predetermined response (error status, empty body, or timeout) without
/// touching the network.
///
/// Spec: Phase 9 – External Metadata Adapters § Graceful Failure.
/// </summary>
public sealed class AdapterFallbackTests
{
    // ── Apple Books — HTTP 503 ────────────────────────────────────────────────

    [Fact]
    public async Task AppleBooks_Returns_Empty_On_HttpError()
    {
        // Arrange: every request returns HTTP 503.
        var factory = BuildFactory("apple_books", HttpStatusCode.ServiceUnavailable);
        var adapter = new AppleBooksAdapter(
            factory,
            MediaType.Epub,
            NullLogger<AppleBooksAdapter>.Instance);

        var request = new ProviderLookupRequest
        {
            EntityId   = Guid.NewGuid(),
            EntityType = EntityType.MediaAsset,
            MediaType  = MediaType.Epub,
            Title      = "Dune",
            Author     = "Frank Herbert",
            BaseUrl    = "https://itunes.apple.com",
        };

        // Act
        var claims = await adapter.FetchAsync(request);

        // Assert: empty list, no exception.
        Assert.Empty(claims);
    }

    // ── Audnexus — missing ASIN ───────────────────────────────────────────────

    [Fact]
    public async Task Audnexus_Returns_Empty_When_No_Asin()
    {
        // Arrange: handler counts calls so we can assert zero HTTP requests were made.
        var callCount = 0;
        var factory   = BuildFactory("audnexus", HttpStatusCode.OK,
            onRequest: _ => callCount++);

        var adapter = new AudnexusAdapter(
            factory,
            NullLogger<AudnexusAdapter>.Instance);

        var request = new ProviderLookupRequest
        {
            EntityId   = Guid.NewGuid(),
            EntityType = EntityType.MediaAsset,
            MediaType  = MediaType.Audiobook,
            Title      = "Project Hail Mary",
            Asin       = null, // <── No ASIN; adapter must skip immediately.
            BaseUrl    = "https://api.audnexus.com",
        };

        // Act
        var claims = await adapter.FetchAsync(request);

        // Assert: empty list AND zero HTTP calls (short-circuit, not graceful failure).
        Assert.Empty(claims);
        Assert.Equal(0, callCount);
    }

    // ── Wikidata — TaskCanceledException (simulated timeout) ─────────────────

    [Fact]
    public async Task Wikidata_Returns_Empty_On_Timeout()
    {
        // Arrange: handler throws TaskCanceledException to simulate a timeout.
        var factory = BuildTimeoutFactory("wikidata_api");
        var adapter = new WikidataAdapter(
            factory,
            new StubConfigurationLoader(),
            NullLogger<WikidataAdapter>.Instance);

        var request = new ProviderLookupRequest
        {
            EntityId   = Guid.NewGuid(),
            EntityType = EntityType.Person,
            MediaType  = MediaType.Unknown,
            PersonName = "Frank Herbert",
            PersonRole = "Author",
            BaseUrl    = "https://www.wikidata.org/w/api.php",
        };

        // Act
        var claims = await adapter.FetchAsync(request);

        // Assert: graceful empty list despite timeout.
        Assert.Empty(claims);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
    public ProviderConfiguration? LoadProvider(string name) => null;
    public void SaveProvider(ProviderConfiguration config) { }
    public IReadOnlyList<ProviderConfiguration> LoadAllProviders() => [];
    public T? LoadConfig<T>(string subdirectory, string name) where T : class => null;
    public void SaveConfig<T>(string subdirectory, string name, T config) where T : class { }
}
