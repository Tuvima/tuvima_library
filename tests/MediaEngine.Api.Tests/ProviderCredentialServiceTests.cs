using System.Net;
using System.Text.Json;
using MediaEngine.Api.Services;
using MediaEngine.Domain;
using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Enums;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Storage;

namespace MediaEngine.Api.Tests;

public sealed class ProviderCredentialServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "tuvima-provider-credentials",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TestAsync_RejectsInvalidFormatWithoutCallingProvider()
    {
        using var loader = CreateLoader();
        var handler = new StubHandler(HttpStatusCode.OK);
        var service = CreateService(loader, handler);

        var result = await service.TestAsync(
            "contract_provider",
            new Dictionary<string, string> { ["api_key"] = "not-a-key" });

        Assert.False(result.Success);
        Assert.Equal("invalid_format", result.Status);
        Assert.Contains("api_key", result.FieldErrors);
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "invalid_credential")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "provider_outage")]
    [InlineData(HttpStatusCode.UnavailableForLegalReasons, "region_restricted")]
    public async Task TestAsync_ClassifiesProviderFailures(HttpStatusCode statusCode, string expected)
    {
        using var loader = CreateLoader();
        var service = CreateService(loader, new StubHandler(statusCode));

        var result = await service.TestAsync("contract_provider", ValidCredentials('a'));

        Assert.False(result.Success);
        Assert.Equal(expected, result.Status);
        Assert.DoesNotContain(new string('a', 32), JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestAsync_AppendsReadOnlyProbeBeneathVersionedApiPath()
    {
        using var loader = CreateLoader();
        var handler = new StubHandler(HttpStatusCode.OK);
        var service = CreateService(loader, handler);

        var result = await service.TestAsync("contract_provider", ValidCredentials('a'));

        Assert.True(result.Success);
        Assert.Equal(
            "https://provider.example/api/configuration?api_key=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            handler.LastRequestUri?.AbsoluteUri);
        Assert.Equal(HttpMethod.Get, handler.LastMethod);
    }

    [Fact]
    public async Task SaveRotateAndRemove_AreAtomicAndRefreshRuntimeConsumer()
    {
        using var loader = CreateLoader();
        var consumer = new CredentialConsumer("contract_provider");
        var service = CreateService(loader, new StubHandler(HttpStatusCode.OK), consumer);
        var first = new string('a', 32);
        var second = new string('b', 32);

        var saved = await service.SaveAsync("contract_provider", ValidCredentials('a'));
        var secretPath = Path.Combine(_root, "secrets", "contract_provider.json");

        Assert.True(saved.Success);
        Assert.True(File.Exists(secretPath));
        Assert.False(File.Exists(secretPath + ".bak"));
        Assert.Equal(first, consumer.LastCredentials["api_key"]);
        Assert.DoesNotContain(first, JsonSerializer.Serialize(saved), StringComparison.Ordinal);

        var rotated = await service.SaveAsync("contract_provider", ValidCredentials('b'));

        Assert.True(rotated.Success);
        Assert.Contains(second, File.ReadAllText(secretPath), StringComparison.Ordinal);
        Assert.DoesNotContain(first, File.ReadAllText(secretPath), StringComparison.Ordinal);
        Assert.False(File.Exists(secretPath + ".bak"));
        Assert.Equal(second, consumer.LastCredentials["api_key"]);

        var removed = service.Remove("contract_provider");

        Assert.True(removed.Success);
        Assert.Equal("removed", removed.Status);
        Assert.False(File.Exists(secretPath));
        Assert.Null(consumer.LastCredentials.GetValueOrDefault("api_key"));
    }

    [Fact]
    public async Task FailedRotation_PreservesExistingCredential()
    {
        using var loader = CreateLoader();
        var handler = new StubHandler(HttpStatusCode.OK);
        var service = CreateService(loader, handler);
        await service.SaveAsync("contract_provider", ValidCredentials('a'));
        handler.StatusCode = HttpStatusCode.Unauthorized;

        var result = await service.SaveAsync("contract_provider", ValidCredentials('b'));
        var secretPath = Path.Combine(_root, "secrets", "contract_provider.json");

        Assert.False(result.Success);
        Assert.Contains(new string('a', 32), File.ReadAllText(secretPath), StringComparison.Ordinal);
        Assert.DoesNotContain(new string('b', 32), File.ReadAllText(secretPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Loader_CreatesSecretsDirectoryWhenExistingConfigRootIsEmpty()
    {
        Directory.CreateDirectory(_root);

        using var loader = new ConfigurationDirectoryLoader(_root);

        Assert.True(Directory.Exists(Path.Combine(_root, "secrets")));
    }

    private ConfigurationDirectoryLoader CreateLoader()
    {
        Directory.CreateDirectory(_root);
        var loader = new ConfigurationDirectoryLoader(_root);
        loader.SaveProvider(new ProviderConfiguration
        {
            Name = "contract_provider",
            DisplayName = "Contract Provider",
            ProviderId = "11111111-2222-3333-4444-555555555555",
            Enabled = true,
            RequiresApiKey = true,
            Endpoints = new Dictionary<string, string> { ["api"] = "https://provider.example/api" },
            HttpClient = new HttpClientConfig
            {
                ApiKeyDelivery = "query",
                ApiKeyParamName = "api_key",
            },
            Onboarding = new ProviderOnboardingConfiguration
            {
                Classification = "recommended",
                SupportedLanes = ["watch"],
                Credentials =
                [
                    new ProviderCredentialFieldConfiguration
                    {
                        Key = "api_key",
                        Label = "Provider API key",
                        Required = true,
                        MinimumLength = 32,
                        MaximumLength = 32,
                        ValidationPattern = "^[a-f0-9]{32}$",
                    },
                ],
                AuthenticationProbe = new ProviderAuthenticationProbeConfiguration
                {
                    Path = "/configuration",
                    Method = "GET",
                    SuccessStatusCodes = [200],
                },
            },
        });
        return loader;
    }

    private static ProviderCredentialService CreateService(
        ConfigurationDirectoryLoader loader,
        HttpMessageHandler handler,
        params IExternalMetadataProvider[] providers) => new(
            loader,
            new StubHttpClientFactory(handler),
            providers,
            []);

    private static Dictionary<string, string> ValidCredentials(char value) =>
        new(StringComparer.OrdinalIgnoreCase) { ["api_key"] = new string(value, 32) };

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = statusCode;
        public int RequestCount { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(StatusCode));
        }
    }

    private sealed class CredentialConsumer(string name) : IExternalMetadataProvider, IProviderCredentialConsumer
    {
        public string Name { get; } = name;
        public ProviderDomain Domain => ProviderDomain.Universal;
        public IReadOnlyList<string> CapabilityTags => [];
        public Guid ProviderId { get; } = Guid.NewGuid();
        public Dictionary<string, string?> LastCredentials { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool CanHandle(MediaType mediaType) => true;
        public bool CanHandle(EntityType entityType) => true;
        public Task<IReadOnlyList<ProviderClaim>> FetchAsync(ProviderLookupRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProviderClaim>>([]);
        public void ApplyCredentials(IReadOnlyDictionary<string, string?> credentials) =>
            LastCredentials = new Dictionary<string, string?>(credentials, StringComparer.OrdinalIgnoreCase);
    }
}
