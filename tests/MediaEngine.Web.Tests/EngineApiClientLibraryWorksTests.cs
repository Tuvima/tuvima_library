using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MediaEngine.Web.Services.Integration;

namespace MediaEngine.Web.Tests;

public sealed class EngineApiClientLibraryWorksTests
{
    [Fact]
    public async Task GetLibraryWorksAsync_MapsReturnedItemsForHomePage()
    {
        const string json = """
            [
              {
                "id": "11111111-1111-1111-1111-111111111111",
                "collectionId": "99999999-9999-9999-9999-999999999999",
                "rootWorkId": "33333333-3333-3333-3333-333333333333",
                "mediaType": "Books",
                "workKind": "leaf",
                "ordinal": 1,
                "wikidataQid": "Q123",
                "assetId": "22222222-2222-2222-2222-222222222222",
                "createdAt": "2026-04-17T12:00:00Z",
                "coverUrl": "/stream/22222222-2222-2222-2222-222222222222/cover",
                "backgroundUrl": "/stream/22222222-2222-2222-2222-222222222222/background",
                "bannerUrl": "/stream/22222222-2222-2222-2222-222222222222/banner",
                "heroUrl": "/stream/22222222-2222-2222-2222-222222222222/hero",
                "logoUrl": "/stream/22222222-2222-2222-2222-222222222222/logo",
                "canonicalValues": {
                  "title": "Dune",
                  "author": "Frank Herbert"
                }
              }
            ]
            """;

        using var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });

        var client = new EngineApiClient(httpClient, NullLogger<EngineApiClient>.Instance);

        var results = await client.GetLibraryWorksAsync();

        var work = Assert.Single(results);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), work.Id);
        Assert.Equal(Guid.Parse("99999999-9999-9999-9999-999999999999"), work.CollectionId);
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), work.RootWorkId);
        Assert.Equal("Books", work.MediaType);
        Assert.Equal("leaf", work.WorkKind);
        Assert.Equal(new DateTimeOffset(2026, 4, 17, 12, 0, 0, TimeSpan.Zero), work.CreatedAt);
        Assert.Equal("Dune", work.Title);
        Assert.Equal("Frank Herbert", work.Author);
        Assert.Equal("http://localhost:61495/stream/22222222-2222-2222-2222-222222222222/cover", work.CoverUrl);
        Assert.Equal("http://localhost:61495/stream/22222222-2222-2222-2222-222222222222/background", work.BackgroundUrl);
        Assert.Equal("http://localhost:61495/stream/22222222-2222-2222-2222-222222222222/banner", work.BannerUrl);
        Assert.Equal("http://localhost:61495/stream/22222222-2222-2222-2222-222222222222/hero", work.HeroUrl);
        Assert.Equal("http://localhost:61495/stream/22222222-2222-2222-2222-222222222222/logo", work.LogoUrl);
    }

    [Fact]
    public async Task GetLibraryWorksAsync_NormalizesRootRelativeAndRootlessArtworkUrls()
    {
        const string json = """
            [
              {
                "id": "11111111-1111-1111-1111-111111111111",
                "mediaType": "Books",
                "assetId": "22222222-2222-2222-2222-222222222222",
                "coverUrl": "stream/22222222-2222-2222-2222-222222222222/cover",
                "heroUrl": "stream/22222222-2222-2222-2222-222222222222/hero",
                "canonicalValues": {
                  "title": "Dune"
                }
              }
            ]
            """;

        using var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });

        var client = new EngineApiClient(httpClient, NullLogger<EngineApiClient>.Instance);

        var results = await client.GetLibraryWorksAsync();

        var work = Assert.Single(results);
        Assert.Equal("http://localhost:61495/stream/22222222-2222-2222-2222-222222222222/cover", work.CoverUrl);
        Assert.Equal("http://localhost:61495/stream/22222222-2222-2222-2222-222222222222/hero", work.HeroUrl);
    }

    [Fact]
    public async Task GetContentGroupsAsync_MapsRichArtworkAndContextFields()
    {
        const string json = """
            [
              {
                "collection_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "display_name": "Funny AF with Kevin Hart",
                "primary_media_type": "TV",
                "work_count": 6,
                "cover_url": "/stream/cover",
                "background_url": "/stream/background",
                "banner_url": "/stream/banner",
                "logo_url": "/stream/logo",
                "description": "Competition series",
                "tagline": "New episodes coming Monday",
                "creator": "Kevin Hart",
                "director": "Leslie Small",
                "writer": "Writers Room",
                "release_date": "2026-04-14",
                "network": "Netflix",
                "year": "2026",
                "season_count": 2,
                "created_at": "2026-04-10T12:00:00Z"
              }
            ]
            """;

        using var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });

        var client = new EngineApiClient(httpClient, NullLogger<EngineApiClient>.Instance);

        var groups = await client.GetContentGroupsAsync();

        var group = Assert.Single(groups);
        Assert.Equal("Funny AF with Kevin Hart", group.DisplayName);
        Assert.Equal("http://localhost:61495/stream/cover", group.CoverUrl);
        Assert.Equal("http://localhost:61495/stream/background", group.BackgroundUrl);
        Assert.Equal("http://localhost:61495/stream/banner", group.BannerUrl);
        Assert.Equal("http://localhost:61495/stream/logo", group.LogoUrl);
        Assert.Equal("Competition series", group.Description);
        Assert.Equal("New episodes coming Monday", group.Tagline);
        Assert.Equal("Kevin Hart", group.Creator);
        Assert.Equal("Leslie Small", group.Director);
        Assert.Equal("Writers Room", group.Writer);
        Assert.Equal("2026-04-14", group.ReleaseDate);
        Assert.Equal("Netflix", group.Network);
        Assert.Equal("2026", group.Year);
        Assert.Equal(2, group.SeasonCount);
    }

    [Fact]
    public async Task GetSystemViewGroupsAsync_UsesRequestedGroupingAndMapsLogo()
    {
        HttpRequestMessage? capturedRequest = null;
        const string json = """
            [
              {
                "collection_id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                "display_name": "The Record",
                "primary_media_type": "Music",
                "work_count": 12,
                "cover_url": "/stream/album-cover",
                "logo_url": "/stream/album-logo",
                "description": "Album group",
                "tagline": "Fresh in your library",
                "creator": "boygenius",
                "year": "2023",
                "created_at": "2026-04-10T12:00:00Z"
              }
            ]
            """;

        using var httpClient = CreateHttpClient(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });

        var client = new EngineApiClient(httpClient, NullLogger<EngineApiClient>.Instance);

        var groups = await client.GetSystemViewGroupsAsync(mediaType: "Music", groupField: "album");

        Assert.NotNull(capturedRequest);
        Assert.Equal(
            "http://localhost:61495/collections/system-views?mediaType=Music&groupField=album",
            capturedRequest!.RequestUri!.ToString());

        var group = Assert.Single(groups);
        Assert.Equal("The Record", group.DisplayName);
        Assert.Equal("http://localhost:61495/stream/album-cover", group.CoverUrl);
        Assert.Equal("http://localhost:61495/stream/album-logo", group.LogoUrl);
        Assert.Equal("Album group", group.Description);
        Assert.Equal("Fresh in your library", group.Tagline);
    }

    [Fact]
    public async Task GetSystemViewGroupDetailAsync_IncludesArtistFilterWhenProvided()
    {
        HttpRequestMessage? capturedRequest = null;
        const string json = """
            {
              "collection_id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
              "display_name": "The Record",
              "works": [],
              "seasons": [],
              "top_cast": []
            }
            """;

        using var httpClient = CreateHttpClient(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });

        var client = new EngineApiClient(httpClient, NullLogger<EngineApiClient>.Instance);

        var detail = await client.GetSystemViewGroupDetailAsync("album", "The Record", mediaType: "Music", artistName: "boygenius");

        Assert.NotNull(detail);
        Assert.NotNull(capturedRequest);
        Assert.Equal(
            "http://localhost:61495/collections/system-view-detail?groupField=album&groupValue=The Record&mediaType=Music&artistName=boygenius",
            capturedRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task UploadScopeArtworkFromUrlAsync_PostsToScopedArtworkEndpoint()
    {
        HttpRequestMessage? capturedRequest = null;

        using var httpClient = CreateHttpClient(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        });

        var client = new EngineApiClient(httpClient, NullLogger<EngineApiClient>.Instance);
        var result = await client.UploadScopeArtworkFromUrlAsync(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "series",
            "CoverArt",
            "https://example.test/poster.jpg");

        Assert.True(result);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal(
            "http://localhost:61495/metadata/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/artwork/series/CoverArt/from-url",
            capturedRequest.RequestUri!.ToString());

        var payload = await capturedRequest.Content!.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);
        Assert.Equal("https://example.test/poster.jpg", json.RootElement.GetProperty("image_url").GetString());
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
}
