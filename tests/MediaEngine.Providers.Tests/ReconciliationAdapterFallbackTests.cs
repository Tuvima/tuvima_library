using System.Reflection;
using MediaEngine.Domain;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Tuvima.Wikidata;

namespace MediaEngine.Providers.Tests;

public sealed class ReconciliationAdapterFallbackTests
{
    [Fact]
    public void ResolveDisplayLanguage_PrefersFileLanguageWhenDifferent()
    {
        Assert.Equal("ja", ReconciliationAdapter.ResolveDisplayLanguage("en-US", "ja-JP"));
        Assert.Equal("en", ReconciliationAdapter.ResolveDisplayLanguage("en-US", "en-GB"));
        Assert.Equal("en", ReconciliationAdapter.ResolveDisplayLanguage("en", null));
    }

    [Fact]
    public void BuildComicsParentFallbackRequest_UsesSeriesTitleWithoutAuthorConstraint()
    {
        var adapter = CreateAdapter();

        var request = new WikidataResolveRequest
        {
            CorrelationKey = "batman",
            MediaType = MediaType.Comics,
            Title = "Batman: Year One Part 1",
            Author = "Frank Miller",
            SeriesTitle = "Batman",
        };

        var fallback = adapter.BuildComicsParentFallbackRequest(request);

        Assert.NotNull(fallback);
        Assert.Equal("batman", fallback!.CorrelationKey);
        Assert.Equal("Batman", fallback.Title);
        Assert.Null(fallback.Creator);
        Assert.Equal(BridgeMediaKind.ComicSeries, fallback.MediaKind);
        Assert.Equal(BridgeRollupTarget.ReturnWorkAndEdition, fallback.RollupTarget);
    }

    [Fact]
    public void BuildComicsParentFallbackRequest_DisambiguatesShortSeriesTitles_WhenRequested()
    {
        var adapter = CreateAdapter();

        var request = new WikidataResolveRequest
        {
            CorrelationKey = "batman",
            MediaType = MediaType.Comics,
            SeriesTitle = "Batman",
        };

        var fallback = adapter.BuildComicsParentFallbackRequest(request, disambiguate: true);

        Assert.NotNull(fallback);
        Assert.Equal("Batman comic book series", fallback!.Title);
        Assert.Null(fallback.Creator);
    }

    [Fact]
    public void BuildComicsParentFallbackRequest_ReturnsNull_WhenSeriesTitleMissing()
    {
        var adapter = CreateAdapter();

        var request = new WikidataResolveRequest
        {
            CorrelationKey = "batman",
            MediaType = MediaType.Comics,
            Title = "Batman: Year One Part 1",
            Author = "Frank Miller",
        };

        Assert.Null(adapter.BuildComicsParentFallbackRequest(request));
    }

    [Fact]
    public void BuildMusicWorkFallbackRequest_UsesNonAlbumMusicClassesAndArtistHint()
    {
        var adapter = CreateAdapter();

        var request = new WikidataResolveRequest
        {
            CorrelationKey = "clair",
            MediaType = MediaType.Music,
            Title = "Clair de Lune",
            AlbumTitle = "Suite bergamasque",
            Artist = "Claude Debussy",
            Author = "Claude Debussy",
        };

        var fallback = adapter.BuildMusicWorkFallbackRequest(request);

        Assert.NotNull(fallback);
        Assert.Equal("clair", fallback!.CorrelationKey);
        Assert.Equal("Clair de Lune", fallback.Title);
        Assert.Equal("Claude Debussy", fallback.Creator);
        Assert.Equal(BridgeMediaKind.MusicWork, fallback.MediaKind);
        Assert.Equal(BridgeRollupTarget.ReturnWorkAndEdition, fallback.RollupTarget);
    }

    [Fact]
    public void BuildBridgeResolutionRequest_PreservesMusicBridgeIdsWithOfficialProperties()
    {
        var adapter = CreateAdapter();

        var request = new WikidataResolveRequest
        {
            CorrelationKey = "luftballons",
            MediaType = MediaType.Music,
            AlbumTitle = "99 Luftballons",
            Artist = "Nena",
            BridgeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [BridgeIdKeys.AppleMusicId] = "1446014714",
                [BridgeIdKeys.AppleMusicCollectionId] = "1446014467",
            },
            WikidataProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [BridgeIdKeys.AppleMusicId] = "P10110",
                [BridgeIdKeys.AppleMusicCollectionId] = "P2281",
            },
            IsEditionAware = true,
        };

        var method = typeof(ReconciliationAdapter).GetMethod(
            "BuildBridgeResolutionRequest",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var bridgeRequest = method!.Invoke(adapter, [request]);

        Assert.NotNull(bridgeRequest);
        Assert.IsType<BridgeResolutionRequest>(bridgeRequest);

        var bridgeIdsProperty = bridgeRequest!.GetType().GetProperty("BridgeIds", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(bridgeIdsProperty);

        var bridgeIds = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(bridgeIdsProperty!.GetValue(bridgeRequest));
        Assert.Equal(2, bridgeIds.Count);
        Assert.Equal("1446014467", bridgeIds[BridgeIdKeys.AppleMusicCollectionId]);
    }

    [Fact]
    public void BuildBridgeResolutionRequest_UsesFileLanguageForForeignContent()
    {
        var adapter = CreateAdapter();

        var request = new WikidataResolveRequest
        {
            CorrelationKey = "norwegian-wood",
            MediaType = MediaType.Books,
            Title = "Norwegian Wood",
            Author = "Haruki Murakami",
            FileLanguage = "ja-JP",
        };

        var method = typeof(ReconciliationAdapter).GetMethod(
            "BuildBridgeResolutionRequest",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var bridgeRequest = Assert.IsType<BridgeResolutionRequest>(method!.Invoke(adapter, [request]));

        Assert.Equal("ja", bridgeRequest.Language);
    }

    [Fact]
    public void GetPreferredComicSeriesQid_ReturnsMatchingParentSeriesQid()
    {
        var request = new WikidataResolveRequest
        {
            CorrelationKey = "batman",
            MediaType = MediaType.Comics,
            SeriesTitle = "Batman",
        };

        var claims = new[]
        {
            new ProviderClaim("series_qid", "Q2633138::Batman", 1.0),
        };

        var preferred = ReconciliationAdapter.GetPreferredComicSeriesQid(request, "Q383811", claims);

        Assert.Equal("Q2633138", preferred);
    }

    [Fact]
    public void GetPreferredComicSeriesQid_ReturnsNull_WhenResolvedEntityAlreadyIsSeries()
    {
        var request = new WikidataResolveRequest
        {
            CorrelationKey = "batman",
            MediaType = MediaType.Comics,
            SeriesTitle = "Batman",
        };

        var claims = new[]
        {
            new ProviderClaim("series_qid", "Q2633138::Batman", 1.0),
        };

        var preferred = ReconciliationAdapter.GetPreferredComicSeriesQid(request, "Q2633138", claims);

        Assert.Null(preferred);
    }

    [Fact]
    public async Task FetchAsync_PersonWithPreResolvedQid_DoesNotRequireName()
    {
        var adapter = CreateAdapter();

        var claims = await adapter.FetchAsync(new ProviderLookupRequest
        {
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.Person,
            MediaType = MediaType.Unknown,
            PreResolvedQid = "Q548823",
        });

        Assert.Contains(claims, claim =>
            claim.Key == BridgeIdKeys.WikidataQid &&
            claim.Value == "Q548823");
    }

    private static ReconciliationAdapter CreateAdapter()
    {
        var config = new ReconciliationProviderConfig
        {
            InstanceOfClasses = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Music"] = ["Q105543609", "Q207628", "Q482994"],
                ["MusicAlbum"] = ["Q482994", "Q208569", "Q222910"],
                ["Comics"] = ["Q1004", "Q14406742"],
            },
            EditionPivot = new Dictionary<string, EditionPivotRuleEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["music"] = new()
                {
                    WorkClasses = ["Q482994"],
                    EditionClasses = ["Q2031291"],
                    PreferEdition = false,
                },
            },
            Reconciliation = new ReconciliationSettings
            {
                ReviewThreshold = 55,
            },
        };

        return new ReconciliationAdapter(
            config,
            new StubHttpClientFactory(),
            NullLogger<ReconciliationAdapter>.Instance,
            new StubFuzzyMatchingService());
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class StubFuzzyMatchingService : IFuzzyMatchingService
    {
        public double ComputeTokenSetRatio(string a, string b) => 0.0;

        public double ComputePartialRatio(string a, string b) => 0.0;

        public FieldMatchResult ScoreCandidate(LocalMetadata local, CandidateMetadata candidate) => new();
    }
}
