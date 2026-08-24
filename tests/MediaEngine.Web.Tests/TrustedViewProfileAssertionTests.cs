using System.Net;
using MediaEngine.Web.Services.Integration;

namespace MediaEngine.Web.Tests;

public sealed class TrustedViewProfileAssertionTests
{
    private static readonly Guid ProfileId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public async Task Handler_SignsFinalViewRequestUsingDocumentedCanonicalFormat()
    {
        var accessor = new ActiveProfileAccessor();
        accessor.SetProfile(ProfileId);
        var capture = new CapturingHandler();
        var handler = new ViewProfileAssertionHandler(
            accessor,
            "test-api-key",
            new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_750_000_000)))
        {
            InnerHandler = capture,
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://engine.test") };
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        await client.GetAsync("/view/libraries?profileId=legacy%20value&take=20");

        Assert.NotNull(capture.Request);
        Assert.Equal("test-api-key", Header(capture.Request, "X-Api-Key"));
        Assert.Equal(ProfileId.ToString("D"), Header(capture.Request, ViewProfileAssertionHandler.ProfileHeader));
        Assert.Equal("1750000000", Header(capture.Request, ViewProfileAssertionHandler.TimestampHeader));
        Assert.Equal(
            "tNfYND8CNiJF3X8YoBWg_FwJu_O-pDIEoIHMG1LFnKY",
            Header(capture.Request, ViewProfileAssertionHandler.SignatureHeader));
    }

    [Fact]
    public async Task Handler_AddsProfileOnly_WhenLocalEngineHasNoApiKey()
    {
        var accessor = new ActiveProfileAccessor();
        accessor.SetProfile(ProfileId);
        var capture = new CapturingHandler();
        var handler = new ViewProfileAssertionHandler(accessor, string.Empty)
        {
            InnerHandler = capture,
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:61495") };

        await client.GetAsync("/view/scopes");

        Assert.Equal(ProfileId.ToString("D"), Header(capture.Request!, ViewProfileAssertionHandler.ProfileHeader));
        Assert.Null(Header(capture.Request!, ViewProfileAssertionHandler.TimestampHeader));
        Assert.Null(Header(capture.Request!, ViewProfileAssertionHandler.SignatureHeader));
    }

    [Theory]
    [InlineData("/profiles", true, true)]
    [InlineData("/viewfinder", true, true)]
    [InlineData("/view/libraries", false, true)]
    [InlineData("/view/libraries", true, false)]
    [InlineData("/collections/catalog", true, false)]
    [InlineData("/collections-preview", true, true)]
    public async Task Handler_OnlyAssertsViewRequestsWhenProfileIsAvailable(
        string path,
        bool setProfile,
        bool expectAssertionMissing)
    {
        var accessor = new ActiveProfileAccessor();
        if (setProfile)
        {
            accessor.SetProfile(ProfileId);
        }
        var capture = new CapturingHandler();
        var handler = new ViewProfileAssertionHandler(accessor, "test-api-key")
        {
            InnerHandler = capture,
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://engine.test") };

        await client.GetAsync(path);

        Assert.Equal(
            expectAssertionMissing,
            !capture.Request!.Headers.Contains(ViewProfileAssertionHandler.SignatureHeader));
    }

    [Fact]
    public void BrowserViewComponents_DoNotReceiveTheSigningKey()
    {
        var root = FindRepoRoot();
        var viewPage = File.ReadAllText(Path.Combine(
            root,
            "src",
            "MediaEngine.Web",
            "Components",
            "Pages",
            "ViewPage.razor"));

        Assert.DoesNotContain("Engine:ApiKey", viewPage, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Api-Key", viewPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewProfileAssertionHandler", viewPage, StringComparison.Ordinal);
    }

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
