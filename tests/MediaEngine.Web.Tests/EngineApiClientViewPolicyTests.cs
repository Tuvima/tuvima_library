using System.Net;
using System.Text;
using MediaEngine.Contracts.Profiles;
using MediaEngine.Web.Services.Integration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Web.Tests;

public sealed class EngineApiClientViewPolicyTests
{
    private static readonly Guid ProfileId = Guid.Parse("b5028d65-b179-4bed-a3b6-2e6011b90d31");

    [Fact]
    public async Task UpdateViewProfilePolicyAsync_PreservesIndependentSharedDecisions()
    {
        string? requestJson = null;
        using var http = CreateHttpClient(async request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal($"/profiles/{ProfileId:D}/settings/view", request.RequestUri!.AbsolutePath);
            requestJson = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
            };
        });
        var client = new EngineApiClient(http, NullLogger<EngineApiClient>.Instance);

        var saved = await client.UpdateViewProfilePolicyAsync(ProfileId, new UpdateViewProfilePolicyRequest
        {
            ViewEnabled = true,
            AccessSharedView = false,
            IncludeInSharedView = true,
            AllowGallerySharing = true,
        });

        Assert.NotNull(saved);
        Assert.Contains("\"access_shared_view\":false", requestJson, StringComparison.Ordinal);
        Assert.Contains("\"include_in_shared_view\":true", requestJson, StringComparison.Ordinal);
        Assert.False(saved.AccessSharedView);
        Assert.True(saved.IncludeInSharedView);
    }

    [Fact]
    public async Task GetViewProfilePolicyAsync_UsesProfileAdminRoute()
    {
        using var http = CreateHttpClient(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/profiles/{ProfileId:D}/settings/view", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""
                    {
                      "profile_id": "{{ProfileId:D}}",
                      "view_enabled": true,
                      "access_shared_view": true,
                      "include_in_shared_view": false,
                      "allow_gallery_sharing": false,
                      "updated_at": null
                    }
                    """, Encoding.UTF8, "application/json"),
            });
        });
        var client = new EngineApiClient(http, NullLogger<EngineApiClient>.Instance);

        var policy = await client.GetViewProfilePolicyAsync(ProfileId);

        Assert.NotNull(policy);
        Assert.True(policy.ViewEnabled);
        Assert.True(policy.AccessSharedView);
        Assert.False(policy.IncludeInSharedView);
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) =>
        new(new StubHttpMessageHandler(responder))
        {
            BaseAddress = new Uri("http://localhost:61495/"),
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request);
    }
}
