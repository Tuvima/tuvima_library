using Bunit;
using MediaEngine.Web.Components.Shared;
using MediaEngine.Web.Components.View;
using MediaEngine.Web.Tests.Support;

namespace MediaEngine.Web.Tests;

public sealed class ViewRemediationInteractionTests : AsyncBunitContext
{
    [Fact]
    public void TypingTagDoesNotCommit_ExplicitSubmitCommitsWholePhrase()
    {
        var saved = new List<string>();
        var cut = Render<AppTagInput>(p => p
            .Add(x => x.Search, (_, _) => Task.FromResult<IEnumerable<string>>(["Summer vacation"]))
            .Add(x => x.Commit, value => { saved.Add(value); return Task.FromResult(true); }));
        foreach (var draft in new[] { "S", "Su", "Summer vacation" }) cut.Find("input").Input(draft);
        Assert.Empty(saved);
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Equal(["Summer vacation"], saved));
        Assert.Equal("", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void FailedTagCommitRetainsDraftForRetry()
    {
        var cut = Render<AppTagInput>(p => p
            .Add(x => x.Search, (_, _) => Task.FromResult<IEnumerable<string>>([]))
            .Add(x => x.Commit, _ => Task.FromResult(false)));
        cut.Find("input").Input("Tokyo trip");
        cut.Find("form").Submit();
        Assert.Equal("Tokyo trip", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void ScaleUsesSamePositionForEveryRepresentationOfDate()
    {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var scale = new PlacesTimeScale(start, start.AddYears(2));
        foreach (var tick in scale.Ticks())
            Assert.Equal(scale.Percent(tick), scale.Percent(scale.Index(scale.DateAt(tick))));
        Assert.Equal(0, scale.Percent(0));
        Assert.Equal(100, scale.Percent(scale.Days));
    }

    [Fact]
    public void SingleDateHasNonzeroDomainAndUniqueTicks()
    {
        var date = new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero);
        var scale = new PlacesTimeScale(date, date);
        Assert.True(scale.Days > 0);
        Assert.Equal(scale.Ticks().Count(), scale.Ticks().Distinct().Count());
        Assert.Equal(date, scale.DateAt(scale.Index(date)));
    }
}
