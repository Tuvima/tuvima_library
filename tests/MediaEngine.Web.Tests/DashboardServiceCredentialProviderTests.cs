using System.Net;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaEngine.Contracts.Authentication;
using MediaEngine.Web.Services.Integration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Web.Tests;

public sealed class DashboardServiceCredentialProviderTests : IDisposable
{
    private const string ProtectorPurpose = "Tuvima.DashboardEngineCredential.v1";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tuvima-dashboard-credential-{Guid.NewGuid():N}");

    [Fact]
    public async Task Handler_BlocksMissingCredentialWithoutSending_ThenRecoversAndReloadsRotation()
    {
        var logger = new CountingLogger<DashboardServiceCredentialProvider>();
        var protection = CreateProtectionProvider("keys");
        var provider = CreateProvider(protection, logger);
        var capture = new CapturingHandler();
        using var client = CreateClient(provider, capture);

        using var firstMissing = await client.GetAsync("/system/status");
        using var repeatedMissing = await client.GetAsync("/system/status");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, firstMissing.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, repeatedMissing.StatusCode);
        Assert.Equal(0, capture.SendCount);
        Assert.Equal(1, logger.WarningCount);

        WriteBundle(protection, "first-token");
        using var firstSuccess = await client.GetAsync("/system/status");

        Assert.Equal(HttpStatusCode.OK, firstSuccess.StatusCode);
        Assert.Equal(1, capture.SendCount);
        Assert.Equal("first-token", capture.LastServiceToken);

        WriteBundle(protection, "rotated-token");
        using var rotatedSuccess = await client.GetAsync("/system/status");

        Assert.Equal(HttpStatusCode.OK, rotatedSuccess.StatusCode);
        Assert.Equal(2, capture.SendCount);
        Assert.Equal("rotated-token", capture.LastServiceToken);
    }

    [Fact]
    public async Task Handler_BlocksMalformedOrWrongKeyBundle_AndDoesNotReuseCachedToken()
    {
        var logger = new CountingLogger<DashboardServiceCredentialProvider>();
        var protection = CreateProtectionProvider("keys");
        var provider = CreateProvider(protection, logger);
        var capture = new CapturingHandler();
        using var client = CreateClient(provider, capture);

        WriteBundle(protection, "valid-token");
        using var valid = await client.GetAsync("/system/status");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(1, capture.SendCount);

        Directory.CreateDirectory(Path.GetDirectoryName(CredentialPath)!);
        File.WriteAllText(CredentialPath, "{not-json");
        using var malformed = await client.GetAsync("/system/status");
        using var repeatedMalformed = await client.GetAsync("/system/status");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, repeatedMalformed.StatusCode);
        Assert.Equal(1, capture.SendCount);
        Assert.Equal(1, logger.WarningCount);

        File.WriteAllText(
            CredentialPath,
            JsonSerializer.Serialize(new { KeyId = "missing-token", CreatedAt = DateTimeOffset.UtcNow }));
        using var missingProperty = await client.GetAsync("/system/status");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, missingProperty.StatusCode);
        Assert.Equal(1, capture.SendCount);
        Assert.Equal(2, logger.WarningCount);

        var unrelatedProtection = CreateProtectionProvider("other-keys");
        WriteBundle(unrelatedProtection, "must-not-be-sent");
        using var wrongKey = await client.GetAsync("/system/status");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, wrongKey.StatusCode);
        Assert.Equal(1, capture.SendCount);
        Assert.Equal(3, logger.WarningCount);

        WriteBundle(protection, "recovered-token");
        using var recovered = await client.GetAsync("/system/status");

        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(2, capture.SendCount);
        Assert.Equal("recovered-token", capture.LastServiceToken);
    }

    [Fact]
    public void DashboardRegistration_DoesNotLoadCredentialWhileConstructingClients()
    {
        var program = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "MediaEngine.Web", "Program.cs"));

        Assert.DoesNotContain("ConfigureEngineClient(IServiceProvider", program, StringComparison.Ordinal);
        Assert.DoesNotContain(".GetToken()", program, StringComparison.Ordinal);
        Assert.Contains("AddHttpMessageHandler<DashboardServiceCredentialHandler>()", program, StringComparison.Ordinal);
        Assert.Contains("services.GetRequiredService<IActiveProfileAccessor>()));", program, StringComparison.Ordinal);
        Assert.Contains(
            ".AddHttpMessageHandler<DashboardEngineAuthenticationHandler>()\n    .AddHttpMessageHandler<ViewProfileAssertionHandler>()",
            program.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthenticationAndViewSignature_UseOneTrustedCredentialSnapshotDuringRotation()
    {
        var protection = CreateProtectionProvider("keys");
        var provider = CreateProvider(protection, new CountingLogger<DashboardServiceCredentialProvider>());
        WriteBundle(protection, "first-token");

        var profileId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var activeProfile = new ActiveProfileAccessor();
        activeProfile.SetProfile(profileId);
        var capture = new CapturingHandler();
        var assertion = new ViewProfileAssertionHandler(
            activeProfile,
            new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_750_000_000)))
        {
            InnerHandler = capture,
        };
        var rotate = new CallbackHandler(() => WriteBundle(protection, "rotated-token"))
        {
            InnerHandler = assertion,
        };
        var authentication = new DashboardEngineAuthenticationHandler(
            provider,
            new DashboardSessionAccessor(),
            new HttpContextAccessor())
        {
            InnerHandler = rotate,
        };
        using var client = new HttpClient(authentication) { BaseAddress = new Uri("http://engine.test") };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/view/scopes");
        request.Headers.TryAddWithoutValidation(DashboardServiceCredentialHandler.ServiceHeader, "caller-token");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("first-token", capture.LastServiceToken);
        Assert.Equal(
            ExpectedSignature("first-token", profileId, 1_750_000_000, "GET", "/view/scopes"),
            capture.LastViewSignature);

        using var nextResponse = await client.GetAsync("/view/scopes");
        Assert.Equal(HttpStatusCode.OK, nextResponse.StatusCode);
        Assert.Equal("rotated-token", capture.LastServiceToken);
        Assert.Equal(
            ExpectedSignature("rotated-token", profileId, 1_750_000_000, "GET", "/view/scopes"),
            capture.LastViewSignature);
    }

    [Fact]
    public async Task IdentityBootstrap_PreservesUnknownStateForUnavailableOrDownEngine()
    {
        using var unavailableClient = new HttpClient(new ResponseHandler(HttpStatusCode.ServiceUnavailable))
        {
            BaseAddress = new Uri("http://engine.test"),
        };
        var unavailableIdentity = new DashboardIdentityClient(new FixedHttpClientFactory(unavailableClient));

        Assert.Null(await unavailableIdentity.GetBootstrapStatusAsync());

        using var downClient = new HttpClient(new ThrowingHandler())
        {
            BaseAddress = new Uri("http://engine.test"),
        };
        var logger = new CountingLogger<DashboardIdentityClient>();
        var downIdentity = new DashboardIdentityClient(new FixedHttpClientFactory(downClient), logger: logger);

        Assert.Null(await downIdentity.GetBootstrapStatusAsync());
        Assert.Equal(1, logger.WarningCount);
    }

    [Fact]
    public async Task IdentityBootstrap_PreservesExplicitUnconfiguredState()
    {
        using var client = new HttpClient(new JsonResponseHandler("""{"administrator_configured":false}"""))
        {
            BaseAddress = new Uri("http://engine.test"),
        };
        var identity = new DashboardIdentityClient(new FixedHttpClientFactory(client));

        var result = await identity.GetBootstrapStatusAsync();

        Assert.NotNull(result);
        Assert.False(result.AdministratorConfigured);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CredentialPath => Path.Combine(_root, "config", ".secrets", "dashboard-engine.credential.json");

    private IDataProtectionProvider CreateProtectionProvider(string directoryName)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, directoryName));
        return DataProtectionProvider.Create(directory);
    }

    private DashboardServiceCredentialProvider CreateProvider(
        IDataProtectionProvider protection,
        ILogger<DashboardServiceCredentialProvider> logger) =>
        new(protection, new DashboardServiceCredentialProviderOptions(Path.Combine(_root, "config")), logger);

    private void WriteBundle(IDataProtectionProvider protection, string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CredentialPath)!);
        var bundle = new DashboardServiceCredentialBundle(
            Guid.NewGuid().ToString("N"),
            protection.CreateProtector(ProtectorPurpose).Protect(token),
            DateTimeOffset.UtcNow);
        File.WriteAllText(CredentialPath, JsonSerializer.Serialize(bundle));
    }

    private static HttpClient CreateClient(
        DashboardServiceCredentialProvider provider,
        CapturingHandler capture)
    {
        var handler = new DashboardEngineAuthenticationHandler(
            provider,
            new DashboardSessionAccessor(),
            new HttpContextAccessor())
        {
            InnerHandler = capture,
        };
        return new HttpClient(handler) { BaseAddress = new Uri("http://engine.test") };
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static string ExpectedSignature(
        string token,
        Guid profileId,
        long timestamp,
        string method,
        string target)
    {
        var canonical = string.Join(
            '\n',
            profileId.ToString("D"),
            timestamp.ToString(CultureInfo.InvariantCulture),
            method,
            target);
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(canonical));
        return Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }
        public string? LastServiceToken { get; private set; }
        public string? LastViewSignature { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            LastServiceToken = request.Headers.TryGetValues(DashboardServiceCredentialHandler.ServiceHeader, out var values)
                ? values.SingleOrDefault()
                : null;
            LastViewSignature = request.Headers.TryGetValues(ViewProfileAssertionHandler.SignatureHeader, out var signatures)
                ? signatures.SingleOrDefault()
                : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request });
        }
    }

    private sealed class CallbackHandler(Action callback) : DelegatingHandler
    {
        private bool _called;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!_called)
            {
                _called = true;
                callback();
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ResponseHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode) { RequestMessage = request });
    }

    private sealed class JsonResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Engine unavailable");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CountingLogger<T> : ILogger<T>
    {
        public int WarningCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningCount++;
            }
        }
    }
}
