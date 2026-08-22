using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Editing;

namespace MediaEngine.Web.Tests;

public sealed class ReviewIssuePresentationBuilderTests
{
    [Fact]
    public void Build_RetailFailure_ExplainsTheOutcomeAndUsesActualKnownFacts()
    {
        var item = new ReviewItemViewModel
        {
            EntityTitle = "Captain Semaphore #404",
            MediaType = "Comics",
            EntityType = "Work",
            Trigger = "RetailMatchFailed",
            ConfidenceScore = 0,
            DetectedFacts = new Dictionary<string, string>
            {
                ["series"] = "Captain Semaphore",
                ["issue_number"] = "404",
                ["year"] = "2021",
            },
        };

        var presentation = ReviewIssuePresentationBuilder.Build(item);

        Assert.Contains("configured providers", presentation.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(presentation.FailureReasons, reason => reason.Contains("reliable enough", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(presentation.KnownFacts, fact => fact.Label == "Title" && fact.Value == "Captain Semaphore #404");
        Assert.Contains(presentation.KnownFacts, fact => fact.Label == "Series" && fact.Value == "Captain Semaphore");
        Assert.Contains(presentation.KnownFacts, fact => fact.Label == "Issue number" && fact.Value == "404");
        Assert.Contains("series", presentation.Guidance, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(presentation.KnownFacts, fact => fact.Value is "Unknown" or "N/A");
    }

    [Fact]
    public void Build_AudiobookIncompleteMetadata_UsesAudiobookSpecificAdvice()
    {
        var item = new ReviewItemViewModel
        {
            EntityTitle = "Untitled audiobook",
            MediaType = "Audiobooks",
            EntityType = "MediaAsset",
            Trigger = "PlaceholderTitle",
        };

        var presentation = ReviewIssuePresentationBuilder.Build(item);

        Assert.Contains("narrator", presentation.Guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("identifying information", presentation.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_CanonicalFailure_PreservesRetailContext()
    {
        var item = new ReviewItemViewModel
        {
            EntityTitle = "Matched edition",
            MediaType = "Books",
            EntityType = "Work",
            Trigger = "WikidataBridgeFailed",
            BridgeIdentifiers = new Dictionary<string, string> { ["isbn_13"] = "9780000000000" },
        };

        var presentation = ReviewIssuePresentationBuilder.Build(item);

        Assert.Contains("retail edition was identified", presentation.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(presentation.KnownFacts, fact => fact.Label == "ISBN-13");
    }
}
