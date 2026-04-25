using System.Text.Json;
using MediaEngine.Contracts.Display;

namespace MediaEngine.Api.Tests;

public sealed class DisplayContractTests
{
    [Fact]
    public void DisplayDtoSerialization_UsesStableCamelCaseContract()
    {
        var workId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var assetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var collectionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var primaryAction = new DisplayActionDto("playAsset", "Play", WorkId: workId, AssetId: assetId, WebUrl: $"/watch/{assetId}");
        var card = new DisplayCardDto(
            Id: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            WorkId: workId,
            AssetId: assetId,
            CollectionId: collectionId,
            MediaType: "Movie",
            GroupingType: "work",
            Title: "Arrival",
            Subtitle: null,
            Facts: ["2016", "Science Fiction"],
            Artwork: new DisplayArtworkDto(
                CoverUrl: "/stream/22222222-2222-2222-2222-222222222222/cover",
                SquareUrl: null,
                BannerUrl: null,
                BackgroundUrl: null,
                LogoUrl: null,
                CoverWidthPx: 1000,
                CoverHeightPx: 1500,
                SquareWidthPx: null,
                SquareHeightPx: null,
                BannerWidthPx: null,
                BannerHeightPx: null,
                BackgroundWidthPx: null,
                BackgroundHeightPx: null,
                AccentColor: "#111111"),
            PreferredShape: "portrait",
            Presentation: "default",
            TileTextMode: "coverOnly",
            PreviewPlacement: "bottom",
            Progress: new DisplayProgressDto(Percent: 42, Label: "42%", LastAccessed: DateTimeOffset.Parse("2026-04-24T12:00:00Z"), ResumeAction: primaryAction),
            Actions: [primaryAction],
            Flags: new DisplayCardFlagsDto(IsPlayable: true, IsReadable: false, CanAddToCollection: true, IsCollection: false, IsFavorite: false),
            SortTimestamp: DateTimeOffset.Parse("2026-04-24T12:00:00Z"));

        var page = new DisplayPageDto(
            Key: "home",
            Title: "Home",
            Subtitle: null,
            Hero: new DisplayHeroDto("Continue", "Pick up where you left off.", "home-hero", card.Artwork, card.Progress, card.Actions),
            Shelves: [new DisplayShelfDto("continue", "Continue", "continue", [card], "/api/v1/display/continue")],
            Catalog: [card]);

        var json = JsonSerializer.Serialize(page, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"key\":\"home\"", json, StringComparison.Ordinal);
        Assert.Contains("\"workId\":\"11111111-1111-1111-1111-111111111111\"", json, StringComparison.Ordinal);
        Assert.Contains("\"assetId\":\"22222222-2222-2222-2222-222222222222\"", json, StringComparison.Ordinal);
        Assert.Contains("\"preferredShape\":\"portrait\"", json, StringComparison.Ordinal);
        Assert.Contains("\"tileTextMode\":\"coverOnly\"", json, StringComparison.Ordinal);
        Assert.Contains("\"previewPlacement\":\"bottom\"", json, StringComparison.Ordinal);
        Assert.Contains("\"progress\":{\"percent\":42", json, StringComparison.Ordinal);
        Assert.Contains("\"actions\":[{\"type\":\"playAsset\",\"label\":\"Play\"", json, StringComparison.Ordinal);
        Assert.Contains("\"flags\":{\"isPlayable\":true", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"WorkId\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayEndpoints_AreVersionedConsumerEndpointsAndMappedInProgram()
    {
        var endpointSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\DisplayEndpoints.cs"));
        var programSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Program.cs"));

        Assert.Contains("app.MapGroup(\"/api/v1/display\")", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/home\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/browse\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/continue\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/search\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/groups/{groupId:guid}\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("includeCatalog", endpointSource, StringComparison.Ordinal);
        Assert.Contains("RequireAnyRole", endpointSource, StringComparison.Ordinal);
        Assert.Contains("app.MapDisplayEndpoints();", programSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayBrowse_LaneRequestsUseRichConsumerPageComposition()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Services\Display\DisplayComposerService.cs"));
        var cardBuilderSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Services\Display\DisplayCardBuilder.cs"));
        var shelfBuilderSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Services\Display\DisplayShelfBuilder.cs"));

        Assert.Contains("BuildLaneAsync(normalizedLane, includeCatalog, ct)", source, StringComparison.Ordinal);
        Assert.Contains("DisplayShelfBuilder", source, StringComparison.Ordinal);
        Assert.Contains("BuildWatchShelves", shelfBuilderSource, StringComparison.Ordinal);
        Assert.Contains("BuildReadShelves", shelfBuilderSource, StringComparison.Ordinal);
        Assert.Contains("BuildListenShelves", shelfBuilderSource, StringComparison.Ordinal);
        Assert.Contains("\"continue-watching\"", shelfBuilderSource, StringComparison.Ordinal);
        Assert.Contains("\"continue-reading\"", shelfBuilderSource, StringComparison.Ordinal);
        Assert.Contains("\"continue-listening\"", shelfBuilderSource, StringComparison.Ordinal);
        Assert.Contains("\"openCollection\"", cardBuilderSource, StringComparison.Ordinal);
    }

    [Fact]
    public void WebBrowseSurfaces_UseDisplayApiBeforeLegacyComposition()
    {
        var clientSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Services\Integration\IEngineApiClient.cs"));
        var composerSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Services\Discovery\DiscoveryComposerService.cs"));
        var browseShellSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Browse\MediaBrowseShell.razor"));

        Assert.Contains("GetDisplayBrowseAsync", clientSource, StringComparison.Ordinal);
        Assert.Contains("GetDisplayBrowseAsync(lane: \"read\"", composerSource, StringComparison.Ordinal);
        Assert.Contains("GetDisplayBrowseAsync(lane: \"watch\"", composerSource, StringComparison.Ordinal);
        Assert.Contains("GetDisplayBrowseAsync(lane: \"listen\"", composerSource, StringComparison.Ordinal);
        Assert.Contains("LoadDisplayCardsAsync", browseShellSource, StringComparison.Ordinal);
        Assert.Contains("DiscoveryComposerService.FromDisplayCard", browseShellSource, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
