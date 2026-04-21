using MediaEngine.Domain.Services;

namespace MediaEngine.Domain.Tests;

public sealed class AssetPathServiceTests
{
    [Fact]
    public void CentralArtworkPath_UsesDataAssetsTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "tuvima-domain-tests");
        var service = new AssetPathService(root);
        var ownerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var variantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var path = service.GetCentralAssetPath("Work", ownerId, "Background", variantId, ".jpg");

        Assert.Equal(
            Path.Combine(root, ".data", "assets", "artwork", "work", ownerId.ToString("D"), "background", $"{variantId:N}.jpg"),
            path);
    }

    [Fact]
    public void ExportedSubtitlePath_StaysBesideMediaFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "tuvima-domain-tests");
        var service = new AssetPathService(root);
        var mediaPath = Path.Combine("C:\\media", "Movies", "Arrival (2016)", "Arrival (2016).mkv");

        var subtitlePath = service.GetLocalSidecarPath(mediaPath, "Subtitle", ".srt");

        Assert.Equal(
            Path.Combine("C:\\media", "Movies", "Arrival (2016)", "Arrival (2016).srt"),
            subtitlePath);
    }

    [Fact]
    public void DiscAndClearArtPaths_UseDedicatedArtworkNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "tuvima-domain-tests");
        var service = new AssetPathService(root);
        var ownerId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var variantId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var mediaPath = Path.Combine("C:\\media", "Movies", "Arrival (2016)", "Arrival (2016).mkv");

        var discCentralPath = service.GetCentralAssetPath("Work", ownerId, "DiscArt", variantId, ".png");
        var clearCentralPath = service.GetCentralAssetPath("Work", ownerId, "ClearArt", variantId, ".png");
        var discSidecarPath = service.GetLocalSidecarPath(mediaPath, "DiscArt", ".png");
        var clearSidecarPath = service.GetLocalSidecarPath(mediaPath, "ClearArt", ".png");

        Assert.Equal(
            Path.Combine(root, ".data", "assets", "artwork", "work", ownerId.ToString("D"), "discart", $"{variantId:N}.png"),
            discCentralPath);
        Assert.Equal(
            Path.Combine(root, ".data", "assets", "artwork", "work", ownerId.ToString("D"), "clearart", $"{variantId:N}.png"),
            clearCentralPath);
        Assert.Equal(
            Path.Combine("C:\\media", "Movies", "Arrival (2016)", "discart.png"),
            discSidecarPath);
        Assert.Equal(
            Path.Combine("C:\\media", "Movies", "Arrival (2016)", "clearart.png"),
            clearSidecarPath);
    }

    [Fact]
    public void DerivedHeroPath_UsesCentralDerivedFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "tuvima-domain-tests");
        var service = new AssetPathService(root);
        var ownerId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var heroPath = service.GetCentralDerivedPath("Work", ownerId, "hero", "hero.jpg");

        Assert.Equal(
            Path.Combine(root, ".data", "assets", "derived", "work", ownerId.ToString("D"), "hero", "hero.jpg"),
            heroPath);
    }

    [Fact]
    public void PersonHeadshotPath_UsesCentralPeopleFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "tuvima-domain-tests");
        var service = new AssetPathService(root);
        var personId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var headshotPath = service.GetPersonHeadshotPath(personId);

        Assert.Equal(
            Path.Combine(root, ".data", "assets", "people", personId.ToString("D"), "headshot.jpg"),
            headshotPath);
    }

    [Fact]
    public void CharacterPortraitPath_UsesCentralPeopleFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "tuvima-domain-tests");
        var service = new AssetPathService(root);
        var personId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var characterId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        var portraitPath = service.GetCharacterPortraitPath(personId, characterId);

        Assert.Equal(
            Path.Combine(root, ".data", "assets", "people", personId.ToString("D"), "characters", characterId.ToString("D"), "portrait.jpg"),
            portraitPath);
    }
}
