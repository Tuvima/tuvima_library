using MediaEngine.Web.Services.Editing;

namespace MediaEngine.Web.Tests;

public sealed class ReviewTargetResolverTests
{
    [Fact]
    public void Resolve_AudiobookQidGap_TargetsNarratorIdentity()
    {
        var target = ReviewTargetResolver.Resolve("Audiobooks", "QidNoMatch");

        Assert.Equal("links", target.InitialTab);
        Assert.Equal("narrator", target.CanonicalTargetGroup);
        Assert.Equal("narrator", target.FocusField);
        Assert.Equal(MediaEditorIdentityIntent.FixWikidataMatch, target.Intent);
    }

    [Fact]
    public void Resolve_TvReview_TargetsShowEpisodeIdentity()
    {
        var target = ReviewTargetResolver.Resolve("TV", "RetailMatchFailed");

        Assert.Equal("links", target.InitialTab);
        Assert.Equal("show_episode", target.CanonicalTargetGroup);
        Assert.Equal("show_name", target.FocusField);
        Assert.Equal(MediaEditorIdentityIntent.FixRetailMatch, target.Intent);
    }

    [Theory]
    [InlineData("RetailMatchFailed", MediaEditorIdentityIntent.FixRetailMatch, "Find Retail Match")]
    [InlineData("RetailMatchAmbiguous", MediaEditorIdentityIntent.ConfirmRetailMatch, "Confirm Retail Match")]
    [InlineData("WikidataBridgeFailed", MediaEditorIdentityIntent.FixWikidataMatch, "Fix Wikidata Match")]
    [InlineData("MissingQid", MediaEditorIdentityIntent.MarkWikidataMissing, "Mark Provider-Only")]
    [InlineData("MultipleQidMatches", MediaEditorIdentityIntent.FixWikidataMatch, "Fix Wikidata Match")]
    [InlineData("ArtworkUnconfirmed", MediaEditorIdentityIntent.ConfirmArtwork, "Review Artwork")]
    [InlineData("AmbiguousMediaType", MediaEditorIdentityIntent.ReclassifyMediaType, "Change Media Type")]
    public void Resolve_Trigger_MapsIntentAndPrimaryAction(string trigger, MediaEditorIdentityIntent intent, string label)
    {
        var target = ReviewTargetResolver.Resolve("Comics", trigger);

        Assert.Equal(intent, target.Intent);
        Assert.Equal(label, target.PrimaryActionLabel);
    }
}
