using MediaEngine.Contracts.Details;
using MediaEngine.Web.Services.Navigation;

namespace MediaEngine.Web.Tests;

public sealed class DetailTabNavigationTests
{
    [Fact]
    public void Resolve_NoRequestedTab_UsesFirstVisibleTab()
    {
        var model = CreateModel("overview", "cast", "details");

        var resolution = DetailTabNavigation.Resolve(model, null);

        Assert.Equal("overview", resolution.ActiveTab);
        Assert.False(resolution.HasRequestedTab);
        Assert.False(resolution.ShouldRedirect);
    }

    [Fact]
    public void Resolve_RequestedTab_IsCaseInsensitiveAndRedirectsToCanonicalSlug()
    {
        var model = CreateModel(["overview", "cast", "details"], withCast: true);

        var resolution = DetailTabNavigation.Resolve(model, "CAST");

        Assert.Equal("cast", resolution.ActiveTab);
        Assert.True(resolution.IsRequestedTabValid);
        Assert.True(resolution.ShouldRedirect);
    }

    [Fact]
    public void Resolve_InvalidTab_RedirectsToFirstVisibleTab()
    {
        var model = CreateModel("overview", "details");

        var resolution = DetailTabNavigation.Resolve(model, "missing");

        Assert.Equal("overview", resolution.ActiveTab);
        Assert.False(resolution.IsRequestedTabValid);
        Assert.True(resolution.ShouldRedirect);
    }

    [Fact]
    public void Resolve_CastTabWithoutRenderableCredits_FallsBack()
    {
        var model = CreateModel("overview", "cast", "details");

        var resolution = DetailTabNavigation.Resolve(model, "cast");

        Assert.Equal("overview", resolution.ActiveTab);
        Assert.False(resolution.IsRequestedTabValid);
        Assert.True(resolution.ShouldRedirect);
    }

    [Fact]
    public void BuildUrl_AppendsTabBeforePreservedQueryString()
    {
        var url = DetailTabNavigation.BuildUrl(
            "/watch/movie/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            "cast",
            new Uri("http://localhost/watch/movie/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa?collectionId=bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb&ignored=1"),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "collectionId" });

        Assert.Equal(
            "/watch/movie/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/cast?collectionId=bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            url);
    }

    [Fact]
    public void MediaNavigation_AppendsDetailTabBeforeExistingQuery()
    {
        var workId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var collectionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var route = MediaNavigation.ForMedia("Movies", workId, collectionId, "cast");

        Assert.Equal(
            "/watch/movie/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/cast?collectionId=bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            route);
    }

    private static DetailPageViewModel CreateModel(params string[] tabs) =>
        CreateModel(tabs, withCast: false);

    private static DetailPageViewModel CreateModel(string[] tabs, bool withCast) =>
        new()
        {
            Id = Guid.NewGuid().ToString("D"),
            EntityType = DetailEntityType.Movie,
            Title = "Test",
            Tabs = tabs.Select(tab => new DetailTab { Key = tab, Label = tab }).ToList(),
            ContributorGroups = withCast
                ? [new CreditGroupViewModel { Title = "Cast", GroupType = CreditGroupType.Cast }]
                : [],
        };
}
