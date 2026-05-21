using System.Text.Json;
using MediaEngine.Contracts.Details;
using MediaEngine.Contracts.Display;
using MediaEngine.Contracts.Paging;
using MediaEngine.Contracts.Playback;
using MediaEngine.Contracts.Settings;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Contracts.Tests;

public sealed class ContractJsonRoundTripTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void DetailsDto_RoundTripsRepresentativeShape()
    {
        var dto = new DetailPageViewModel
        {
            Id = "work-1",
            EntityType = DetailEntityType.Book,
            PresentationContext = DetailPresentationContext.Read,
            Title = "The Left Hand of Darkness",
            Subtitle = "Hainish Cycle",
            Artwork = new ArtworkSet
            {
                CoverUrl = "/images/cover.jpg",
                PrimaryColor = "#101820",
                DominantColors = ["#101820", "#f2aa4c"],
            },
            Metadata =
            [
                new MetadataPill
                {
                    Label = "1969",
                    Kind = "year",
                    Tooltip = "Published in 1969",
                },
            ],
            PrimaryActions =
            [
                new DetailAction
                {
                    Key = "read",
                    Label = "Read",
                    Route = "/read/work-1",
                    IsPrimary = true,
                },
            ],
            OwnedFormats =
            [
                new OwnedFormatViewModel
                {
                    Id = "edition-1",
                    FormatType = MediaFormatType.Ebook,
                    DisplayName = "EPUB",
                    PageCount = 304,
                },
            ],
            IdentityStatus = CanonicalIdentityStatus.ProviderMatched,
            LibraryStatus = LibraryStatus.Owned,
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<DetailPageViewModel>(json, JsonOptions);

        Assert.NotNull(roundTrip);
        Assert.Equal(dto.Id, roundTrip.Id);
        Assert.Equal(dto.EntityType, roundTrip.EntityType);
        Assert.Equal(dto.Artwork.CoverUrl, roundTrip.Artwork.CoverUrl);
        Assert.Equal(dto.Metadata[0].Label, roundTrip.Metadata[0].Label);
        Assert.Equal(dto.OwnedFormats[0].FormatType, roundTrip.OwnedFormats[0].FormatType);
        Assert.Contains("\"entityType\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"EntityType\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayDto_RoundTripsRepresentativeShape()
    {
        var workId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var assetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var action = new DisplayActionDto("playAsset", "Play", WorkId: workId, AssetId: assetId);
        var card = new DisplayCardDto(
            Id: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            WorkId: workId,
            AssetId: assetId,
            CollectionId: null,
            MediaType: "Movie",
            GroupingType: "work",
            Title: "Arrival",
            Subtitle: "2016",
            Facts: ["PG-13", "116 min"],
            Artwork: new DisplayArtworkDto("/cover.jpg", null, null, "/bg.jpg", null, 1000, 1500, null, null, null, null, 1920, 1080, "#112233"),
            PreferredShape: "portrait",
            Presentation: "default",
            TileTextMode: "coverOnly",
            PreviewPlacement: "bottom",
            Progress: new DisplayProgressDto(42, "42%", DateTimeOffset.Parse("2026-04-24T12:00:00Z"), action),
            Actions: [action],
            Flags: new DisplayCardFlagsDto(true, false, true, false, true),
            SortTimestamp: DateTimeOffset.Parse("2026-04-24T12:00:00Z"));
        var dto = new DisplayPageDto("home", "Home", null, null, [new DisplayShelfDto("continue", "Continue", null, [card], "/display/continue")], [card]);

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<DisplayPageDto>(json, JsonOptions);

        Assert.NotNull(roundTrip);
        Assert.Equal(dto.Key, roundTrip.Key);
        Assert.Equal(card.WorkId, roundTrip.Catalog[0].WorkId);
        Assert.Equal(card.Actions[0].Type, roundTrip.Catalog[0].Actions[0].Type);
        Assert.Contains("\"workId\":\"11111111-1111-1111-1111-111111111111\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void PagingDto_RoundTripsRepresentativeShape()
    {
        var dto = new PagedResponse<string>(["one", "two"], Offset: 10, Limit: 2, HasMore: true, TotalCount: 42, NextCursor: "12");

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<PagedResponse<string>>(json, JsonOptions);

        Assert.NotNull(roundTrip);
        Assert.Equal(dto.Items, roundTrip.Items);
        Assert.Equal(dto.NextCursor, roundTrip.NextCursor);
        Assert.Contains("\"has_more\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"total_count\":42", json, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackDto_RoundTripsRepresentativeShape()
    {
        var dto = new PlaybackManifestDto
        {
            AssetId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Client = "web",
            MediaType = "Movies",
            SourceExtension = ".mkv",
            RecommendedDelivery = PlaybackDeliveryModes.Hls,
            HlsUrl = "/playback/hls/manifest.m3u8",
            DirectPlaySupported = false,
            AudioTracks =
            [
                new PlaybackTrackDto
                {
                    Index = 1,
                    Language = "eng",
                    Codec = "aac",
                    IsDefault = true,
                    Channels = 2,
                },
            ],
            Segments =
            [
                new PlaybackSegmentDto
                {
                    Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    AssetId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    Kind = "intro",
                    StartSeconds = 12.5,
                    EndSeconds = 58.0,
                    Confidence = 0.91,
                    Source = "plugin",
                    IsSkippable = true,
                },
            ],
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<PlaybackManifestDto>(json, JsonOptions);

        Assert.NotNull(roundTrip);
        Assert.Equal(dto.AssetId, roundTrip.AssetId);
        Assert.Equal(dto.RecommendedDelivery, roundTrip.RecommendedDelivery);
        Assert.Equal(dto.AudioTracks[0].Language, roundTrip.AudioTracks[0].Language);
        Assert.Equal(dto.Segments[0].Kind, roundTrip.Segments[0].Kind);
        Assert.Contains("\"assetId\":\"44444444-4444-4444-4444-444444444444\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsDto_RoundTripsRepresentativeShape()
    {
        var dto = new PipelineConfiguration
        {
            Pipelines =
            {
                ["Books"] = new MediaTypePipeline
                {
                    Strategy = ProviderStrategy.Waterfall,
                    Providers =
                    [
                        new PipelineProviderEntry
                        {
                            Rank = 1,
                            Name = "openlibrary",
                        },
                    ],
                    FieldPriorities =
                    {
                        ["title"] = ["openlibrary", "embedded"],
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<PipelineConfiguration>(json, JsonOptions);

        Assert.NotNull(roundTrip);
        Assert.True(roundTrip.Pipelines.ContainsKey("Books"));
        Assert.Equal(dto.Pipelines["Books"].Strategy, roundTrip.Pipelines["Books"].Strategy);
        Assert.Equal("openlibrary", roundTrip.Pipelines["Books"].Providers[0].Name);
        Assert.Contains("\"field_priorities\":", json, StringComparison.Ordinal);
    }
}
