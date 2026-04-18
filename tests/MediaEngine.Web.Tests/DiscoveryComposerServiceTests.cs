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
        var tvGroupId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var movieGroupId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var bookGroupId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var comicGroupId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var audiobookGroupId = Guid.Parse("88888888-8888-8888-8888-888888888888");

        var groups = new[]
        {
            new ContentGroupViewModel
            {
                CollectionId = tvGroupId,
                DisplayName = "The Expanse",
                PrimaryMediaType = "TV",
                WorkCount = 10,
                CoverUrl = "/art/tv-cover.jpg",
                SeasonCount = 2,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new ContentGroupViewModel
            {
                CollectionId = movieGroupId,
                DisplayName = "Mission: Impossible",
                PrimaryMediaType = "Movies",
                WorkCount = 7,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new ContentGroupViewModel
            {
                CollectionId = bookGroupId,
                DisplayName = "The Stormlight Archive",
                PrimaryMediaType = "Books",
                WorkCount = 4,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new ContentGroupViewModel
            {
                CollectionId = comicGroupId,
                DisplayName = "Saga",
                PrimaryMediaType = "Comics",
                WorkCount = 3,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new ContentGroupViewModel
            {
                CollectionId = audiobookGroupId,
                DisplayName = "Murderbot Diaries",
                PrimaryMediaType = "Audiobooks",
                WorkCount = 6,
                CreatedAt = DateTimeOffset.UtcNow,
            },
        };

        var albumGroups = new[]
        {
            new ContentGroupViewModel
            {
                CollectionId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
                DisplayName = "The Record",
                PrimaryMediaType = "Music",
                WorkCount = 12,
                CoverUrl = "/art/album.jpg",
                Creator = "boygenius",
                Year = "2023",
                CreatedAt = DateTimeOffset.UtcNow,
            }
        };

        var artistGroups = new[]
        {
            new ContentGroupViewModel
            {
                CollectionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                DisplayName = "boygenius",
                PrimaryMediaType = "Music",
                WorkCount = 18,
                ArtistPhotoUrl = "/art/artist.jpg",
                Creator = "boygenius",
                CreatedAt = DateTimeOffset.UtcNow,
            }
        };

        var previewImages = new Dictionary<Guid, IReadOnlyList<string>>
        {
            [movieGroupId] = ["/art/one.jpg", "/art/two.jpg", "/art/three.jpg"]
        };

        var page = service.ComposeHome([], [], groups, previewImages, albumGroups, artistGroups);

        Assert.Empty(page.Hubs);
        Assert.Contains(page.Shelves, shelf => shelf.Title == "TV Series");
        Assert.Contains(page.Shelves, shelf => shelf.Title == "Movie Series");
        Assert.Contains(page.Shelves, shelf => shelf.Title == "Book Series");
        Assert.Contains(page.Shelves, shelf => shelf.Title == "Comic Series");
        Assert.Contains(page.Shelves, shelf => shelf.Title == "Albums");
        Assert.Contains(page.Shelves, shelf => shelf.Title == "Artists");
        Assert.Contains(page.Shelves, shelf => shelf.Title == "Audiobook Series");
        Assert.DoesNotContain(page.Shelves, shelf => shelf.Title == "Collections built from your library");

        var tvCard = Assert.Single(page.Shelves.Single(shelf => shelf.Title == "TV Series").Items);
        Assert.Equal(DiscoveryCardPresentation.TvSeries, tvCard.Presentation);
        Assert.Equal("/art/tv-cover.jpg", tvCard.CoverUrl);

        var movieCard = Assert.Single(page.Shelves.Single(shelf => shelf.Title == "Movie Series").Items);
        Assert.Equal(DiscoveryCardPresentation.MovieSeries, movieCard.Presentation);
        Assert.Equal(3, movieCard.PreviewImages.Count);
        Assert.Equal("/art/one.jpg", movieCard.PreviewImages[0]);

        var albumCard = Assert.Single(page.Shelves.Single(shelf => shelf.Title == "Albums").Items);
        Assert.Equal(DiscoveryCardPresentation.Album, albumCard.Presentation);
        Assert.Contains("/listen/music?", albumCard.NavigationUrl, StringComparison.Ordinal);
        Assert.Contains("groupField=album", albumCard.NavigationUrl, StringComparison.Ordinal);

        var artistCard = Assert.Single(page.Shelves.Single(shelf => shelf.Title == "Artists").Items);
        Assert.Equal(DiscoveryCardPresentation.Artist, artistCard.Presentation);
        Assert.Equal("/art/artist.jpg", artistCard.CoverUrl);
        Assert.Contains("groupField=artist", artistCard.NavigationUrl, StringComparison.Ordinal);
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
