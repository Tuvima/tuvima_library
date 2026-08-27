using System.Net;
using System.Security.Cryptography;
using System.Text;
using MediaEngine.Api.Middleware;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services;
using MediaEngine.Api.Services.View;
using MediaEngine.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace MediaEngine.Api.Tests;

public sealed class ViewProfileAssertionMiddlewareTests
{
    private const string RawKey = "test-raw-api-key-never-log";

    [Fact]
    public async Task ValidApiKeyAssertionPopulatesTrustedViewProfile()
    {
        var profileId = Guid.NewGuid();
        var context = Request("GET", "/view/assets", "?scope=mine");
        context.Request.Headers["X-Api-Key"] = RawKey;
        Sign(context.Request, profileId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        ViewRequestProfile? captured = null;
        var middleware = new ApiKeyMiddleware(ctx =>
        {
            captured = new HttpViewRequestProfileContext(
                new HttpContextAccessor { HttpContext = ctx }).Current;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, Cache(), Configuration());

        Assert.NotNull(captured);
        Assert.Equal(profileId, captured.ProfileId);
        Assert.Equal("RestrictedProfile", captured.Role);
    }

    [Fact]
    public async Task ValidAssertionAlsoPopulatesCollectionsPersonalMediaContext()
    {
        var profileId = Guid.NewGuid();
        var context = Request("GET", "/collections", "?include=view");
        context.Request.Headers["X-Api-Key"] = RawKey;
        Sign(context.Request, profileId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        ViewRequestProfile? captured = null;
        var middleware = new ApiKeyMiddleware(ctx =>
        {
            captured = new HttpViewRequestProfileContext(
                new HttpContextAccessor { HttpContext = ctx }).Current;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, Cache(), Configuration());

        Assert.Equal(profileId, captured!.ProfileId);
    }

    [Fact]
    public async Task UnsignedProfileHeaderWithValidApiKeyIsNotTrusted()
    {
        var context = Request("GET", "/view/assets");
        context.Request.Headers["X-Api-Key"] = RawKey;
        context.Request.Headers[ViewProfileAssertion.ProfileHeader] = Guid.NewGuid().ToString();
        ViewRequestProfile? captured = new(Guid.NewGuid(), "sentinel");
        var middleware = new ApiKeyMiddleware(ctx =>
        {
            captured = new HttpViewRequestProfileContext(
                new HttpContextAccessor { HttpContext = ctx }).Current;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, Cache(), Configuration());

        Assert.Null(captured);
        Assert.Equal("RestrictedProfile", context.Items["ApiKeyRole"]);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("query-tampered")]
    [InlineData("path-tampered")]
    [InlineData("signature-tampered")]
    public void InvalidAssertionIsRejectedWithoutTrustingItsProfile(string failure)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var profileId = Guid.NewGuid();
        var request = Request("GET", "/view/assets", "?scope=mine").Request;
        var timestamp = failure == "stale"
            ? now.AddMinutes(-10).ToUnixTimeSeconds()
            : now.ToUnixTimeSeconds();
        Sign(request, profileId, timestamp);
        if (failure == "query-tampered")
        {
            request.QueryString = new QueryString("?scope=shared");
        }
        else if (failure == "path-tampered")
        {
            request.Path = "/view/other-assets";
        }
        else if (failure == "signature-tampered")
        {
            request.Headers[ViewProfileAssertion.SignatureHeader] = "AAAA";
        }

        var result = ViewProfileAssertion.Verify(request, RawKey, "RestrictedProfile", now);

        Assert.Null(result);
    }

    [Fact]
    public async Task AssertionHeadersNeverPopulateContextOnUnrelatedRoutes()
    {
        var context = Request("GET", "/libraries");
        context.Request.Headers["X-Api-Key"] = RawKey;
        Sign(context.Request, Guid.NewGuid(), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        ViewRequestProfile? captured = new(Guid.NewGuid(), "sentinel");
        var middleware = new ApiKeyMiddleware(ctx =>
        {
            captured = new HttpViewRequestProfileContext(
                new HttpContextAccessor { HttpContext = ctx }).Current;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, Cache(), Configuration());

        Assert.Null(captured);
    }

    [Fact]
    public async Task ExplicitLoopbackBypassMayTrustUnsignedProfileHeader()
    {
        var profileId = Guid.NewGuid();
        var context = Request("GET", "/view/assets");
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers[ViewProfileAssertion.ProfileHeader] = profileId.ToString();
        ViewRequestProfile? captured = null;
        var middleware = new ApiKeyMiddleware(ctx =>
        {
            captured = new HttpViewRequestProfileContext(
                new HttpContextAccessor { HttpContext = ctx }).Current;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            context,
            new StaticCache(null),
            Configuration(("MediaEngine:Security:LocalhostBypass", "true")));

        Assert.Equal(profileId, captured!.ProfileId);
        Assert.Equal("Administrator", captured.Role);
    }

    [Fact]
    public async Task DisabledLoopbackBypassDoesNotTrustUnsignedProfileHeader()
    {
        var context = Request("GET", "/view/assets");
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers[ViewProfileAssertion.ProfileHeader] = Guid.NewGuid().ToString();
        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            context,
            new StaticCache(null),
            Configuration(("MediaEngine:Security:LocalhostBypass", "false")));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    private static DefaultHttpContext Request(string method, string path, string? query = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query ?? string.Empty);
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static void Sign(HttpRequest request, Guid profileId, long timestamp)
    {
        request.Headers[ViewProfileAssertion.ProfileHeader] = profileId.ToString("D");
        request.Headers[ViewProfileAssertion.TimestampHeader] = timestamp.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        var canonical = ViewProfileAssertion.CreateCanonicalValue(request, profileId, timestamp);
        var signature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(RawKey),
            Encoding.UTF8.GetBytes(canonical));
        request.Headers[ViewProfileAssertion.SignatureHeader] = Convert
            .ToBase64String(signature)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static IApiKeyLookupCache Cache() => new StaticCache(new ApiKey
    {
        Id = Guid.NewGuid(),
        Label = "Dashboard",
        HashedKey = ApiKeyService.HashKey(RawKey),
        Role = "RestrictedProfile",
        CreatedAt = DateTimeOffset.UtcNow,
    });

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value))
            .Build();

    private sealed class StaticCache(ApiKey? key) : IApiKeyLookupCache
    {
        public Task<ApiKey?> FindByHashedKeyAsync(string hashedKey, CancellationToken ct = default) =>
            Task.FromResult(key is not null && key.HashedKey == hashedKey ? key : null);

        public void InvalidateAll()
        {
        }
    }
}
