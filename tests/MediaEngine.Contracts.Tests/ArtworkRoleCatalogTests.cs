using MediaEngine.Contracts.Artwork;

namespace MediaEngine.Contracts.Tests;

public sealed class ArtworkRoleCatalogTests
{
    [Theory]
    [InlineData("Work", "Movies", null, "Primary", "CoverArt", "poster-cover")]
    [InlineData("Work", "Books", null, "Primary", "CoverArt", "cover")]
    [InlineData("Work", "Audiobooks", null, "Primary", "CoverArt", "cover")]
    [InlineData("Work", "TV", "Season", "Primary", "SeasonPoster", "poster")]
    [InlineData("Work", "TV", "Episode", "Primary", "EpisodeStill", "still")]
    [InlineData("Person", null, null, "Portrait", "Headshot", "portrait")]
    [InlineData("FictionalEntity", null, "Character", "Portrait", "CharacterPortrait", "portrait")]
    [InlineData("Collection", null, "Universe", "Primary", "CoverArt", "primary-artwork")]
    public void Resolve_ProvidesStableDefaultRoleAndSlot(
        string entityType,
        string? mediaType,
        string? groupKind,
        string expectedRole,
        string expectedSourceAssetType,
        string expectedPresentationKey)
    {
        var roles = ArtworkRoleCatalog.Resolve(entityType, mediaType, groupKind);

        var role = Assert.Single(roles, item => item.IsDefault);
        Assert.Equal(expectedRole, role.Role);
        Assert.Equal(expectedSourceAssetType, role.SourceAssetType);
        Assert.Equal(expectedPresentationKey, role.PresentationKey);
    }

    [Fact]
    public void Resolve_EpisodeDoesNotRenderUnsupportedStaticRoles()
    {
        var roles = ArtworkRoleCatalog.Resolve("Work", "TV", "Episode", ["EpisodeStill"]);

        var role = Assert.Single(roles);
        Assert.Equal("Primary", role.Role);
        Assert.DoesNotContain(roles, item => item.Role is "Background" or "Logo");
    }
}
