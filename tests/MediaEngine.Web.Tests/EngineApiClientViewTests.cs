using System.Net;
using System.Text;
using MediaEngine.Web.Services.Integration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Web.Tests;

public sealed class EngineApiClientViewTests
{
    [Fact]
    public async Task UploadViewMediaAsync_PostsMultipartFileAndExplicitDestination()
    {
        var libraryId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        string? multipartBody = null;
        string? requestContentType = null;
        using var http = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestContentType = request.Content?.Headers.ContentType?.MediaType;
            multipartBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"path":"C:\\view\\photo.jpg","mediaType":"","destinationLibraryId":"{{libraryId:D}}"}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        })) { BaseAddress = new Uri("http://localhost:61495/") };
        var client = new EngineApiClient(http, NullLogger<EngineApiClient>.Instance);
        await using var stream = new MemoryStream([1, 2, 3, 4]);

        var result = await client.UploadViewMediaAsync(
            libraryId,
            stream,
            "photo.jpg",
            "image/jpeg");

        Assert.True(result.Success);
        Assert.Equal(libraryId.ToString("D"), result.Upload?.destinationLibraryId);
        Assert.Equal("multipart/form-data", requestContentType);
        Assert.Contains("name=file; filename=photo.jpg", multipartBody, StringComparison.Ordinal);
        Assert.Contains("Content-Type: image/jpeg", multipartBody, StringComparison.Ordinal);
        Assert.Contains("name=destinationLibraryId", multipartBody, StringComparison.Ordinal);
        Assert.Contains(libraryId.ToString("D"), multipartBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadViewMediaAsync_ReturnsServerPolicyDetailOnBadRequest()
    {
        using var http = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                "{\"title\":\"Bad Request\",\"detail\":\"Browser upload is disabled for personal libraries by administrator policy.\"}",
                Encoding.UTF8,
                "application/problem+json"),
        })) { BaseAddress = new Uri("http://localhost:61495/") };
        var client = new EngineApiClient(http, NullLogger<EngineApiClient>.Instance);
        await using var stream = new MemoryStream([1]);

        var result = await client.UploadViewMediaAsync(Guid.NewGuid(), stream, "photo.jpg", "image/jpeg");

        Assert.False(result.Success);
        Assert.Equal(
            "Browser upload is disabled for personal libraries by administrator policy.",
            result.ErrorMessage);
    }

    [Fact]
    public async Task ViewClient_UsesProfileScopedLibraryItemAndMutationRoutes()
    {
        var profileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var libraryId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var itemId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var requests = new List<(HttpMethod Method, string PathAndQuery)>();
        using var http = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requests.Add((request.Method, request.RequestUri!.PathAndQuery));
            var body = request.RequestUri.AbsolutePath switch
            {
                "/view/libraries" => "[]",
                var path when path.EndsWith("/scan", StringComparison.Ordinal) => $$"""
                    {"library_id":"{{libraryId}}","files_seen":0,"items_added":0,"files_added":0,"sources_added":0,"duplicates_found":0,"errors":0}
                    """,
                var path when request.Method == HttpMethod.Get && path == $"/view/{libraryId:D}" =>
                    "{\"items\":[],\"offset\":0,\"limit\":120,\"total\":0,\"has_more\":false}",
                _ => string.Empty,
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        })) { BaseAddress = new Uri("http://localhost:61495/") };
        var client = new EngineApiClient(http, NullLogger<EngineApiClient>.Instance);

        await client.GetViewLibrariesAsync(profileId);
        await client.GetViewItemsAsync(libraryId, profileId, "summer trip", "media", favorites: true, hidden: true);
        await client.ScanViewLibraryAsync(libraryId, profileId);
        await client.SetViewItemFavoriteAsync(libraryId, itemId, profileId, true);
        await client.SetViewItemHiddenAsync(libraryId, itemId, profileId, true);

        Assert.Contains(requests, request => request == (HttpMethod.Get, $"/view/libraries?profileId={profileId:D}"));
        var itemRequest = Assert.Single(
            requests,
            request => request.Method == HttpMethod.Get
                && request.PathAndQuery.StartsWith($"/view/{libraryId:D}?", StringComparison.Ordinal));
        Assert.Contains($"profileId={profileId:D}", itemRequest.PathAndQuery, StringComparison.Ordinal);
        Assert.Contains("q=summer%20trip", itemRequest.PathAndQuery, StringComparison.Ordinal);
        Assert.Contains("kind=media", itemRequest.PathAndQuery, StringComparison.Ordinal);
        Assert.Contains("favorite=true", itemRequest.PathAndQuery, StringComparison.Ordinal);
        Assert.Contains("hidden=true", itemRequest.PathAndQuery, StringComparison.Ordinal);
        Assert.Contains(requests, request => request == (HttpMethod.Post, $"/view/{libraryId:D}/scan?profileId={profileId:D}"));
        Assert.Contains(requests, request => request == (HttpMethod.Put, $"/view/{libraryId:D}/items/{itemId:D}/favorite?profileId={profileId:D}"));
        Assert.Contains(requests, request => request == (HttpMethod.Put, $"/view/{libraryId:D}/items/{itemId:D}/hidden?profileId={profileId:D}"));
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
