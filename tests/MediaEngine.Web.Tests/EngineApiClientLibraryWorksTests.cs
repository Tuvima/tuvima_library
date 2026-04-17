using System.Net;
using System.Net.Http;
using System.Text;
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
                "mediaType": "Books",
                "ordinal": 1,
                "wikidataQid": "Q123",
                "assetId": "22222222-2222-2222-2222-222222222222",
                "createdAt": "2026-04-17T12:00:00Z",
                "canonicalValues": {
                  "title": "Dune",
                  "author": "Frank Herbert",
                  "cover": "/stream/22222222-2222-2222-2222-222222222222/cover"
                }
              }
            ]
            """;

        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            }))
        {
            BaseAddress = new Uri("http://localhost:61495"),
        };

        var client = new EngineApiClient(httpClient, NullLogger<EngineApiClient>.Instance);

        var results = await client.GetLibraryWorksAsync();

        var work = Assert.Single(results);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), work.Id);
        Assert.Equal("Books", work.MediaType);
        Assert.Equal("Dune", work.Title);
        Assert.Equal("Frank Herbert", work.Author);
        Assert.Equal("http://localhost:61495/stream/22222222-2222-2222-2222-222222222222/cover", work.CoverUrl);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
