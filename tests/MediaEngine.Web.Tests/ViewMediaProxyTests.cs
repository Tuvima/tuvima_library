using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MediaEngine.Web.Services.Integration;
using Microsoft.AspNetCore.Http;

namespace MediaEngine.Web.Tests;

public sealed class ViewMediaProxyTests
{
    private static readonly byte[] GrantKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private static readonly Guid ProfileId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid LibraryId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid AssetId = Guid.Parse("40000000-0000-0000-0000-000000000004");

    [Fact]
    public void Dashboard_MapsGrantOnlyProxyAndKeepsLegacyEnginePathServerSide()
    {
        var root = FindRepoRoot();
        var program = File.ReadAllText(Path.Combine(root, "src", "MediaEngine.Web", "Program.cs"));
        var page = File.ReadAllText(Path.Combine(root, "src", "MediaEngine.Web", "Components", "Pages", "ViewPage.razor"));
        var engineClient = File.ReadAllText(Path.Combine(root, "src", "MediaEngine.Web", "Services", "Integration", "ViewMediaEngineClient.cs"));

        Assert.Contains("app.MapViewMediaProxy()", program, StringComparison.Ordinal);
        Assert.Contains("/view-media/{grant.Value}", page, StringComparison.Ordinal);
        Assert.DoesNotContain("profileId=", page, StringComparison.Ordinal);
        Assert.DoesNotContain("items/{item", page, StringComparison.Ordinal);
        Assert.Contains("/view/{grant.LibraryId:D}/items/{grant.AssetId:D}/{resource}?profileId={grant.ProfileId:D}", engineClient, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EngineProxyClient_SendsLegacyPathThroughProfileAssertionHandler()
    {
        var activeProfile = new ActiveProfileAccessor();
        activeProfile.SetProfile(ProfileId);
        var capture = new CapturingHttpHandler();
        var assertion = new ViewProfileAssertionHandler(activeProfile, "server-api-key")
        {
            InnerHandler = capture,
        };
        using var http = new HttpClient(assertion) { BaseAddress = new Uri("http://engine.test") };
        http.DefaultRequestHeaders.Add("X-Api-Key", "server-api-key");
        using var engine = new ViewMediaEngineClient(http);
        var grant = new ViewMediaGrant(
            ProfileId,
            LibraryId,
            AssetId,
            ViewMediaResourceKind.Content,
            ViewMediaResourceRole.Primary,
            DateTimeOffset.UtcNow.AddMinutes(5));

        using var response = await engine.SendAsync(
            grant,
            HttpMethod.Get,
            "bytes=0-99",
            null,
            CancellationToken.None);

        Assert.Equal(
            $"http://engine.test/view/{LibraryId:D}/items/{AssetId:D}/content?profileId={ProfileId:D}",
            capture.Request?.RequestUri?.AbsoluteUri);
        Assert.Equal("bytes=0-99", capture.Request?.Headers.Range?.ToString());
        Assert.True(capture.Request?.Headers.Contains(ViewProfileAssertionHandler.SignatureHeader));
        Assert.Equal(ProfileId.ToString("D"), capture.Request?.Headers.GetValues(ViewProfileAssertionHandler.ProfileHeader).Single());
    }

    [Fact]
    public void Grant_RoundTripsExactProfileResourceAndRole()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_750_000_000));
        var grants = new ViewMediaGrantService(GrantKey, TimeSpan.FromMinutes(5), clock);

        var token = grants.Create(
            ProfileId,
            LibraryId,
            AssetId,
            ViewMediaResourceKind.Content,
            ViewMediaResourceRole.Primary);

        Assert.True(grants.TryValidate(token.Value, out var grant));
        Assert.Equal(ProfileId, grant!.ProfileId);
        Assert.Equal(LibraryId, grant.LibraryId);
        Assert.Equal(AssetId, grant.AssetId);
        Assert.Equal(ViewMediaResourceKind.Content, grant.ResourceKind);
        Assert.Equal(ViewMediaResourceRole.Primary, grant.ResourceRole);
        Assert.Equal(token.ExpiresAt, grant.ExpiresAt);
    }

    [Fact]
    public void Grant_RejectsTamperingIncludingCrossProfileSubstitution()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_750_000_000));
        var grants = new ViewMediaGrantService(GrantKey, TimeSpan.FromMinutes(5), clock);
        var token = grants.Create(ProfileId, LibraryId, AssetId, ViewMediaResourceKind.Thumbnail).Value;
        var parts = token.Split('.');
        var payload = Decode(parts[0]);
        OtherProfileId.TryWriteBytes(payload.AsSpan(1, 16), bigEndian: true, out _);
        var substituted = $"{Encode(payload)}.{parts[1]}";

        Assert.False(grants.TryValidate(substituted, out _));

        var signature = Decode(parts[1]);
        signature[^1] ^= 0x01;
        Assert.False(grants.TryValidate($"{parts[0]}.{Encode(signature)}", out _));
    }

    [Fact]
    public void Grant_RejectsExpiredToken()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_750_000_000));
        var grants = new ViewMediaGrantService(GrantKey, TimeSpan.FromSeconds(30), clock);
        var token = grants.Create(ProfileId, LibraryId, AssetId, ViewMediaResourceKind.Content);

        clock.Advance(TimeSpan.FromSeconds(31));

        Assert.False(grants.TryValidate(token.Value, out _));
    }

    [Fact]
    public async Task Proxy_UsesGrantProfileForSignedEngineFetchAndForwardsRange()
    {
        var grants = CreateGrantService();
        var token = grants.Create(ProfileId, LibraryId, AssetId, ViewMediaResourceKind.Content);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.QueryString = new QueryString($"?profileId={OtherProfileId:D}");
        context.Request.Headers.Range = "bytes=10-19";
        context.Request.Headers.IfRange = "\"asset-etag\"";
        context.Response.Body = new MemoryStream();
        var activeProfile = new ActiveProfileAccessor();
        activeProfile.SetProfile(OtherProfileId);
        var engine = new StubViewMediaEngineClient();

        await ViewMediaProxyEndpoint.HandleAsync(
            token.Value,
            context,
            grants,
            activeProfile,
            engine,
            CancellationToken.None);

        Assert.Equal(ProfileId, activeProfile.ProfileId);
        Assert.Equal(ProfileId, engine.Grant?.ProfileId);
        Assert.Equal(LibraryId, engine.Grant?.LibraryId);
        Assert.Equal(AssetId, engine.Grant?.AssetId);
        Assert.Equal(HttpMethod.Get, engine.Method);
        Assert.Equal("bytes=10-19", engine.Range);
        Assert.Equal("\"asset-etag\"", engine.IfRange);
        Assert.Equal(StatusCodes.Status206PartialContent, context.Response.StatusCode);
        Assert.Equal("private, no-store", context.Response.Headers.CacheControl);
        Assert.Equal("no-cache", context.Response.Headers.Pragma);
        Assert.Equal("bytes 10-19/100", context.Response.Headers.ContentRange);
        Assert.Equal("personal bytes", Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }

    [Fact]
    public async Task Proxy_RejectsTamperedGrantBeforeEngineCall()
    {
        var grants = CreateGrantService();
        var token = grants.Create(ProfileId, LibraryId, AssetId, ViewMediaResourceKind.Thumbnail).Value;
        var tampered = $"{token[..^1]}{(token[^1] == 'A' ? 'B' : 'A')}";
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        var engine = new StubViewMediaEngineClient();

        await ViewMediaProxyEndpoint.HandleAsync(
            tampered,
            context,
            grants,
            new ActiveProfileAccessor(),
            engine,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal("private, no-store", context.Response.Headers.CacheControl);
        Assert.Equal(0, engine.CallCount);
    }

    [Fact]
    public async Task Proxy_ForwardsHeadWithoutWritingBody()
    {
        var grants = CreateGrantService();
        var token = grants.Create(ProfileId, LibraryId, AssetId, ViewMediaResourceKind.Thumbnail);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Head;
        context.Response.Body = new MemoryStream();
        var engine = new StubViewMediaEngineClient();

        await ViewMediaProxyEndpoint.HandleAsync(
            token.Value,
            context,
            grants,
            new ActiveProfileAccessor(),
            engine,
            CancellationToken.None);

        Assert.Equal(HttpMethod.Head, engine.Method);
        Assert.Empty(((MemoryStream)context.Response.Body).ToArray());
    }

    private static ViewMediaGrantService CreateGrantService() =>
        new(GrantKey, TimeSpan.FromMinutes(5),
            new MutableTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_750_000_000)));

    private static byte[] Decode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(padded);
    }

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }

    private sealed class StubViewMediaEngineClient : IViewMediaEngineClient
    {
        public int CallCount { get; private set; }
        public ViewMediaGrant? Grant { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Range { get; private set; }
        public string? IfRange { get; private set; }

        public Task<HttpResponseMessage> SendAsync(
            ViewMediaGrant grant,
            HttpMethod method,
            string? range,
            string? ifRange,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Grant = grant;
            Method = method;
            Range = range;
            IfRange = ifRange;
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("personal bytes")),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(10, 19, 100);
            return Task.FromResult(response);
        }
    }

    private sealed class CapturingHttpHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([]),
            });
        }
    }
}
