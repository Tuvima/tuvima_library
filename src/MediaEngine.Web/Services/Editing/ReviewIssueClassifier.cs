using MediaEngine.Domain.Enums;

namespace MediaEngine.Web.Services.Editing;

public enum ReviewIssueBucket
{
    All,
    QuickFix,
    ManualReview,
    HighPriority,
}

public sealed record ReviewIssuePresentation(
    ReviewIssueBucket Bucket,
    string Label,
    string Explanation,
    bool HighPriority);

public static class ReviewIssueClassifier
{
    public static ReviewIssuePresentation Classify(string? trigger)
    {
        var rootCause = ReviewRootCauseExtensions.FromTrigger(trigger);
        return trigger switch
        {
            "ArtworkUnconfirmed" => new(ReviewIssueBucket.QuickFix, "Confirm artwork", "The artwork came from a less precise search and needs confirmation.", false),
            "MissingQid" or "WikidataBridgeFailed" => new(ReviewIssueBucket.QuickFix, "No canonical identity found", "The retail match is retained, but no Wikidata identity could be confirmed.", false),
            "WritebackFailed" => new(ReviewIssueBucket.HighPriority, "File write-back failed", "Metadata could not be written to the physical file after retrying.", true),
            "StagedUnidentifiable" => new(ReviewIssueBucket.HighPriority, "Item could not be identified", "The file is blocked in staging because there is not enough reliable identity evidence.", true),
            "LanguageMismatch" => new(ReviewIssueBucket.ManualReview, "Language needs confirmation", "The file language conflicts with the configured library language.", false),
            "AmbiguousMediaType" or "RootWatchFolder" => new(ReviewIssueBucket.ManualReview, "Media type is uncertain", "The file could belong to more than one media lane and needs a person to choose.", false),
            "RetailMatchAmbiguous" => new(ReviewIssueBucket.ManualReview, "Retail match needs confirmation", "More than one retail record may match the detected item.", false),
            "MultipleQidMatches" => new(ReviewIssueBucket.ManualReview, "Canonical identity needs confirmation", "More than one Wikidata identity may match the confirmed retail record.", false),
            "RetailMatchFailed" => new(ReviewIssueBucket.ManualReview, "No retail match", "No configured retail provider returned a sufficiently reliable match.", false),
            "PlaceholderTitle" => new(ReviewIssueBucket.ManualReview, "Local metadata is incomplete", "A usable title or identifier is required before retail matching can continue.", false),
            "MetadataConflict" => new(ReviewIssueBucket.ManualReview, "Metadata sources disagree", "Two or more sources supplied similarly credible but conflicting values.", false),
            _ when rootCause == ReviewRootCause.EnrichmentIncomplete => new(ReviewIssueBucket.QuickFix, "Enrichment incomplete", "The item is identified but one enrichment step needs confirmation.", false),
            _ => new(ReviewIssueBucket.ManualReview, "Manual review needed", "The ingestion pipeline could not safely make this decision automatically.", false),
        };
    }
}
