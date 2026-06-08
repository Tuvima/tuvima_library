using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MediaEngine.Domain;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Providers.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Providers.Tests;

public sealed class RetailProviderDecompositionTests
{
    [Fact]
    public void RetailRequestBuilder_BuildsAppleSearchAndLookupUrls()
    {
        var builder = new RetailRequestBuilder();

        var track = builder.BuildAppleTrackSearchUrl("Rock & Roll", "us", "en");
        var album = builder.BuildAppleAlbumSearchUrl("AC/DC", "Back in Black", "gb", "en");
        var lookup = builder.BuildAppleAlbumLookupUrl("12345", "ca", "fr");

        Assert.Equal("https://itunes.apple.com/search?term=Rock%20%26%20Roll&entity=musicTrack&limit=10&country=us&lang=en_us", track);
        Assert.Equal("https://itunes.apple.com/search?term=AC%2FDC%20Back%20in%20Black&entity=album&limit=10&country=gb&lang=en_gb", album);
        Assert.Equal("https://itunes.apple.com/lookup?id=12345&entity=song&country=ca&lang=fr_ca", lookup);
    }

    [Fact]
    public void RetailRequestBuilder_BuildsTmdbUrlsAndImageUrls()
    {
        var builder = new RetailRequestBuilder();

        var search = builder.BuildTmdbTvSearchUrl("Shogun", 2024, "key", "en", "US");
        var unfiltered = builder.BuildTmdbTvSearchUrl("Shogun", null, "key", "en", "US");
        var details = builder.BuildTmdbTvDetailsUrl("456", "key", "en", "US");
        var season = builder.BuildTmdbSeasonUrl("456", 2, "key", "en", "US");

        Assert.Equal("https://api.themoviedb.org/3/search/tv?query=Shogun&include_adult=false&language=en-US&page=1&api_key=key&first_air_date_year=2024", search);
        Assert.Equal("https://api.themoviedb.org/3/search/tv?query=Shogun&include_adult=false&language=en-US&page=1&api_key=key", unfiltered);
        Assert.Equal("https://api.themoviedb.org/3/tv/456?language=en-US&append_to_response=content_ratings&api_key=key", details);
        Assert.Equal("https://api.themoviedb.org/3/tv/456/season/2?language=en-US&api_key=key", season);
        Assert.Equal("https://image.tmdb.org/t/p/w500/path.jpg", RetailRequestBuilder.BuildTmdbImageUrl("/path.jpg"));
        Assert.Equal("https://cdn.example/image.png", RetailRequestBuilder.BuildTmdbImageUrl("https://cdn.example/image.png"));
    }

    [Fact]
    public async Task AppleRetailClient_TrackSearchAcceptsThresholdAndKeepsBestMatch()
    {
        var client = BuildAppleClient(request =>
        {
            Assert.Contains("entity=musicTrack", request.RequestUri!.Query, StringComparison.Ordinal);
            return JsonResponse("""
                {
                  "results": [
                    { "trackName": "Wrong", "artistName": "Other", "collectionName": "Other", "collectionId": 1, "trackCount": 9 },
                    { "trackName": "Everlong", "artistName": "Foo Fighters", "collectionName": "The Colour and the Shape", "collectionId": 99, "trackCount": 13 }
                  ]
                }
                """);
        });

        var match = await client.SearchTrackAsync(
            "Foo Fighters",
            "Everlong",
            "The Colour and the Shape",
            "us",
            "en",
            CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal("99", match.CollectionId);
        Assert.True(match.Score >= 0.50);
    }

    [Fact]
    public async Task AppleRetailClient_TrackSearchReturnsNullBelowThreshold()
    {
        var client = BuildAppleClient(_ => JsonResponse("""
            { "results": [
              { "trackName": "Completely Different", "artistName": "Other", "collectionName": "Other", "collectionId": 7, "trackCount": 11 }
            ] }
            """));

        var match = await client.SearchTrackAsync("Foo Fighters", "Everlong", null, "us", "en", CancellationToken.None);

        Assert.Null(match);
    }

    [Fact]
    public async Task AppleRetailClient_TrackSearchReturnsExactSingleTrackImmediately()
    {
        var calls = 0;
        var client = BuildAppleClient(_ =>
        {
            calls++;
            return JsonResponse("""
                { "results": [
                  { "trackName": "One More Time", "artistName": "Daft Punk", "collectionName": "One More Time", "collectionId": 42, "trackCount": 1 }
                ] }
                """);
        });

        var match = await client.SearchTrackAsync("Daft Punk", "One More Time", "Discovery", "us", "en", CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal("42", match.CollectionId);
        Assert.True(match.SingleTrackRelease);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AppleRetailClient_AlbumSearchUsesThresholdAndWeightedAlbumArtistScore()
    {
        var client = BuildAppleClient(request =>
        {
            Assert.Contains("entity=album", request.RequestUri!.Query, StringComparison.Ordinal);
            return JsonResponse("""
                { "results": [
                  { "collectionName": "Discovery", "artistName": "Daft Punk", "collectionId": 123 },
                  { "collectionName": "Homework", "artistName": "Daft Punk", "collectionId": 456 }
                ] }
                """);
        });

        var collectionId = await client.SearchAlbumAsync("Daft Punk", "Discovery", "us", "en", CancellationToken.None);

        Assert.Equal("123", collectionId);
    }

    [Fact]
    public async Task AppleRetailClient_AlbumLookupReturnsOnlyTrackWrappers()
    {
        var client = BuildAppleClient(_ => JsonResponse("""
            { "results": [
              { "wrapperType": "collection", "collectionId": 123 },
              { "wrapperType": "track", "trackName": "One" },
              { "wrapperType": "track", "trackName": "Two" }
            ] }
            """));

        var tracks = await client.FetchAlbumTracksAsync("123", "us", "en", CancellationToken.None);

        Assert.Equal(2, tracks.Count);
        Assert.Equal("One", tracks[0]!["trackName"]!.GetValue<string>());
    }

    [Fact]
    public async Task AppleRetailClient_ProviderFailuresReturnSafeNoMatchButCancellationPropagates()
    {
        var failingClient = BuildAppleClient(_ => throw new HttpRequestException("boom"));

        var failed = await failingClient.SearchTrackAsync("A", "B", null, "us", "en", CancellationToken.None);

        Assert.Null(failed);

        var cancellingClient = BuildAppleClient(_ => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            cancellingClient.SearchTrackAsync("A", "B", null, "us", "en", CancellationToken.None));
    }

    [Fact]
    public async Task TmdbRetailClient_YearFilteredSearchRetriesUnfiltered()
    {
        var calls = new List<string>();
        var client = BuildTmdbClient(request =>
        {
            calls.Add(request.RequestUri!.ToString());
            return calls.Count == 1
                ? JsonResponse("""{ "results": [] }""")
                : JsonResponse("""
                    { "results": [
                      { "name": "Shogun", "id": 900, "poster_path": "/poster.jpg", "first_air_date": "2024-02-27" }
                    ] }
                    """);
        });

        var result = await client.SearchShowAsync("Shogun", 1980, "key", "en", "US", CancellationToken.None);

        Assert.Equal("900", result.TvId);
        Assert.Contains("first_air_date_year=1980", calls[0], StringComparison.Ordinal);
        Assert.DoesNotContain("first_air_date_year", calls[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TmdbRetailClient_DetailAndSeasonNotFoundAreSafeFallbacks()
    {
        var client = BuildTmdbClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var details = await client.FetchShowDetailsAsync("1", "key", "en", "US", CancellationToken.None);
        var episodes = await client.FetchSeasonEpisodesAsync("1", 1, "key", "en", "US", CancellationToken.None);

        Assert.Null(details);
        Assert.Empty(episodes);
    }

    [Fact]
    public void RetailCandidateScorer_PreservesThresholdAndCapBehavior()
    {
        var scorer = new RetailCandidateScorer();
        var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MetadataFieldConstants.Title] = "Dune",
            [MetadataFieldConstants.Author] = "Frank Herbert",
        };

        var accepted = scorer.EvaluateDecision(
            hints,
            "Dune",
            "Frank Herbert",
            null,
            new FieldMatchScores { TitleScore = 1, AuthorScore = 1, FormatScore = 1, CompositeScore = 0.95 },
            0.95,
            0.90,
            0.65,
            "test");
        var ambiguous = scorer.EvaluateDecision(
            hints,
            "Dune",
            "Different Author",
            null,
            new FieldMatchScores { TitleScore = 1, AuthorScore = 0.1, FormatScore = 1, CompositeScore = 0.95 },
            0.95,
            0.90,
            0.65,
            "test");
        var rejected = scorer.EvaluateDecision(
            hints,
            "Different",
            "Other",
            null,
            new FieldMatchScores { TitleScore = 0.1, AuthorScore = 0.1, FormatScore = 1, CompositeScore = 0.50 },
            0.50,
            0.90,
            0.65,
            "test");

        Assert.Equal("AutoAccepted", accepted.Outcome);
        Assert.Equal("Ambiguous", ambiguous.Outcome);
        Assert.Contains("creator_similarity_weak", ambiguous.RejectionReasons);
        Assert.Equal("Rejected", rejected.Outcome);
    }

    [Fact]
    public void RetailCandidateScorer_RejectsDerivativeBookCandidateForNormalSource()
    {
        var scorer = new RetailCandidateScorer();
        var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MetadataFieldConstants.Title] = "The Long Walk",
            [MetadataFieldConstants.Author] = "Stephen King",
        };

        var decision = scorer.EvaluateDecision(
            hints,
            "The Long Walk: Book Analysis and Summary",
            "Stephen King",
            null,
            new FieldMatchScores { TitleScore = 0.96, AuthorScore = 1, FormatScore = 1, CompositeScore = 0.97 },
            0.97,
            0.90,
            0.65,
            "test",
            mediaType: MediaType.Books,
            extendedMetadata: new CandidateExtendedMetadata
            {
                Description = "A chapter by chapter analysis of the novel.",
                Genres = ["Study Aids"],
            });

        Assert.Equal("Rejected", decision.Outcome);
        Assert.Equal("candidate_quality_rejected", decision.ThresholdPath);
        Assert.Contains("derivative_candidate", decision.RejectionReasons);
    }

    private static AppleRetailClient BuildAppleClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(
            new RoutingHttpClientFactory(responder),
            new RetailRequestBuilder(),
            new RetailHttpThrottle(),
            NullLogger<AppleRetailClient>.Instance);

    private static TmdbRetailClient BuildTmdbClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(
            new RoutingHttpClientFactory(responder),
            new RetailRequestBuilder(),
            new RetailHttpThrottle(),
            NullLogger<TmdbRetailClient>.Instance);

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class RoutingHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RoutingHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        public HttpClient CreateClient(string name)
            => new(new RoutingHttpMessageHandler(_responder), disposeHandler: true);
    }

    private sealed class RoutingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RoutingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
