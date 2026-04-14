using MediaEngine.Web.Services.Editing;

namespace MediaEngine.Web.Tests;

public sealed class ReviewTargetResolverTests
{
    [Fact]
    public void Resolve_AudiobookQidGap_TargetsNarratorIdentity()
    {
        var target = ReviewTargetResolver.Resolve("Audiobooks", "QidNoMatch");

        Assert.Equal("details", target.InitialTab);
        Assert.Equal("narrator", target.CanonicalTargetGroup);
        Assert.Equal("narrator", target.FocusField);
    }

    [Fact]
    public void Resolve_TvReview_TargetsShowEpisodeIdentity()
    {
        var target = ReviewTargetResolver.Resolve("TV", "RetailMatchFailed");

        Assert.Equal("details", target.InitialTab);
        Assert.Equal("show_episode", target.CanonicalTargetGroup);
        Assert.Equal("show_name", target.FocusField);
    }
}
