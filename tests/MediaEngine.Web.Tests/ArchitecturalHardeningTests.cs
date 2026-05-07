using System.Net;
using System.Reflection;
using System.Text;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using MediaEngine.Web.Services.Playback;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Web.Tests;

public sealed class ArchitecturalHardeningTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void IntegrationClients_AreRegisteredWithConfiguredEngineHttpClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<MediaEngine.Web.Services.Branding.StreamingServiceLogoResolver>();
        services.AddHttpClient<IEngineApiClient, EngineApiClient>(client =>
        {
            client.BaseAddress = new Uri("http://engine.test");
            client.DefaultRequestHeaders.Add("X-Api-Key", "secret");
        });

        using var provider = services.BuildServiceProvider();
        var client = Assert.IsType<EngineApiClient>(provider.GetRequiredService<IEngineApiClient>());

        Assert.Equal("http://engine.test/cover", client.ToAbsoluteEngineUrl("/cover"));
    }

    [Fact]
    public async Task EngineApiClient_DelegatesSystemStatusAndPreservesFailureSnapshot()
    {
        using var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Engine warming up", Encoding.UTF8, "text/plain"),
            });
        var client = new EngineApiClient(httpClient, NullLogger<EngineApiClient>.Instance);

        var status = await client.GetSystemStatusAsync();

        Assert.Null(status);
        Assert.Equal("GET /system/status", client.LastFailedEndpoint);
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, client.LastStatusCode);
        Assert.Equal("http_failure", client.LastFailureKind);
        Assert.Contains("Engine warming up", client.LastError);
    }

    [Fact]
    public async Task EngineApiClient_DelegatesProviderCatalogue()
    {
        const string json = """
            [
              { "providerId": "open-library", "name": "open_library", "displayName": "Open Library", "enabled": true }
            ]
            """;
        using var httpClient = CreateHttpClient(request =>
        {
            Assert.Equal("http://localhost:61495/providers/catalogue", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });
        var client = new EngineApiClient(httpClient, NullLogger<EngineApiClient>.Instance);

        var catalogue = await client.GetProviderCatalogueAsync();

        var provider = Assert.Single(catalogue);
        Assert.Equal("open_library", provider.Name);
        Assert.Equal("Open Library", provider.DisplayName);
    }

    [Fact]
    public async Task ProviderCatalogueService_UsesMemoryCacheAndInvalidates()
    {
        var api = CountingEngineApiClient.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new ProviderCatalogueService(api.Client, cache);

        var first = await service.GetCatalogueAsync();
        var second = await service.GetCatalogueAsync();
        service.Invalidate();
        var third = await service.GetCatalogueAsync();

        Assert.Same(first, second);
        Assert.NotSame(first, third);
        Assert.Equal(2, api.CatalogueCalls);
    }

    [Fact]
    public void ListenPageState_TracksDefaultsAndSelectionTransitions()
    {
        var state = new ListenPageState();
        var trackId = Guid.NewGuid();

        Assert.Equal("music", state.ActiveMode);
        Assert.True(state.Loading);

        state.SetMode("audiobooks");
        state.SetSearch("dune");
        state.SetSort("title", descending: false);
        state.ReplaceTrackSelection([trackId]);
        state.SetError("failed");

        Assert.Equal("audiobooks", state.ActiveMode);
        Assert.Equal("dune", state.Search);
        Assert.Equal("title", state.SortColumn);
        Assert.False(state.SortDescending);
        Assert.Contains(trackId, state.SelectedTrackIds);
        Assert.False(state.Loading);
        Assert.Equal("failed", state.Error);
    }

    [Fact]
    public void PerformanceSourceGuardrails_RemainInPlace()
    {
        var drawer = File.ReadAllText(Path.Combine(RepoRoot, "src/MediaEngine.Web/Components/Library/LibraryDetailDrawer.razor"));
        var appJs = File.ReadAllText(Path.Combine(RepoRoot, "src/MediaEngine.Web/wwwroot/app.js"));
        var universeTab = File.ReadAllText(Path.Combine(RepoRoot, "src/MediaEngine.Web/Components/Details/UniverseTab.razor"));

        Assert.DoesNotContain("<style>", drawer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LibraryDetailDrawerHeader", drawer);
        Assert.Contains("LibraryDetailDrawerStatusBanners", drawer);
        Assert.Contains("__discoveryHoverFrame", appJs);
        Assert.Contains("cancelAnimationFrame", appJs);
        Assert.Contains("ShouldRender", universeTab);
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new StubHttpMessageHandler(responder))
        {
            BaseAddress = new Uri("http://localhost:61495"),
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private class CountingEngineApiClient : DispatchProxy
    {
        public int CatalogueCalls { get; private set; }
        public IEngineApiClient Client => (IEngineApiClient)(object)this;

        public static CountingEngineApiClient Create()
        {
            var proxy = DispatchProxy.Create<IEngineApiClient, CountingEngineApiClient>();
            return (CountingEngineApiClient)(object)proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IEngineApiClient.GetProviderCatalogueAsync))
            {
                CatalogueCalls++;
                return Task.FromResult<IReadOnlyList<ProviderCatalogueDto>>(
                [
                    new ProviderCatalogueDto
                    {
                        Name = $"provider_{CatalogueCalls}",
                        DisplayName = $"Provider {CatalogueCalls}",
                    },
                ]);
            }

            if (targetMethod?.ReturnType == typeof(string))
            {
                return string.Empty;
            }

            if (targetMethod?.ReturnType == typeof(string))
            {
                return null;
            }

            if (targetMethod?.ReturnType.IsGenericType == true && targetMethod.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = targetMethod.ReturnType.GetGenericArguments()[0];
                var method = typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType);
                return method.Invoke(null, [resultType.IsValueType ? Activator.CreateInstance(resultType) : null]);
            }

            return null;
        }
    }
}
