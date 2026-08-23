using MediaEngine.Api.Services.Metadata;

namespace MediaEngine.Api.Tests;

public sealed class ArtworkTypeRetirementTests
{
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
}
