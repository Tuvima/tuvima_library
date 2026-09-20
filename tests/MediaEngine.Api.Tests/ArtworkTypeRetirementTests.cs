using MediaEngine.Api.Services.Metadata;

namespace MediaEngine.Api.Tests;

public sealed class ArtworkTypeRetirementTests
{
    [Theory]
    [InlineData("Books", "series")]
    [InlineData("Audiobooks", "series")]
    [InlineData("Comics", "series")]
    [InlineData("Movies", "series")]
    public void StructuralShelvesOwnACustomCover(string mediaType, string scope)
        => Assert.Equal(["CoverArt"], ArtworkScopeService.GetScopedArtworkSlots(mediaType, scope));

    [Theory]
    [InlineData("Books", "book")]
    [InlineData("Audiobooks", "audiobook")]
    [InlineData("Comics", "issue")]
    public void LeafScopeNamesExposeTheirOwnedArtwork(string mediaType, string scope)
        => Assert.Equal(["CoverArt", "Background", "Logo"], ArtworkScopeService.GetScopedArtworkSlots(mediaType, scope));

    [Theory]
    [InlineData("Movies", "item")]
    [InlineData("TV", "series")]
    [InlineData("Music", "album")]
    [InlineData("Books", "item")]
    [InlineData("Audiobooks", "item")]
    [InlineData("Comics", "item")]
    public void ScopedArtworkSlots_DoNotExposeBanner(string mediaType, string scopeId)
    {
        var slots = ArtworkScopeService.GetScopedArtworkSlots(mediaType, scopeId);

        Assert.DoesNotContain("Banner", slots, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("banner")]
    [InlineData("Banner")]
    public void UploadedArtworkType_DoesNotAcceptBanner(string value)
    {
        Assert.Null(ArtworkScopeService.NormalizeUploadedArtworkType(value));
    }

    [Fact]
    public void TvSeasonScope_ExposesOnlySeasonArtwork()
    {
        var slots = ArtworkScopeService.GetScopedArtworkSlots("TV", "season");

        Assert.Equal(["SeasonPoster", "SeasonThumb"], slots);
    }
}
