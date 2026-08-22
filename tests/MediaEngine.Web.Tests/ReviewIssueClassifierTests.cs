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

    [Theory]
    [InlineData("RetailMatchFailed", "No retail match")]
    [InlineData("RetailMatchAmbiguous", "Retail match needs confirmation")]
    [InlineData("MissingQid", "No canonical identity found")]
    [InlineData("MultipleQidMatches", "Canonical identity needs confirmation")]
    public void Classify_UsesPipelineStageSpecificLabels(string trigger, string expectedLabel)
    {
        Assert.Equal(expectedLabel, ReviewIssueClassifier.Classify(trigger).Label);
    }

    [Theory]
    [InlineData("RetailMatchFailed", ReviewIssueClassifier.CategoryNoProviderMatch)]
    [InlineData("PlaceholderTitle", ReviewIssueClassifier.CategoryIncompleteMetadata)]
    [InlineData("StagedUnidentifiable", ReviewIssueClassifier.CategoryIncompleteMetadata)]
    [InlineData("PossibleDuplicate", ReviewIssueClassifier.CategoryDuplicates)]
    [InlineData("ArtworkUnconfirmed", ReviewIssueClassifier.CategoryQuickFix)]
    public void CategoryFor_UsesUserFacingQueueCategories(string trigger, string expected)
    {
        Assert.Equal(expected, ReviewIssueClassifier.CategoryFor(trigger));
    }

    [Fact]
    public void ManualReviewCategory_IncludesHighPriorityIssues()
    {
        Assert.True(ReviewIssueClassifier.MatchesCategory("WritebackFailed", ReviewIssueClassifier.CategoryManualReview));
    }
}
