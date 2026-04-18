using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Discovery;

namespace MediaEngine.Web.Tests;

public sealed class DiscoveryComposerServiceTests
{
    [Fact]
    public void ComposeListen_CreatesDistinctMusicAndAudiobookShelves()
    {
        var service = new DiscoveryComposerService(null!);

        var works = new[]
        {
            CreateWork(
                id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                mediaType: "Music",
                title: "The Record",
                creator: "Boygenius",
                year: "2023"),
            CreateWork(
                id: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                mediaType: "Audiobooks",
                title: "Project Hail Mary",
                creator: "Andy Weir",
                year: "2021")
        };

        var page = service.ComposeListen(works, [], []);

        Assert.Contains(page.Shelves, shelf => shelf.Title == "New music in your library");
        Assert.Contains(page.Shelves, shelf => shelf.Title == "Audiobooks on deck");
    }

    [Fact]
    public void ComposeHome_UsesSafeSeparatorsInHeroAndCollectionDescriptions()
    {
        var service = new DiscoveryComposerService(null!);

        var groups = new[]
        {
            new ContentGroupViewModel
            {
                CollectionId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                DisplayName = "The Expanse",
                PrimaryMediaType = "TV",
                WorkCount = 10,
                SeasonCount = 2,
                CreatedAt = DateTimeOffset.UtcNow,
            }
        };

        var page = service.ComposeHome([], [], groups);

        Assert.Equal("2 seasons / 10 items", page.Hero?.Description);
        Assert.DoesNotContain("Â", page.Hero?.Description);
    }

    [Fact]
    public void ComposeHome_UsesCollectionPreviewImagesWhenGroupArtIsMissing()
    {
        var service = new DiscoveryComposerService(null!);
        var collectionId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var groups = new[]
        {
            new ContentGroupViewModel
            {
                CollectionId = collectionId,
                DisplayName = "Sci-Fi Shelf",
                PrimaryMediaType = "Books",
                WorkCount = 4,
                CreatedAt = DateTimeOffset.UtcNow,
            }
        };

        var previewImages = new Dictionary<Guid, IReadOnlyList<string>>
        {
            [collectionId] = ["/art/one.jpg", "/art/two.jpg", "/art/three.jpg"]
        };

        var page = service.ComposeHome([], [], groups, previewImages);
        var collectionShelf = Assert.Single(page.Shelves, shelf => shelf.Title == "Collections built from your library");
        var collectionCard = Assert.Single(collectionShelf.Items);
        var hub = Assert.Single(page.Hubs);

        Assert.Equal(3, collectionCard.PreviewImages.Count);
        Assert.Equal("/art/one.jpg", collectionCard.PreviewImages[0]);
        Assert.Equal(3, hub.PreviewImages.Count);
        Assert.Equal("/art/two.jpg", hub.PreviewImages[1]);
    }

    private static WorkViewModel CreateWork(
        Guid id,
        string mediaType,
        string title,
        string creator,
        string year) =>
        new()
        {
            Id = id,
            MediaType = mediaType,
            CanonicalValues =
            [
                CreateCanonical("title", title),
                CreateCanonical("author", creator),
                CreateCanonical("year", year),
                CreateCanonical("release_year", year),
            ]
        };

    private static CanonicalValueViewModel CreateCanonical(string key, string value) =>
        new()
        {
            Key = key,
            Value = value,
            LastScoredAt = DateTimeOffset.UtcNow,
        };
}
