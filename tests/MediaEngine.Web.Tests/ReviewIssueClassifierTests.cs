using MediaEngine.Web.Services.Editing;

namespace MediaEngine.Web.Tests;

public sealed class ReviewIssueClassifierTests
{
    [Theory]
    [InlineData("MissingQid", ReviewIssueBucket.QuickFix)]
    [InlineData("ArtworkUnconfirmed", ReviewIssueBucket.QuickFix)]
    [InlineData("RetailMatchAmbiguous", ReviewIssueBucket.ManualReview)]
    [InlineData("AmbiguousMediaType", ReviewIssueBucket.ManualReview)]
    [InlineData("WritebackFailed", ReviewIssueBucket.HighPriority)]
    [InlineData("StagedUnidentifiable", ReviewIssueBucket.HighPriority)]
    public void Classify_UsesStableTriggerBuckets(string trigger, ReviewIssueBucket expected)
    {
        Assert.Equal(expected, ReviewIssueClassifier.Classify(trigger).Bucket);
    }

    [Fact]
    public void Classify_UnknownTrigger_RemainsVisibleForManualReview()
    {
        var presentation = ReviewIssueClassifier.Classify("FutureTrigger");

        Assert.Equal(ReviewIssueBucket.ManualReview, presentation.Bucket);
        Assert.False(string.IsNullOrWhiteSpace(presentation.Explanation));
    }
}
