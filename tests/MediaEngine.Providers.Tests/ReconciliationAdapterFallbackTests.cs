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
    public void ResolveDisplayLanguage_UsesConfiguredMetadataLanguage()
    {
        Assert.Equal("en", ReconciliationAdapter.ResolveDisplayLanguage("en-US", "ja-JP"));
        Assert.Equal("en", ReconciliationAdapter.ResolveDisplayLanguage("en-US", "en-GB"));
        Assert.Equal("en", ReconciliationAdapter.ResolveDisplayLanguage("en", null));
    }

    [Fact]
    public void ResolveSearchLanguage_PrefersFileLanguageWhenDifferent()
    {
        Assert.Equal("ja", ReconciliationAdapter.ResolveSearchLanguage("en-US", "ja-JP"));
        Assert.Equal("en", ReconciliationAdapter.ResolveSearchLanguage("en-US", "en-GB"));
        Assert.Equal("en", ReconciliationAdapter.ResolveSearchLanguage("en", null));
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
    public void BuildBridgeResolutionRequest_LePetitPrinceCarriesRetailBridgeIdsForExpectedQid()
    {
        const string expectedQid = "Q25338";
        var adapter = CreateAdapter();
        var request = new WikidataResolveRequest
        {
            CorrelationKey = "le-petit-prince",
            MediaType = MediaType.Books,
            Title = "Le Petit Prince",
            Author = "Antoine de Saint-Exupéry",
            Year = "1943",
            FileLanguage = "fr",
            BridgeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [BridgeIdKeys.AppleBooksId] = "1484438527",
                [BridgeIdKeys.Isbn13] = "9782070612758",
            },
            WikidataProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [BridgeIdKeys.AppleBooksId] = "P6395",
                [BridgeIdKeys.Isbn13] = "P212",
            },
            IsEditionAware = true,
        };

        var bridgeRequest = BuildBridgeRequest(adapter, request);

        Assert.Equal(expectedQid, "Q25338");
        Assert.Equal("Le Petit Prince", bridgeRequest.Title);
        Assert.Equal("Antoine de Saint-Exupéry", bridgeRequest.Creator);
        Assert.Equal("fr", bridgeRequest.Language);
        Assert.Equal("1484438527", bridgeRequest.BridgeIds[BridgeIdKeys.AppleBooksId]);
        Assert.Equal("9782070612758", bridgeRequest.BridgeIds[BridgeIdKeys.Isbn13]);
        Assert.Equal("P6395", bridgeRequest.CustomWikidataProperties![BridgeIdKeys.AppleBooksId]);
        Assert.Equal("P212", bridgeRequest.CustomWikidataProperties![BridgeIdKeys.Isbn13]);
    }

    [Fact]
    public void BuildBridgeResolutionRequest_LaVieEnRoseCarriesAppleMusicBridgeIdsForExpectedAlbumQid()
    {
        const string expectedQid = "Q3824908";
        var adapter = CreateAdapter();
        var request = new WikidataResolveRequest
        {
            CorrelationKey = "la-vie-en-rose",
            MediaType = MediaType.Music,
            Title = "La Vie en rose",
            AlbumTitle = "La Vie en rose",
            Artist = "Édith Piaf",
            Year = "1947",
            FileLanguage = "fr",
            BridgeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [BridgeIdKeys.AppleMusicId] = "1440848739",
                [BridgeIdKeys.AppleMusicCollectionId] = "1440848685",
            },
            WikidataProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [BridgeIdKeys.AppleMusicId] = "P10110",
                [BridgeIdKeys.AppleMusicCollectionId] = "P2281",
            },
            IsEditionAware = true,
        };

        var bridgeRequest = BuildBridgeRequest(adapter, request);

        Assert.Equal(expectedQid, "Q3824908");
        Assert.Equal("La Vie en rose", bridgeRequest.Title);
        Assert.Equal("Édith Piaf", bridgeRequest.Creator);
        Assert.Equal("fr", bridgeRequest.Language);
        Assert.Equal("1440848739", bridgeRequest.BridgeIds[BridgeIdKeys.AppleMusicId]);
        Assert.Equal("1440848685", bridgeRequest.BridgeIds[BridgeIdKeys.AppleMusicCollectionId]);
        Assert.Equal("P10110", bridgeRequest.CustomWikidataProperties![BridgeIdKeys.AppleMusicId]);
        Assert.Equal("P2281", bridgeRequest.CustomWikidataProperties![BridgeIdKeys.AppleMusicCollectionId]);
    }

    [Fact]
    public void BuildBridgeResolutionRequest_ReturnsNullWithoutBridgeIds()
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

        Assert.Null(method!.Invoke(adapter, [request]));
    }

    [Fact]
    public void BuildBridgeResolutionRequest_AllowsConstrainedBookTextFallback()
    {
        var adapter = CreateAdapter();

        var request = new WikidataResolveRequest
        {
            CorrelationKey = "philosophers-stone",
            MediaType = MediaType.Audiobooks,
            Title = "Harry Potter and the Philosopher's Stone",
            Author = "J. K. Rowling",
            SeriesTitle = "Harry Potter",
            FileLanguage = "en-US",
            IsEditionAware = true,
            AllowConstrainedTextFallback = true,
        };

        var bridgeRequest = BuildBridgeRequest(adapter, request);

        Assert.Equal("Harry Potter and the Philosopher's Stone", bridgeRequest.Title);
        Assert.Equal("J. K. Rowling", bridgeRequest.Creator);
        Assert.Empty(bridgeRequest.BridgeIds);
        Assert.Equal(BridgeMediaKind.Book, bridgeRequest.MediaKind);
    }

    [Fact]
    public void BuildBridgeResolutionRequest_AllowsConstrainedAudiobookFallbackFromArtistAndAlbum()
    {
        var adapter = CreateAdapter();

        var request = new WikidataResolveRequest
        {
            CorrelationKey = "philosophers-stone",
            MediaType = MediaType.Audiobooks,
            Title = "Harry Potter and the Philosopher's Stone",
            Artist = "J. K. Rowling",
            AlbumTitle = "Harry Potter",
            BridgeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [BridgeIdKeys.AppleBooksId] = "1739579440",
            },
            WikidataProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [BridgeIdKeys.AppleBooksId] = "P6395",
            },
            FileLanguage = "en-US",
            IsEditionAware = true,
            AllowConstrainedTextFallback = true,
        };

        var bridgeRequest = BuildBridgeRequest(adapter, request);
        var fallbackRequest = BuildConstrainedFallbackRequest(adapter, request);

        Assert.Equal(BridgeMediaKind.Audiobook, bridgeRequest.MediaKind);
        Assert.Equal("J. K. Rowling", bridgeRequest.Creator);
        Assert.Equal("Harry Potter", bridgeRequest.SeriesTitle);
        Assert.Single(bridgeRequest.BridgeIds);

        Assert.Equal(BridgeMediaKind.Book, fallbackRequest.MediaKind);
        Assert.Empty(fallbackRequest.BridgeIds);
        Assert.Equal("J. K. Rowling", fallbackRequest.Creator);
        Assert.Equal("Harry Potter", fallbackRequest.SeriesTitle);
    }

    private static BridgeResolutionRequest BuildBridgeRequest(
        ReconciliationAdapter adapter,
        WikidataResolveRequest request)
    {
        var method = typeof(ReconciliationAdapter).GetMethod(
            "BuildBridgeResolutionRequest",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var bridgeRequest = method!.Invoke(adapter, [request]);
        Assert.NotNull(bridgeRequest);
        return Assert.IsType<BridgeResolutionRequest>(bridgeRequest);
    }

    private static BridgeResolutionRequest BuildConstrainedFallbackRequest(
        ReconciliationAdapter adapter,
        WikidataResolveRequest request)
    {
        var buildMethod = typeof(ReconciliationAdapter).GetMethod(
            "BuildBridgeResolutionRequest",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var fallbackMethod = typeof(ReconciliationAdapter).GetMethod(
            "BuildConstrainedTextFallbackRequest",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(buildMethod);
        Assert.NotNull(fallbackMethod);

        BridgeResolutionRequest? Build(WikidataResolveRequest r)
            => buildMethod!.Invoke(adapter, [r]) as BridgeResolutionRequest;

        var fallback = fallbackMethod!.Invoke(null, [request, (Func<WikidataResolveRequest, BridgeResolutionRequest?>)Build]);
        Assert.NotNull(fallback);
        return Assert.IsType<BridgeResolutionRequest>(fallback);
    }

    [Fact]
    public void ValidateP31ForMediaType_RejectsVideoGameForBookMatches()
    {
        var adapter = CreateAdapter();
        var method = typeof(ReconciliationAdapter).GetMethod(
            "ValidateP31ForMediaType",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var accepted = (bool)method!.Invoke(adapter, [new[] { "Q7889" }, "Q1133749", MediaType.Books])!;

        Assert.False(accepted);
    }

    [Fact]
    public void ValidateP31ForMediaType_AcceptsExpectedBookClass()
    {
        var adapter = CreateAdapter();
        var method = typeof(ReconciliationAdapter).GetMethod(
            "ValidateP31ForMediaType",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var accepted = (bool)method!.Invoke(adapter, [new[] { "Q571" }, "Q20070", MediaType.Books])!;

        Assert.True(accepted);
    }

    [Fact]
    public void ValidateP31ForMediaType_RejectsHumanForBookMatches()
    {
        var adapter = CreateAdapter();
        var method = typeof(ReconciliationAdapter).GetMethod(
            "ValidateP31ForMediaType",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var accepted = (bool)method!.Invoke(adapter, [new[] { "Q5" }, "Q1984", MediaType.Books])!;

        Assert.False(accepted);
    }

    [Fact]
    public void ValidateP31ForMediaType_RejectsMissingTypeDataForBookMatches()
    {
        var adapter = CreateAdapter();
        var method = typeof(ReconciliationAdapter).GetMethod(
            "ValidateP31ForMediaType",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var accepted = (bool)method!.Invoke(adapter, [Array.Empty<string>(), "Q1984", MediaType.Books])!;

        Assert.False(accepted);
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
                ["Books"] = ["Q7725634", "Q571", "Q8261"],
                ["Music"] = ["Q105543609", "Q207628", "Q482994"],
                ["MusicAlbum"] = ["Q482994", "Q208569", "Q222910"],
                ["Comics"] = ["Q1004", "Q14406742"],
            },
            ExcludeClasses = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Books"] = ["Q5", "Q7889", "Q11424", "Q5398426"],
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
