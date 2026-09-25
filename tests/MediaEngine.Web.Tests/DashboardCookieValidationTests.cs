using System.Net;
using MediaEngine.Web.Services.Integration;

namespace MediaEngine.Web.Tests;

public sealed class DashboardCookieValidationTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    public async Task OnlyProvenInvalidSessionsInvalidateTheCookie(HttpStatusCode status, bool invalid)
    {
        using var http = new HttpClient(new Handler(status)) { BaseAddress = new Uri("http://localhost") };
        var identity = new DashboardIdentityClient(new Factory(http));
        var result = await identity.ValidateCookieAsync("test-session");
        Assert.Null(result.Response);
        Assert.Equal(invalid, result.Invalid);
    }
    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status));
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
