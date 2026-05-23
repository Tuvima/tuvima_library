using System.Reflection;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;
using MediaEngine.Storage;
using MediaEngine.Storage.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Providers.Tests;

public sealed class SearchServiceUniverseSearchTests
{
    [Fact]
    public async Task SearchUniverse_ExactQid_DoesNotAppendCreatorHint()
    {
        var provider = new CapturingWikidataProvider();
        var service = BuildSearchService(provider);

        await service.SearchUniverseAsync(new SearchUniverseRequest(
            Query: "Q155653",
            MediaType: "Movies",
            MaxCandidates: 5,
            LocalAuthor: "Hayao Miyazaki"));

        Assert.NotNull(provider.LastRequest);
        Assert.Equal("Q155653", provider.LastRequest!.Title);
        Assert.Null(provider.LastRequest.Author);
    }

    [Fact]
    public async Task SearchUniverse_MovieSearch_DoesNotAppendCreatorHint()
    {
        var provider = new CapturingWikidataProvider();
        var service = BuildSearchService(provider);

        await service.SearchUniverseAsync(new SearchUniverseRequest(
            Query: "Spirited Away",
            MediaType: "Movies",
            MaxCandidates: 5,
            LocalAuthor: "Hayao Miyazaki"));

        Assert.NotNull(provider.LastRequest);
        Assert.Equal("Spirited Away", provider.LastRequest!.Title);
        Assert.Null(provider.LastRequest.Author);
    }

    [Fact]
    public async Task SearchUniverse_BookSearch_StillUsesCreatorHint()
    {
        var provider = new CapturingWikidataProvider();
        var service = BuildSearchService(provider);

        await service.SearchUniverseAsync(new SearchUniverseRequest(
            Query: "Dune",
            MediaType: "Books",
            MaxCandidates: 5,
            LocalAuthor: "Frank Herbert"));

        Assert.NotNull(provider.LastRequest);
        Assert.Equal("Dune Frank Herbert", provider.LastRequest!.Title);
        Assert.Equal("Frank Herbert", provider.LastRequest.Author);
    }

    [Fact]
    public void ReconciliationTitleConstraints_DoNotApplyBookAuthorConstraintToMovieOrExactQid()
    {
        var method = typeof(ReconciliationAdapter).GetMethod(
            "BuildTitleSearchConstraints",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var qidConstraints = InvokeBuildTitleSearchConstraints(method!, new ProviderLookupRequest
        {
            Title = "Q155653",
            MediaType = MediaType.Movies,
            Author = "Hayao Miyazaki",
        });
        Assert.Null(qidConstraints);

        var movieConstraints = InvokeBuildTitleSearchConstraints(method!, new ProviderLookupRequest
        {
            Title = "Spirited Away",
            MediaType = MediaType.Movies,
            Author = "Hayao Miyazaki",
        });
        Assert.Null(movieConstraints);

        var bookConstraints = InvokeBuildTitleSearchConstraints(method!, new ProviderLookupRequest
        {
            Title = "Dune",
            MediaType = MediaType.Books,
            Author = "Frank Herbert",
        });
        Assert.NotNull(bookConstraints);
        Assert.Equal("Frank Herbert", bookConstraints!["P50"]);
    }

    private static SearchService BuildSearchService(params IExternalMetadataProvider[] providers)
    {
        var configLoader = new ConfigurationDirectoryLoader(Path.Combine(FindRepoRoot(), "config"));
        return new SearchService(
            providers,
            configLoader,
            new StubFuzzyMatchingService(),
            new StubRetailMatchScoringService(),
            NullLogger<SearchService>.Instance);
    }

    private static Dictionary<string, string>? InvokeBuildTitleSearchConstraints(
        MethodInfo method,
        ProviderLookupRequest request) =>
        (Dictionary<string, string>?)method.Invoke(null, [request]);

    private static string FindRepoRoot()
    {
        var dir = Path.GetDirectoryName(typeof(SearchServiceUniverseSearchTests).Assembly.Location);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private sealed class CapturingWikidataProvider : IExternalMetadataProvider
    {
        public string Name => "wikidata_reconciliation";
        public ProviderDomain Domain => ProviderDomain.Universal;
        public IReadOnlyList<string> CapabilityTags => ["wikidata"];
        public Guid ProviderId => Guid.Parse("b3000003-d000-4000-8000-000000000004");
        public ProviderLookupRequest? LastRequest { get; private set; }

        public bool CanHandle(MediaType mediaType) => true;

        public bool CanHandle(EntityType entityType) => true;

        public Task<IReadOnlyList<ProviderClaim>> FetchAsync(
            ProviderLookupRequest request,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProviderClaim>>([]);

        public Task<IReadOnlyList<SearchResultItem>> SearchAsync(
            ProviderLookupRequest request,
            int limit = 25,
            CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult<IReadOnlyList<SearchResultItem>>(
            [
                new SearchResultItem(
                    Title: "Spirited Away",
                    Author: null,
                    Description: "2001 anime film directed by Hayao Miyazaki",
                    Year: "2001",
                    ThumbnailUrl: null,
                    ProviderItemId: "Q155653",
                    Confidence: 0.99,
                    ProviderName: Name),
            ]);
        }
    }

    private sealed class StubFuzzyMatchingService : IFuzzyMatchingService
    {
        public double ComputeTokenSetRatio(string a, string b) => 1.0;
        public double ComputePartialRatio(string a, string b) => 1.0;
        public FieldMatchResult ScoreCandidate(LocalMetadata local, CandidateMetadata candidate) =>
            new() { TitleScore = 1.0, AuthorScore = 1.0, YearScore = 1.0, CompositeScore = 1.0 };
    }

    private sealed class StubRetailMatchScoringService : IRetailMatchScoringService
    {
        public FieldMatchScores ScoreCandidate(
            IReadOnlyDictionary<string, string> fileHints,
            string? candidateTitle,
            string? candidateAuthor,
            string? candidateYear,
            MediaType mediaType,
            MatchTierConfig? matchTiers = null,
            CandidateExtendedMetadata? extendedMetadata = null,
            double structuralBonus = 0.0) =>
            new()
            {
                TitleScore = 1.0,
                AuthorScore = 1.0,
                YearScore = 1.0,
                FormatScore = 1.0,
                CompositeScore = 1.0,
            };
    }
}
