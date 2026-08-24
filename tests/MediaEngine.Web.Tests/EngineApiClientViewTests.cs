using System.Net;
using System.Text;
using MediaEngine.Domain.PersonalMedia;
using MediaEngine.Web.Services.Integration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Web.Tests;

public sealed class EngineApiClientViewTests
{
    [Fact]
    public async Task UploadViewMediaAsync_PostsOnlyMultipartFileToCleanRoute()
    {
        var itemId = Guid.NewGuid(); string? body = null; string? path = null;
        using var http = Http(request => { path = request.RequestUri!.PathAndQuery; body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult(); return Json(HttpStatusCode.OK, $$"""{"item_id":"{{itemId}}","item_added":true,"files_added":1,"sources_added":1}"""); });
        var client = Client(http); await using var stream = new MemoryStream([1, 2, 3]);
        var result = await client.UploadViewMediaAsync(stream, "photo.jpg", "image/jpeg");
        Assert.True(result.Success); Assert.Equal(itemId, result.Upload?.ItemId); Assert.Equal("/view/uploads", path);
        Assert.Contains("name=file; filename=photo.jpg", body, StringComparison.Ordinal);
        Assert.DoesNotContain("library", body, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("profile", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadViewMediaAsync_ReturnsServerPolicyDetailOnBadRequest()
    {
        using var http = Http(_ => Json(HttpStatusCode.BadRequest, "{\"title\":\"Bad Request\",\"detail\":\"Browser upload is disabled.\"}", "application/problem+json"));
        await using var stream = new MemoryStream([1]);
        var result = await Client(http).UploadViewMediaAsync(stream, "photo.jpg", "image/jpeg");
        Assert.False(result.Success); Assert.Equal("Browser upload is disabled.", result.ErrorMessage);
    }

    [Fact]
    public async Task ViewClient_UsesTrustedCleanScopeAssetDiscoveryAndMutationRoutes()
    {
        var itemId = Guid.NewGuid(); var requests = new List<(HttpMethod Method, string Path)>();
        using var http = Http(request =>
        {
            requests.Add((request.Method, request.RequestUri!.PathAndQuery));
            var json = request.RequestUri.AbsolutePath switch
            {
                "/view/scopes" => "{\"scope\":{\"kind\":0,\"profile_id\":null,\"was_fallback\":false},\"available_scopes\":[]}",
                "/view/assets" => "{\"items\":[],\"next_cursor\":null,\"has_more\":false}",
                "/view/people" => "{\"items\":[],\"next_cursor\":null,\"has_more\":false,\"capability\":{\"state\":\"empty\",\"has_indexed_data\":false,\"automatic_processing_available\":false,\"message\":\"None\",\"evidence_kinds\":[]}}",
                _ => "",
            };
            return Json(HttpStatusCode.OK, json);
        });
        var client = Client(http);
        await client.GetViewScopesAsync(ViewScopeKind.Shared);
        await client.GetViewAssetsAsync(new(ViewScopeKind.Shared, Search: "summer trip", Kinds: ["video"], FavoritesOnly: true));
        await client.GetViewPeopleAsync(new(ViewScopeKind.Mine, Search: "Sarah"));
        await client.SetViewItemFavoriteAsync(itemId, true); await client.ArchiveViewItemAsync(itemId);
        Assert.Contains(requests, r => r.Path.StartsWith("/view/scopes?", StringComparison.Ordinal));
        Assert.Contains(requests, r => r.Path.Contains("/view/assets?scope=shared", StringComparison.Ordinal) && r.Path.Contains("q=summer%20trip", StringComparison.Ordinal) && r.Path.Contains("kind=video", StringComparison.Ordinal));
        Assert.Contains(requests, r => r.Path.Contains("/view/people?scope=mine", StringComparison.Ordinal));
        Assert.Contains(requests, r => r == (HttpMethod.Put, $"/view/items/{itemId:D}/favorite")); Assert.Contains(requests, r => r == (HttpMethod.Post, $"/view/items/{itemId:D}/archive"));
        Assert.DoesNotContain(requests, r => r.Path.Contains("profileId=", StringComparison.OrdinalIgnoreCase) || r.Path.Contains("/view/libraries", StringComparison.Ordinal));
    }

    private static EngineApiClient Client(HttpClient http) => new(http, NullLogger<EngineApiClient>.Instance);
    private static HttpClient Http(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new Stub(respond)) { BaseAddress = new Uri("http://localhost:61495/") };
    private static HttpResponseMessage Json(HttpStatusCode status, string json, string media = "application/json") => new(status) { Content = new StringContent(json, Encoding.UTF8, media) };
    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request)); }
}
