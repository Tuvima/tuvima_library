using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MediaEngine.Web.Services.Integration;

namespace MediaEngine.Web.Tests;

public sealed class EngineApiClientPersonCreditsTests
{
    [Fact]
    public async Task GetPersonLibraryCreditsAsync_NormalizesCreditAndCharacterArtwork()
    {
        const string json = """
            [
              {
                "work_id": "11111111-1111-1111-1111-111111111111",
                "collection_id": "22222222-2222-2222-2222-222222222222",
                "media_type": "Movies",
                "title": "Dune",
                "cover_url": "/stream/work-cover",
                "year": "2021",
                "role": "Actor",
                "characters": [
                  {
                    "fictional_entity_id": "33333333-3333-3333-3333-333333333333",
                    "character_name": "Paul Atreides",
                    "portrait_url": "stream/paul-portrait"
                  }
                ]
              }
            ]
            """;

        using var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });

        var client = new EngineApiClient(httpClient, NullLogger<EngineApiClient>.Instance);

        var credits = await client.GetPersonLibraryCreditsAsync(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

        var credit = Assert.Single(credits);
        Assert.Equal("http://localhost:61495/stream/work-cover", credit.CoverUrl);
        Assert.Equal("Paul Atreides", Assert.Single(credit.Characters).CharacterName);
        Assert.Equal("http://localhost:61495/stream/paul-portrait", credit.Characters[0].PortraitUrl);
    }

    [Fact]
    public async Task GetPersonCharacterRolesAsync_PreservesExactWorkContextAndNormalizesPortrait()
    {
        const string json = """
            [
              {
                "fictional_entity_id": "33333333-3333-3333-3333-333333333333",
                "character_name": "Paul Atreides",
                "portrait_url": "/stream/paul-portrait",
                "work_id": "11111111-1111-1111-1111-111111111111",
                "work_qid": "Q1544",
                "work_title": "Dune",
                "collection_id": "22222222-2222-2222-2222-222222222222",
                "media_type": "Movies",
                "is_default": true,
                "universe_qid": "Q240827",
                "universe_label": "Dune"
              }
            ]
            """;

        using var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });

        var client = new EngineApiClient(httpClient, NullLogger<EngineApiClient>.Instance);

        var roles = await client.GetPersonCharacterRolesAsync(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

        var role = Assert.Single(roles);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), role.WorkId);
        Assert.Equal("Q1544", role.WorkQid);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), role.CollectionId);
        Assert.Equal("Movies", role.MediaType);
        Assert.Equal("http://localhost:61495/stream/paul-portrait", role.PortraitUrl);
    }

    [Fact]
    public async Task GetWorkCastAsync_NormalizesActorAndCharacterImages()
    {
        const string json = """
            [
              {
                "person_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "name": "Timothee Chalamet",
                "actor_person_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "actor_name": "Timothee Chalamet",
                "headshot_url": "/stream/person-headshot",
                "actor_headshot_url": "stream/actor-headshot",
                "character_name": "Paul Atreides",
                "character_image_url": "/stream/character-fallback",
                "characters": [
                  {
                    "fictional_entity_id": "33333333-3333-3333-3333-333333333333",
                    "character_name": "Paul Atreides",
                    "portrait_url": "/stream/paul-portrait"
                  }
                ]
              }
            ]
            """;

        using var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });

        var client = new EngineApiClient(httpClient, NullLogger<EngineApiClient>.Instance);

        var cast = await client.GetWorkCastAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        var castCredit = Assert.Single(cast);
        Assert.Equal("http://localhost:61495/stream/person-headshot", castCredit.HeadshotUrl);
        Assert.Equal("http://localhost:61495/stream/actor-headshot", castCredit.ActorHeadshotUrl);
        Assert.Equal("http://localhost:61495/stream/character-fallback", castCredit.CharacterImageUrl);
        Assert.Equal("http://localhost:61495/stream/paul-portrait", Assert.Single(castCredit.Characters).PortraitUrl);
    }

    [Fact]
    public async Task GetPersonAliasesAsync_NormalizesLocalHeadshotRoutes()
    {
        const string json = """
            {
              "person_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
              "person_name": "Timothee Chalamet",
              "is_pseudonym": false,
              "aliases": [
                {
                  "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                  "name": "Alias Name",
                  "headshot_url": "/persons/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb/headshot",
                  "is_pseudonym": true,
                  "wikidata_qid": "Q123",
                  "relationship": "pen_name"
                }
              ]
            }
            """;

        using var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });

        var client = new EngineApiClient(httpClient, NullLogger<EngineApiClient>.Instance);

        var aliases = await client.GetPersonAliasesAsync(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

        Assert.NotNull(aliases);
        Assert.Equal(
            "http://localhost:61495/persons/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb/headshot",
            Assert.Single(aliases!.Aliases).HeadshotUrl);
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
