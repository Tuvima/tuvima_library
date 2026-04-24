using MediaEngine.Domain;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using MediaEngine.Web.Services.Navigation;
using System.Security.Cryptography;
using System.Text;

namespace MediaEngine.Web.Services.Discovery;

public sealed class DiscoveryComposerService
{
    private readonly IEngineApiClient _api;

    public DiscoveryComposerService(IEngineApiClient api)
    {
        _api = api;
    }

    public async Task<DiscoveryPageViewModel> BuildHomeAsync(CancellationToken ct = default)
    {
        var worksTask = _api.GetLibraryWorksAsync(ct);
        var journeyTask = _api.GetJourneyAsync(limit: 18, ct: ct);
        var groupsTask = _api.GetContentGroupsAsync(ct);
        var musicAlbumGroupsTask = _api.GetSystemViewGroupsAsync(mediaType: "Music", groupField: "album", ct: ct);
        var musicArtistGroupsTask = _api.GetSystemViewGroupsAsync(mediaType: "Music", groupField: "artist", ct: ct);

        await Task.WhenAll(worksTask, journeyTask, groupsTask, musicAlbumGroupsTask, musicArtistGroupsTask);

        var works = await worksTask;
        var journey = await journeyTask;
        var groups = await groupsTask;
        var musicAlbumGroups = await musicAlbumGroupsTask;
        var musicArtistGroups = await musicArtistGroupsTask;
        var tasteProfile = await LoadActiveTasteProfileAsync(ct);
        var previewImages = await LoadCollectionPreviewImagesAsync(
            groups
                .OrderByDescending(group => group.WorkCount)
                .ThenByDescending(group => group.CreatedAt)
                .Where(group => string.IsNullOrWhiteSpace(group.CoverUrl) && string.IsNullOrWhiteSpace(group.ArtistPhotoUrl))
                .Take(18),
            ct);

        return ComposeHome(works, journey, groups, previewImages, musicAlbumGroups, musicArtistGroups, tasteProfile);
    }

    public async Task<DiscoveryPageViewModel> BuildReadAsync(CancellationToken ct = default)
    {
        var worksTask = _api.GetLibraryWorksAsync(ct);
        var journeyTask = _api.GetJourneyAsync(limit: 18, ct: ct);
        var groupsTask = _api.GetContentGroupsAsync(ct);

        await Task.WhenAll(worksTask, journeyTask, groupsTask);

        var works = await worksTask;
        var journey = await journeyTask;
        var groups = await groupsTask;
        var previewImages = await LoadCollectionPreviewImagesAsync(
            groups
                .Where(group => IsReadKind(NormalizeDisplayKind(group.PrimaryMediaType)))
                .OrderByDescending(group => group.WorkCount)
                .ThenByDescending(group => group.CreatedAt)
                .Where(group => string.IsNullOrWhiteSpace(group.CoverUrl) && string.IsNullOrWhiteSpace(group.ArtistPhotoUrl))
                .Take(12),
            ct);

        return ComposeRead(works, journey, groups, previewImages);
    }

    public async Task<DiscoveryPageViewModel> BuildWatchAsync(CancellationToken ct = default)
    {
        var worksTask = _api.GetLibraryWorksAsync(ct);
        var journeyTask = _api.GetJourneyAsync(limit: 18, ct: ct);
        var groupsTask = _api.GetContentGroupsAsync(ct);

        await Task.WhenAll(worksTask, journeyTask, groupsTask);

        var works = await worksTask;
        var journey = await journeyTask;
        var groups = await groupsTask;
        var previewImages = await LoadCollectionPreviewImagesAsync(
            groups
                .Where(group => IsWatchKind(NormalizeDisplayKind(group.PrimaryMediaType)))
                .OrderByDescending(group => group.WorkCount)
                .ThenByDescending(group => group.CreatedAt)
                .Where(group => string.IsNullOrWhiteSpace(group.CoverUrl) && string.IsNullOrWhiteSpace(group.ArtistPhotoUrl))
                .Take(12),
            ct);

        return ComposeWatch(works, journey, groups, previewImages);
    }

    public async Task<DiscoveryPageViewModel> BuildListenAsync(CancellationToken ct = default)
    {
        var worksTask = _api.GetLibraryWorksAsync(ct);
        var journeyTask = _api.GetJourneyAsync(limit: 18, ct: ct);
        var groupsTask = _api.GetContentGroupsAsync(ct);

        await Task.WhenAll(worksTask, journeyTask, groupsTask);

        var works = await worksTask;
        var journey = await journeyTask;
        var groups = await groupsTask;
        var previewImages = await LoadCollectionPreviewImagesAsync(
            groups
                .Where(group => IsListenKind(NormalizeDisplayKind(group.PrimaryMediaType)))
                .OrderByDescending(group => group.WorkCount)
                .ThenByDescending(group => group.CreatedAt)
                .Where(group => string.IsNullOrWhiteSpace(group.CoverUrl) && string.IsNullOrWhiteSpace(group.ArtistPhotoUrl))
                .Take(12),
            ct);

        return ComposeListen(works, journey, groups, previewImages);
    }

    public DiscoveryPageViewModel ComposeHome(
        IReadOnlyList<WorkViewModel> works,
        IReadOnlyList<JourneyItemViewModel> journey,
        IReadOnlyList<ContentGroupViewModel> groups,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? groupPreviewImages = null,
        IReadOnlyList<ContentGroupViewModel>? musicAlbumGroups = null,
        IReadOnlyList<ContentGroupViewModel>? musicArtistGroups = null,
        TasteProfile? tasteProfile = null)
    {
        var orderedWorks = works.OrderByDescending(GetHomeSortTimestamp).ThenByDescending(work => ParseYear(work.Year)).ToList();
        var progressLookup = BuildProgressLookup(journey);
        var workLookup = works.ToDictionary(work => work.Id);
        var catalog = orderedWorks
            .Where(work => !SuppressIndividualOnHome(work))
            .Select(work => ToWorkCard(work, progressLookup.GetValueOrDefault(work.Id)))
            .ToList();
        var orderedMusicAlbumGroups = (musicAlbumGroups ?? [])
            .OrderByDescending(group => group.WorkCount)
            .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orderedMusicArtistGroups = (musicArtistGroups ?? [])
            .OrderByDescending(group => group.WorkCount)
            .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var groupLookup = groups
            .Where(group => group.CollectionId != Guid.Empty)
            .GroupBy(group => group.CollectionId)
            .ToDictionary(group => group.Key, group => group.First());

        var continueCards = RankHomeCards(
            BuildHomeContinueCards(journey, workLookup, groupLookup, groupPreviewImages),
            tasteProfile);
        var freshArrivalCards = RankHomeCards(
            BuildHomeFreshArrivalCards(orderedWorks, groupLookup, groupPreviewImages),
            tasteProfile);

        var shelves = new List<DiscoveryShelfViewModel>();
        shelves.AddRange(BuildHomeSurfaceShelves(continueCards, freshArrivalCards, tasteProfile));

        shelves.AddRange(BuildHomeCollectionShelves(groups, orderedMusicAlbumGroups, orderedMusicArtistGroups, groupPreviewImages));

        foreach (var shelf in BuildAffinityShelves(
                     catalog.Where(card => IsReadKind(card.MediaKind)).ToList(),
                     catalog.Where(card => IsWatchKind(card.MediaKind)).ToList(),
                     catalog.Where(card => IsListenKind(card.MediaKind)).ToList(),
                     journey))
        {
            shelves.Add(shelf);
        }

        return new DiscoveryPageViewModel
        {
            Key = "home",
            AccentColor = "#1CE783",
            Hero = BuildHomeHero(continueCards, freshArrivalCards, groups, orderedMusicAlbumGroups, groupPreviewImages, "#1CE783", tasteProfile),
            Hubs = [],
            Shelves = shelves,
            Catalog = catalog,
            EmptyTitle = "Your home screen is waiting for its first story",
            EmptySubtitle = "Once media lands in the library, home becomes the personalized view across everything you own.",
        };
    }

    public DiscoveryPageViewModel ComposeRead(
        IReadOnlyList<WorkViewModel> works,
        IReadOnlyList<JourneyItemViewModel> journey,
        IReadOnlyList<ContentGroupViewModel> groups,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? groupPreviewImages = null)
    {
        var readWorks = works
            .Where(work => IsReadBucket(GetBucket(work.MediaType)))
            .OrderByDescending(GetSortTimestamp)
            .ThenByDescending(work => ParseYear(work.Year))
            .ToList();

        var readJourney = journey
            .Where(item => IsReadBucket(GetBucket(item.MediaType)))
            .OrderByDescending(item => item.LastAccessed)
            .ToList();

        var readGroups = groups
            .Where(group => IsReadKind(NormalizeDisplayKind(group.PrimaryMediaType)))
            .Where(ShouldDisplayCollectionGroup)
            .OrderByDescending(group => group.WorkCount)
            .ThenByDescending(group => group.CreatedAt)
            .ToList();

        var progressLookup = BuildProgressLookup(readJourney);
        var catalog = readWorks.Select(work => ToWorkCard(work, progressLookup.GetValueOrDefault(work.Id))).ToList();

        var authorShelves = readWorks
            .Where(work => !string.IsNullOrWhiteSpace(work.Author))
            .GroupBy(work => work.Author!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= 2)
            .OrderByDescending(group => group.Count())
            .Take(3)
            .Select(group => new DiscoveryShelfViewModel
            {
                Title = group.Key,
                Subtitle = $"{group.Count()} titles",
                Items = TakeShelfItems(
                    group
                        .OrderByDescending(GetSortTimestamp)
                        .Select(work => ToWorkCard(work, progressLookup.GetValueOrDefault(work.Id))),
                    10),
            })
            .ToList();

        var shelves = new List<DiscoveryShelfViewModel>();

        if (readJourney.Count > 0)
        {
            shelves.Add(new DiscoveryShelfViewModel
            {
                Title = "Continue reading",
                Subtitle = "Books and comics already in motion",
                Items = TakeShelfItems(readJourney.Select(item => ToJourneyCard(item)), 10),
            });
        }

        if (readGroups.Count > 0)
        {
            shelves.Add(new DiscoveryShelfViewModel
            {
                Title = "Collections to explore",
                Subtitle = "Series and grouped reading pulled from your library",
                Items = TakeShelfItems(
                    readGroups.Select(group => ToCollectionCard(
                        group,
                        DiscoveryCardShape.Portrait,
                        GetPreviewImages(groupPreviewImages, group.CollectionId))),
                    10),
                SeeAllRoute = "/collections",
            });
        }

        if (catalog.Count > 0)
        {
            shelves.Add(new DiscoveryShelfViewModel
            {
                Title = "Recently added to read",
                Subtitle = "Fresh pages ready to pick up",
                Items = TakeShelfItems(catalog, 12),
            });
        }

        shelves.AddRange(authorShelves);

        return new DiscoveryPageViewModel
        {
            Key = "read",
            AccentColor = "#5DCAA5",
            Hero = BuildHero(
                journey: readJourney,
                works: readWorks,
                groups: readGroups,
                groupPreviewImages: groupPreviewImages,
                accentColor: "#5DCAA5",
                journeyEyebrow: "Continue reading",
                workEyebrow: "New on your shelf",
                groupEyebrow: "Series to explore"),
            Hubs = readGroups.Take(8).Select(group => ToHub(group, GetPreviewImages(groupPreviewImages, group.CollectionId))).ToList(),
            Shelves = shelves,
            Catalog = catalog,
            EmptyTitle = "Nothing to read yet",
            EmptySubtitle = "Books and comics appear here with searchable discovery once they are in the library.",
        };
    }

    public DiscoveryPageViewModel ComposeWatch(
        IReadOnlyList<WorkViewModel> works,
        IReadOnlyList<JourneyItemViewModel> journey,
        IReadOnlyList<ContentGroupViewModel> groups,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? groupPreviewImages = null)
    {
        var watchWorks = works
            .Where(work => IsWatchBucket(GetBucket(work.MediaType)))
            .OrderByDescending(GetSortTimestamp)
            .ThenByDescending(work => ParseYear(work.Year))
            .ToList();

        var watchJourney = journey
            .Where(item => IsWatchBucket(GetBucket(item.MediaType)))
            .OrderByDescending(item => item.LastAccessed)
            .ToList();

        var watchGroups = groups
            .Where(group => IsWatchKind(NormalizeDisplayKind(group.PrimaryMediaType)))
            .Where(ShouldDisplayCollectionGroup)
            .OrderByDescending(group => group.WorkCount)
            .ThenByDescending(group => group.CreatedAt)
            .ToList();

        var progressLookup = BuildProgressLookup(watchJourney);
        var catalog = watchWorks.Select(work => ToWorkCard(work, progressLookup.GetValueOrDefault(work.Id))).ToList();

        var shelves = new List<DiscoveryShelfViewModel>();

        if (watchGroups.Count > 0)
        {
            shelves.Add(new DiscoveryShelfViewModel
            {
                Title = "Collections to watch",
                Subtitle = "Shows, series, and grouped franchises from your library",
                Items = TakeShelfItems(
                    watchGroups.Select(group => ToCollectionCard(
                        group,
                        DiscoveryCardShape.Landscape,
                        GetPreviewImages(groupPreviewImages, group.CollectionId))),
                    10),
                SeeAllRoute = "/collections",
            });
        }

        var movieCards = TakeShelfItems(
            catalog.Where(card => string.Equals(card.MediaKind, "Movie", StringComparison.OrdinalIgnoreCase)),
            12);
        if (movieCards.Count > 0)
        {
            shelves.Add(new DiscoveryShelfViewModel
            {
                Title = "Movies in your library",
                Subtitle = "Library discovery in a Plex-style grid and row flow",
                Items = movieCards,
            });
        }

        var tvCards = TakeShelfItems(
            catalog.Where(card => string.Equals(card.MediaKind, "TV", StringComparison.OrdinalIgnoreCase)),
            12);
        if (tvCards.Count > 0)
        {
            shelves.Add(new DiscoveryShelfViewModel
            {
                Title = "TV in your library",
                Subtitle = "Jump back into shows and seasons quickly",
                Items = tvCards,
            });
        }

        var genreShelves = watchWorks
            .Where(work => work.Genres.Count > 0)
            .SelectMany(work => work.Genres.Select(genre => (Genre: genre, Work: work)))
            .GroupBy(item => item.Genre, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= 3)
            .OrderByDescending(group => group.Count())
            .Take(3)
            .Select(group => new DiscoveryShelfViewModel
            {
                Title = group.Key,
                Subtitle = $"{group.Count()} titles",
                Items = TakeShelfItems(
                    group.Select(item => ToWorkCard(item.Work, progressLookup.GetValueOrDefault(item.Work.Id))),
                    10),
            });

        shelves.AddRange(genreShelves);

        return new DiscoveryPageViewModel
        {
            Key = "watch",
            AccentColor = "#60A5FA",
            Hero = BuildHero(
                journey: watchJourney,
                works: watchWorks,
                groups: watchGroups,
                groupPreviewImages: groupPreviewImages,
                accentColor: "#60A5FA",
                journeyEyebrow: "Continue watching",
                workEyebrow: "Featured from your library",
                groupEyebrow: "Featured collection"),
            Hubs = watchGroups.Take(8).Select(group => ToHub(group, GetPreviewImages(groupPreviewImages, group.CollectionId))).ToList(),
            Shelves = shelves,
            Catalog = catalog,
            EmptyTitle = "Nothing to watch yet",
            EmptySubtitle = "Movies and TV become searchable and filterable here once they are imported.",
        };
    }

    public DiscoveryPageViewModel ComposeListen(
        IReadOnlyList<WorkViewModel> works,
        IReadOnlyList<JourneyItemViewModel> journey,
        IReadOnlyList<ContentGroupViewModel> groups,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? groupPreviewImages = null)
    {
        var listenWorks = works
            .Where(work => IsListenBucket(GetBucket(work.MediaType)))
            .OrderByDescending(GetSortTimestamp)
            .ThenByDescending(work => ParseYear(work.Year))
            .ToList();

        var listenJourney = journey
            .Where(item => IsListenBucket(GetBucket(item.MediaType)))
            .OrderByDescending(item => item.LastAccessed)
            .ToList();

        var listenGroups = groups
            .Where(group => IsListenKind(NormalizeDisplayKind(group.PrimaryMediaType)))
            .Where(ShouldDisplayCollectionGroup)
            .OrderByDescending(group => group.WorkCount)
            .ThenByDescending(group => group.CreatedAt)
            .ToList();

        var progressLookup = BuildProgressLookup(listenJourney);
        var catalog = listenWorks.Select(work => ToWorkCard(work, progressLookup.GetValueOrDefault(work.Id))).ToList();

        var shelves = new List<DiscoveryShelfViewModel>();

        if (listenJourney.Count > 0)
        {
            shelves.Add(new DiscoveryShelfViewModel
            {
                Title = "Continue listening",
                Subtitle = "Resume music and audiobooks already in progress",
                Items = TakeShelfItems(listenJourney.Select(item => ToJourneyCard(item)), 10),
            });
        }

        if (listenGroups.Count > 0)
        {
            shelves.Add(new DiscoveryShelfViewModel
            {
                Title = "Collections and mixes",
                Subtitle = "Dynamic albums, artist groupings, and audiobook series",
                Items = TakeShelfItems(
                    listenGroups.Select(group => ToCollectionCard(
                        group,
                        OverrideShapeForGroup(group, "listen"),
                        GetPreviewImages(groupPreviewImages, group.CollectionId))),
                    10),
                SeeAllRoute = "/collections",
            });
        }

        var musicCards = TakeShelfItems(
            catalog.Where(card => string.Equals(card.MediaKind, "Music", StringComparison.OrdinalIgnoreCase)),
            12);
        if (musicCards.Count > 0)
        {
            shelves.Add(new DiscoveryShelfViewModel
            {
                Title = "New music in your library",
                Subtitle = "Album art first, like a streaming browse surface",
                Items = musicCards,
            });
        }

        var audiobookCards = TakeShelfItems(
            catalog.Where(card => string.Equals(card.MediaKind, "Audiobook", StringComparison.OrdinalIgnoreCase)),
            12);
        if (audiobookCards.Count > 0)
        {
            shelves.Add(new DiscoveryShelfViewModel
            {
                Title = "Audiobooks on deck",
                Subtitle = "A separate browse mode for spoken-word titles",
                Items = audiobookCards,
            });
        }

        return new DiscoveryPageViewModel
        {
            Key = "listen",
            AccentColor = "#1ED760",
            Hero = BuildHero(
                journey: listenJourney,
                works: listenWorks,
                groups: listenGroups,
                groupPreviewImages: groupPreviewImages,
                accentColor: "#1ED760",
                journeyEyebrow: "Continue listening",
                workEyebrow: "Featured from your library",
                groupEyebrow: "Featured collection"),
            Hubs = listenGroups.Take(8).Select(group => ToHub(group, GetPreviewImages(groupPreviewImages, group.CollectionId))).ToList(),
            Shelves = shelves,
            Catalog = catalog,
            EmptyTitle = "Nothing to listen to yet",
            EmptySubtitle = "Music and audiobooks show up here with dedicated discovery views once the library has them.",
        };
    }

    public DiscoveryPageViewModel ComposeMusicHome(
        IReadOnlyList<WorkViewModel> musicWorks,
        IReadOnlyList<JourneyItemViewModel> musicJourney,
        IReadOnlyList<ContentGroupViewModel> albumGroups,
        IReadOnlyList<ContentGroupViewModel> artistGroups,
        IReadOnlyCollection<Guid>? favoriteWorkIds = null)
    {
        var orderedWorks = musicWorks
            .OrderByDescending(GetSortTimestamp)
            .ThenByDescending(work => ParseYear(work.Year))
            .ToList();

        var orderedJourney = musicJourney
            .Where(item => GetBucket(item.MediaType) == DiscoveryBucket.Music)
            .OrderByDescending(item => item.LastAccessed)
            .ToList();

        var orderedAlbums = albumGroups
            .OrderByDescending(group => group.CreatedAt)
            .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var orderedArtists = artistGroups
            .OrderByDescending(group => group.WorkCount)
            .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var progressLookup = BuildProgressLookup(orderedJourney);
        var favoriteSet = favoriteWorkIds?.ToHashSet() ?? [];
        var shelves = new List<DiscoveryShelfViewModel>();

        AddHomeCollectionShelf(
            shelves,
            title: "Recently Played",
            subtitle: "Pick up albums and tracks from your latest listening sessions",
            seeAllRoute: ListenNavigation.SongsRoute,
            items: orderedJourney.Select(item => ToJourneyCard(item)));

        AddHomeCollectionShelf(
            shelves,
            title: "Favorite Songs",
            subtitle: "Tracks you marked as favorites",
            seeAllRoute: "/listen/music/playlists/system/favorite-songs",
            items: orderedWorks
                .Where(work => favoriteSet.Contains(work.Id))
                .Select(work => ToWorkCard(work, progressLookup.GetValueOrDefault(work.Id))));

        AddHomeCollectionShelf(
            shelves,
            title: "Recently Added",
            subtitle: "Fresh arrivals from your music library",
            seeAllRoute: "/listen/music/playlists/system/recently-added",
            items: orderedWorks.Select(work => ToWorkCard(work, progressLookup.GetValueOrDefault(work.Id))));

        AddHomeCollectionShelf(
            shelves,
            title: "Albums",
            subtitle: "Album-first browsing with cover art at the center",
            seeAllRoute: ListenNavigation.AlbumsRoute,
            items: orderedAlbums.Select(CreateAlbumGroupCard));

        AddHomeCollectionShelf(
            shelves,
            title: "Artists",
            subtitle: "Artist-led listening built from your library",
            seeAllRoute: ListenNavigation.ArtistsRoute,
            items: orderedArtists.Select(group => ToSystemViewCard(
                group,
                groupType: "artist",
                groupField: "artist",
                routeBase: "/listen",
                routeTab: "music",
                presentation: DiscoveryCardPresentation.Artist,
                shape: DiscoveryCardShape.Square)));

        return new DiscoveryPageViewModel
        {
            Key = "listen-music",
            AccentColor = "#1ED760",
            Hero = BuildHero(
                journey: orderedJourney,
                works: orderedWorks,
                groups: orderedAlbums,
                groupPreviewImages: null,
                accentColor: "#1ED760",
                journeyEyebrow: "Recently played",
                workEyebrow: "New music in your library",
                groupEyebrow: "Featured album"),
            Shelves = shelves,
            Catalog = orderedWorks.Select(work => ToWorkCard(work, progressLookup.GetValueOrDefault(work.Id))).ToList(),
            EmptyTitle = "No music is ready yet",
            EmptySubtitle = "Music will appear here once the library has tracks and albums to browse.",
        };
    }

    private static IReadOnlyList<DiscoveryShelfViewModel> BuildAffinityShelves(
        IReadOnlyList<DiscoveryCardViewModel> readItems,
        IReadOnlyList<DiscoveryCardViewModel> watchItems,
        IReadOnlyList<DiscoveryCardViewModel> listenItems,
        IReadOnlyList<JourneyItemViewModel> journey)
    {
        var shelves = new List<(int Score, DiscoveryShelfViewModel Shelf)>();

        if (readItems.Count > 0)
        {
            var shelfItems = TakeShelfItems(readItems, 12);
            shelves.Add((GetAffinityScore(journey, IsReadBucket), new DiscoveryShelfViewModel
            {
                Title = "Read next",
                Subtitle = "Books and comics ordered by your recent library behavior",
                Items = shelfItems,
                SeeAllRoute = "/read",
            }));
        }

        if (watchItems.Count > 0)
        {
            var shelfItems = TakeShelfItems(watchItems, 12);
            shelves.Add((GetAffinityScore(journey, IsWatchBucket), new DiscoveryShelfViewModel
            {
                Title = "Watch next",
                Subtitle = "Movies and shows ready to jump back into",
                Items = shelfItems,
                SeeAllRoute = "/watch",
            }));
        }

        if (listenItems.Count > 0)
        {
            var shelfItems = TakeShelfItems(listenItems, 12);
            shelves.Add((GetAffinityScore(journey, IsListenBucket), new DiscoveryShelfViewModel
            {
                Title = "Listen next",
                Subtitle = "Music and audiobooks surfaced from your recent habits",
                Items = shelfItems,
                SeeAllRoute = "/listen",
            }));
        }

        return shelves
            .OrderByDescending(entry => entry.Score)
            .Select(entry => entry.Shelf)
            .ToList();
    }

    private static DiscoveryHeroViewModel? BuildHomeHero(
        IReadOnlyList<DiscoveryCardViewModel> continueCards,
        IReadOnlyList<DiscoveryCardViewModel> freshArrivalCards,
        IReadOnlyList<ContentGroupViewModel> contentGroups,
        IReadOnlyList<ContentGroupViewModel> musicAlbumGroups,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? groupPreviewImages,
        string accentColor,
        TasteProfile? tasteProfile)
    {
        var continueHero = continueCards.FirstOrDefault();
        if (continueHero is not null)
            return ToHeroFromCard(continueHero, "Continue with your library", accentColor);

        var freshHero = freshArrivalCards.FirstOrDefault();
        if (freshHero is not null)
            return ToHeroFromCard(freshHero, "Fresh in your library", accentColor);

        var featuredGroup = contentGroups
            .Where(ShouldDisplayCollectionGroup)
            .Concat(musicAlbumGroups)
            .OrderByDescending(group => GroupTasteScore(group, tasteProfile))
            .ThenByDescending(group => group.WorkCount)
            .ThenByDescending(group => group.CreatedAt)
            .FirstOrDefault();
        if (featuredGroup is null)
            return null;

        var featuredCard = GetBucket(featuredGroup.PrimaryMediaType) switch
        {
            DiscoveryBucket.Tv => CreateTvCollectionCard(featuredGroup, groupPreviewImages),
            DiscoveryBucket.Music => CreateAlbumGroupCard(featuredGroup),
            _ => CreateCollectionShelfCard(featuredGroup, OverrideShapeForGroup(featuredGroup, "home"), DiscoveryCardPresentation.Default, groupPreviewImages),
        };

        return ToHeroFromCard(featuredCard, "Featured collection", accentColor);
    }

    private static IReadOnlyList<DiscoveryShelfViewModel> BuildHomeSurfaceShelves(
        IReadOnlyList<DiscoveryCardViewModel> continueCards,
        IReadOnlyList<DiscoveryCardViewModel> freshArrivalCards,
        TasteProfile? tasteProfile)
    {
        var bannerWatchContinue = TakeShelfItems(
            continueCards.Where(IsBannerWatchCard),
            12);
        var bannerWatchFresh = TakeShelfItems(
            freshArrivalCards.Where(IsBannerWatchCard),
            12);
        var watchPosters = TakeShelfItems(
            MergeHomeCards(
                continueCards.Where(IsPosterWatchCard),
                freshArrivalCards.Where(IsPosterWatchCard)),
            12);
        var readContinue = TakeShelfItems(
            continueCards.Where(card => card.MediaKind is "Book" or "Comic"),
            12);
        var readFresh = TakeShelfItems(
            freshArrivalCards.Where(card => card.MediaKind is "Book" or "Comic"),
            12);
        var audiobookContinue = TakeShelfItems(
            continueCards.Where(card => card.MediaKind == "Audiobook"),
            12);
        var audiobookFresh = TakeShelfItems(
            freshArrivalCards.Where(card => card.MediaKind == "Audiobook"),
            12);
        var musicContinue = TakeShelfItems(
            continueCards.Where(card => card.MediaKind == "Music"),
            12);
        var musicFresh = TakeShelfItems(
            freshArrivalCards.Where(card => card.MediaKind == "Music"),
            12);

        var groupedShelves = new List<(double Score, IReadOnlyList<DiscoveryShelfViewModel> Shelves)>
        {
            (
                ScoreHomeFamily("watch", bannerWatchContinue.Concat(bannerWatchFresh), tasteProfile) + 40,
                BuildShelfGroup(
                    CreateShelf("Continue Watching", BuildHomeSubtitle("Continue where you left off with banner-backed movies and shows", "watch", tasteProfile), bannerWatchContinue),
                    CreateShelf("Fresh to Watch", BuildHomeSubtitle("Recently added watch titles with banner art up front", "watch", tasteProfile), bannerWatchFresh))
            ),
            (
                ScoreHomeFamily("watch", watchPosters, tasteProfile) + 34,
                BuildShelfGroup(
                    CreateShelf("Watch Posters", BuildHomeSubtitle("Poster-led movies and shows without banner art", "watch", tasteProfile), watchPosters))
            ),
            (
                ScoreHomeFamily("read", readContinue.Concat(readFresh), tasteProfile) + 30,
                BuildShelfGroup(
                    CreateShelf("Continue Reading", BuildHomeSubtitle("Books and comics currently in motion", "read", tasteProfile), readContinue),
                    CreateShelf("Fresh Reads", BuildHomeSubtitle("Recently added books and comics ready to open", "read", tasteProfile), readFresh))
            ),
            (
                ScoreHomeFamily("audiobook", audiobookContinue.Concat(audiobookFresh), tasteProfile) + 24,
                BuildShelfGroup(
                    CreateShelf("Continue Audiobooks", BuildHomeSubtitle("Pick back up with audiobooks already underway", "audiobook", tasteProfile), audiobookContinue),
                    CreateShelf("Fresh Audiobooks", BuildHomeSubtitle("New audiobooks added to your library", "audiobook", tasteProfile), audiobookFresh))
            ),
            (
                ScoreHomeFamily("music", musicContinue.Concat(musicFresh), tasteProfile) + 20,
                BuildShelfGroup(
                    CreateShelf("Continue Listening", BuildHomeSubtitle("Album-first listening from your recent sessions", "music", tasteProfile), musicContinue),
                    CreateShelf("Fresh Music", BuildHomeSubtitle("New albums and tracks surfaced from your library", "music", tasteProfile), musicFresh))
            ),
        };

        return groupedShelves
            .Where(group => group.Shelves.Count > 0)
            .OrderByDescending(group => group.Score)
            .SelectMany(group => group.Shelves)
            .ToList();
    }

    private static IReadOnlyList<DiscoveryCardViewModel> BuildHomeContinueCards(
        IReadOnlyList<JourneyItemViewModel> journey,
        IReadOnlyDictionary<Guid, WorkViewModel> workLookup,
        IReadOnlyDictionary<Guid, ContentGroupViewModel> groupLookup,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? groupPreviewImages)
    {
        var cards = new List<DiscoveryCardViewModel>();

        cards.AddRange(
            journey
                .Where(item => !SuppressIndividualOnHome(item))
                .OrderByDescending(item => item.LastAccessed)
                .Select(item => ToHomeJourneyCard(
                    item,
                    workLookup.GetValueOrDefault(item.WorkId))));

        foreach (var groupedJourney in journey
                     .Where(SuppressIndividualOnHome)
                     .Where(item => item.CollectionId.HasValue)
                     .GroupBy(item => item.CollectionId!.Value)
                     .OrderByDescending(group => group.Max(item => item.LastAccessed)))
        {
            var latest = groupedJourney
                .OrderByDescending(item => item.LastAccessed)
                .First();

            cards.Add(GetBucket(latest.MediaType) switch
            {
                DiscoveryBucket.Tv => CreateTvContinueCard(
                    groupLookup.GetValueOrDefault(groupedJourney.Key),
                    groupedJourney.ToList(),
                    groupPreviewImages,
                    workLookup.GetValueOrDefault(latest.WorkId)),
                DiscoveryBucket.Music => CreateMusicContinueCard(
                    groupedJourney.ToList(),
                    workLookup.GetValueOrDefault(latest.WorkId)),
                _ => ToHomeJourneyCard(latest, workLookup.GetValueOrDefault(latest.WorkId)),
            });
        }

        return cards
            .OrderByDescending(card => card.SortTimestamp)
            .ToList();
    }

    private static IReadOnlyList<DiscoveryCardViewModel> BuildHomeFreshArrivalCards(
        IReadOnlyList<WorkViewModel> works,
        IReadOnlyDictionary<Guid, ContentGroupViewModel> groupLookup,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? groupPreviewImages)
    {
        var cards = new List<DiscoveryCardViewModel>();

        cards.AddRange(
            works
                .Where(work => !SuppressIndividualOnHome(work))
                .OrderByDescending(GetHomeSortTimestamp)
                .Select(ToHomeWorkCard));

        foreach (var groupedWorks in works
                     .Where(SuppressIndividualOnHome)
                     .Where(work => work.CollectionId.HasValue)
                     .GroupBy(work => work.CollectionId!.Value)
                     .OrderByDescending(group => group.Max(GetHomeSortTimestamp)))
        {
            var latest = groupedWorks
                .OrderByDescending(GetHomeSortTimestamp)
                .First();

            cards.Add(GetBucket(latest.MediaType) switch
            {
                DiscoveryBucket.Tv => CreateTvFreshArrivalCard(groupLookup.GetValueOrDefault(groupedWorks.Key), groupedWorks.ToList(), groupPreviewImages),
                DiscoveryBucket.Music => CreateMusicFreshArrivalCard(groupedWorks.ToList()),
                _ => ToHomeWorkCard(latest),
            });
        }

        return cards
            .OrderByDescending(card => card.SortTimestamp)
            .ToList();
    }

    private static DiscoveryCardViewModel CreateTvContinueCard(
        ContentGroupViewModel? group,
        IReadOnlyList<JourneyItemViewModel> items,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? groupPreviewImages,
        WorkViewModel? representativeWork)
    {
        var latest = items
            .OrderByDescending(item => item.LastAccessed)
            .First();
        var title = group?.DisplayName ?? latest.CollectionDisplayName ?? latest.Series ?? latest.Title;
        var detailsUrl = BuildJourneyDetailsUrl(latest);
        var primaryUrl = MediaNavigation.ForJourney(latest);
        var shape = HomeShapeForBucket(
            DiscoveryBucket.Tv,
            group?.BannerUrl ?? latest.BannerUrl,
            group?.BackgroundUrl ?? latest.BackgroundUrl,
            representativeWork?.CoverAspectClass,
            representativeWork?.SquareAspectClass,
            representativeWork?.BackgroundAspectClass,
            representativeWork?.BannerAspectClass);
        var surface = ResolveDiscoverySurface(
            DiscoveryBucket.Tv,
            shape,
            DiscoveryCardPresentation.TvSeries,
            group?.CoverUrl ?? latest.CoverUrl,
            representativeWork?.SquareUrl,
            group?.BackgroundUrl ?? latest.BackgroundUrl,
            group?.BannerUrl ?? latest.BannerUrl,
            coverAspectClass: representativeWork?.CoverAspectClass,
            squareAspectClass: representativeWork?.SquareAspectClass,
            backgroundAspectClass: representativeWork?.BackgroundAspectClass,
            bannerAspectClass: representativeWork?.BannerAspectClass);

        return new DiscoveryCardViewModel
        {
            Id = latest.CollectionId ?? latest.WorkId,
            CollectionId = latest.CollectionId,
            Title = title,
            Subtitle = group?.Network ?? group?.Creator ?? latest.Author,
            Description = TrimTo(group?.Description ?? latest.Description ?? group?.Tagline, 150),
            Tldr = TrimTo(representativeWork?.Tldr, 120),
            CoverUrl = group?.CoverUrl ?? latest.CoverUrl,
            BackgroundUrl = group?.BackgroundUrl ?? latest.BackgroundUrl,
            BannerUrl = group?.BannerUrl ?? latest.BannerUrl,
            HeroUrl = null,
            LogoUrl = group?.LogoUrl ?? latest.LogoUrl,
            PreviewImages = latest.CollectionId.HasValue ? GetPreviewImages(groupPreviewImages, latest.CollectionId.Value) : [],
            StatusText = BuildContinueStatus(latest, DiscoveryBucket.Tv),
            MetaText = JoinPartsSafe(group?.Year, group is not null ? CountLabel(group) : $"{items.Count} episodes"),
            ContextLines = BuildContextLines(group?.Network, group?.Creator, group?.Writer),
            VibeTags = BuildVibeTags(representativeWork),
            MediaKind = NormalizeDisplayKind(latest.MediaType),
            AccentColor = representativeWork?.ArtworkAccentHex ?? group?.MediaTypeColor ?? AccentForBucket(DiscoveryBucket.Tv),
            Shape = shape,
            Presentation = DiscoveryCardPresentation.TvSeries,
            SurfaceKind = surface.SurfaceKind,
            HoverLayout = surface.HoverLayout,
            TileImageUrl = surface.TileImageUrl,
            HoverImageUrl = surface.HoverImageUrl,
            HeroBackgroundImageUrl = surface.HeroBackgroundImageUrl,
            PreviewImageUrl = surface.PreviewImageUrl,
            TileImageFitMode = surface.TileImageFitMode,
            HoverImageFitMode = surface.HoverImageFitMode,
            RepresentativeEntityId = representativeWork?.Id ?? latest.WorkId,
            NavigationUrl = detailsUrl,
            PrimaryNavigationUrl = primaryUrl,
            DetailsNavigationUrl = detailsUrl,
            PrimaryActionLabel = ContinueLabel(DiscoveryBucket.Tv),
            ProgressPct = latest.ProgressPct,
            Creator = group?.Creator ?? latest.Author,
            CollectionKey = title,
            SortYear = ParseYear(group?.Year),
            SortTimestamp = latest.LastAccessed,
            IsCollection = true,
        };
    }

    private static DiscoveryCardViewModel CreateMusicContinueCard(
        IReadOnlyList<JourneyItemViewModel> items,
        WorkViewModel? representativeWork)
    {
        var latest = items
            .OrderByDescending(item => item.LastAccessed)
            .First();
        var detailsUrl = BuildJourneyDetailsUrl(latest);
        var title = latest.CollectionDisplayName ?? latest.Series ?? latest.Title;
        var surface = ResolveDiscoverySurface(
            DiscoveryBucket.Music,
            DiscoveryCardShape.Square,
            DiscoveryCardPresentation.Album,
            latest.CoverUrl,
            representativeWork?.SquareUrl,
            latest.BackgroundUrl,
            latest.BannerUrl,
            coverAspectClass: representativeWork?.CoverAspectClass,
            squareAspectClass: representativeWork?.SquareAspectClass,
            backgroundAspectClass: representativeWork?.BackgroundAspectClass,
            bannerAspectClass: representativeWork?.BannerAspectClass);

        return new DiscoveryCardViewModel
        {
            Id = latest.CollectionId ?? latest.WorkId,
            CollectionId = latest.CollectionId,
            Title = title,
            Subtitle = latest.Author,
            Description = TrimTo(latest.Description, 150),
            Tldr = TrimTo(representativeWork?.Tldr, 120),
            CoverUrl = latest.CoverUrl,
            BackgroundUrl = latest.BackgroundUrl,
            BannerUrl = latest.BannerUrl,
            HeroUrl = null,
            LogoUrl = latest.LogoUrl,
            StatusText = BuildContinueStatus(latest, DiscoveryBucket.Music),
            MetaText = JoinPartsSafe($"{items.Count} tracks", latest.TrackNumber is not null ? $"Track {latest.TrackNumber}" : null),
            ContextLines = BuildContextLines(latest.Author),
            VibeTags = BuildVibeTags(representativeWork),
            MediaKind = NormalizeDisplayKind(latest.MediaType),
            AccentColor = representativeWork?.ArtworkAccentHex ?? AccentForBucket(DiscoveryBucket.Music),
            Shape = DiscoveryCardShape.Square,
            Presentation = DiscoveryCardPresentation.Album,
            SurfaceKind = surface.SurfaceKind,
            HoverLayout = surface.HoverLayout,
            TileImageUrl = surface.TileImageUrl,
            HoverImageUrl = surface.HoverImageUrl,
            HeroBackgroundImageUrl = surface.HeroBackgroundImageUrl,
            PreviewImageUrl = surface.PreviewImageUrl,
            TileImageFitMode = surface.TileImageFitMode,
            HoverImageFitMode = surface.HoverImageFitMode,
            RepresentativeEntityId = representativeWork?.Id ?? latest.WorkId,
            NavigationUrl = detailsUrl,
            PrimaryNavigationUrl = detailsUrl,
            DetailsNavigationUrl = detailsUrl,
            PrimaryActionLabel = "Continue album",
            ProgressPct = latest.ProgressPct,
            Creator = latest.Author,
            CollectionKey = title,
            SortYear = 0,
            SortTimestamp = latest.LastAccessed,
            IsCollection = true,
        };
    }

    private static DiscoveryCardViewModel CreateTvFreshArrivalCard(
        ContentGroupViewModel? group,
        IReadOnlyList<WorkViewModel> items,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? groupPreviewImages)
    {
        var latest = items
            .OrderByDescending(GetHomeSortTimestamp)
            .First();
        var detailsUrl = BuildWorkDetailsUrl(latest);
        var title = group?.DisplayName ?? latest.ShowName ?? latest.Series ?? latest.Title;
        var shape = HomeShapeForBucket(
            DiscoveryBucket.Tv,
            group?.BannerUrl ?? latest.BannerUrl,
            group?.BackgroundUrl ?? latest.BackgroundUrl,
            latest.CoverAspectClass,
            latest.SquareAspectClass,
            latest.BackgroundAspectClass,
            latest.BannerAspectClass);
        var surface = ResolveDiscoverySurface(
            DiscoveryBucket.Tv,
            shape,
            DiscoveryCardPresentation.TvSeries,
            group?.CoverUrl ?? latest.CoverUrl,
            latest.SquareUrl,
            group?.BackgroundUrl ?? latest.BackgroundUrl,
            group?.BannerUrl ?? latest.BannerUrl,
            coverAspectClass: latest.CoverAspectClass,
            squareAspectClass: latest.SquareAspectClass,
            backgroundAspectClass: latest.BackgroundAspectClass,
            bannerAspectClass: latest.BannerAspectClass);

        return new DiscoveryCardViewModel
        {
            Id = latest.CollectionId ?? latest.Id,
            CollectionId = latest.CollectionId,
            Title = title,
            Subtitle = group?.Network ?? group?.Creator,
            Description = TrimTo(group?.Description ?? latest.Description ?? group?.Tagline, 150),
            Tldr = TrimTo(latest.Tldr, 120),
            CoverUrl = group?.CoverUrl ?? latest.CoverUrl,
            BackgroundUrl = group?.BackgroundUrl ?? latest.BackgroundUrl,
            BannerUrl = group?.BannerUrl ?? latest.BannerUrl,
            HeroUrl = null,
            LogoUrl = group?.LogoUrl ?? latest.LogoUrl,
            PreviewImages = latest.CollectionId.HasValue ? GetPreviewImages(groupPreviewImages, latest.CollectionId.Value) : [],
            StatusText = Pluralize(items.Count, "new episode"),
            MetaText = JoinPartsSafe(group?.Year, group is not null ? CountLabel(group) : $"{items.Count} episodes"),
            ContextLines = BuildContextLines(group?.Network, group?.Creator),
            VibeTags = BuildVibeTags(latest),
            MediaKind = NormalizeDisplayKind(latest.MediaType),
            AccentColor = latest.ArtworkAccentHex ?? group?.MediaTypeColor ?? AccentForBucket(DiscoveryBucket.Tv),
            Shape = shape,
            Presentation = DiscoveryCardPresentation.TvSeries,
            SurfaceKind = surface.SurfaceKind,
            HoverLayout = surface.HoverLayout,
            TileImageUrl = surface.TileImageUrl,
            HoverImageUrl = surface.HoverImageUrl,
            HeroBackgroundImageUrl = surface.HeroBackgroundImageUrl,
            PreviewImageUrl = surface.PreviewImageUrl,
            TileImageFitMode = surface.TileImageFitMode,
            HoverImageFitMode = surface.HoverImageFitMode,
            RepresentativeEntityId = latest.Id,
            NavigationUrl = detailsUrl,
            PrimaryNavigationUrl = detailsUrl,
            DetailsNavigationUrl = detailsUrl,
            PrimaryActionLabel = "Browse show",
            Creator = group?.Creator,
            CollectionKey = title,
            SortYear = ParseYear(group?.Year ?? latest.Year),
            SortTimestamp = GetHomeSortTimestamp(latest),
            IsCollection = true,
        };
    }

    private static DiscoveryCardViewModel CreateMusicFreshArrivalCard(IReadOnlyList<WorkViewModel> items)
    {
        var latest = items
            .OrderByDescending(GetHomeSortTimestamp)
            .First();
        var detailsUrl = BuildWorkDetailsUrl(latest);
        var title = latest.Album ?? latest.Title;
        var surface = ResolveDiscoverySurface(
            DiscoveryBucket.Music,
            DiscoveryCardShape.Square,
            DiscoveryCardPresentation.Album,
            latest.CoverUrl,
            latest.SquareUrl,
            latest.BackgroundUrl,
            latest.BannerUrl,
            coverAspectClass: latest.CoverAspectClass,
            squareAspectClass: latest.SquareAspectClass,
            backgroundAspectClass: latest.BackgroundAspectClass,
            bannerAspectClass: latest.BannerAspectClass);

        return new DiscoveryCardViewModel
        {
            Id = latest.CollectionId ?? latest.Id,
            CollectionId = latest.CollectionId,
            Title = title,
            Subtitle = latest.Artist ?? latest.Author,
            Description = TrimTo(latest.Description, 150),
            Tldr = TrimTo(latest.Tldr, 120),
            CoverUrl = latest.CoverUrl,
            BackgroundUrl = latest.BackgroundUrl,
            BannerUrl = latest.BannerUrl,
            HeroUrl = null,
            LogoUrl = latest.LogoUrl,
            StatusText = Pluralize(items.Count, "new track"),
            MetaText = JoinPartsSafe(latest.Year, "Album"),
            ContextLines = BuildContextLines(latest.Artist ?? latest.Author),
            VibeTags = BuildVibeTags(latest),
            MediaKind = NormalizeDisplayKind(latest.MediaType),
            AccentColor = latest.ArtworkAccentHex ?? AccentForBucket(DiscoveryBucket.Music),
            Shape = DiscoveryCardShape.Square,
            Presentation = DiscoveryCardPresentation.Album,
            SurfaceKind = surface.SurfaceKind,
            HoverLayout = surface.HoverLayout,
            TileImageUrl = surface.TileImageUrl,
            HoverImageUrl = surface.HoverImageUrl,
            HeroBackgroundImageUrl = surface.HeroBackgroundImageUrl,
            PreviewImageUrl = surface.PreviewImageUrl,
            TileImageFitMode = surface.TileImageFitMode,
            HoverImageFitMode = surface.HoverImageFitMode,
            RepresentativeEntityId = latest.Id,
            NavigationUrl = detailsUrl,
            PrimaryNavigationUrl = detailsUrl,
            DetailsNavigationUrl = detailsUrl,
            PrimaryActionLabel = "Browse album",
            Creator = latest.Artist ?? latest.Author,
            CollectionKey = title,
            SortYear = ParseYear(latest.Year),
            SortTimestamp = GetHomeSortTimestamp(latest),
            IsCollection = true,
        };
    }

    private static DiscoveryHeroViewModel ToHeroFromCard(
        DiscoveryCardViewModel card,
        string eyebrow,
        string accentColor)
    {
        var backdrop = card.HeroBackgroundImageUrl ?? card.BackgroundUrl ?? card.BannerUrl;

        return new DiscoveryHeroViewModel
        {
            Eyebrow = eyebrow,
            Title = card.Title,
            Subtitle = card.Subtitle,
            Description = TrimTo(card.Description, 240),
            Tldr = TrimTo(card.Tldr, 140),
            VibeTags = card.VibeTags,
            BackgroundImageUrl = backdrop,
            HeroBackgroundImageUrl = backdrop,
            BannerImageUrl = card.BannerUrl,
            PreviewImageUrl = card.PreviewImageUrl ?? card.CoverUrl ?? card.TileImageUrl,
            TileImageFitMode = card.TileImageFitMode,
            HoverImageFitMode = card.HoverImageFitMode,
            LogoUrl = card.LogoUrl,
            AccentColor = string.IsNullOrWhiteSpace(card.AccentColor) ? accentColor : card.AccentColor,
            StatusText = card.StatusText,
            MetaText = card.MetaText,
            ProgressPct = card.ProgressPct,
            RepresentativeEntityId = card.RepresentativeEntityId ?? card.WorkId ?? card.CollectionId,
            SurfaceKind = card.SurfaceKind,
            PrimaryActionLabel = card.PrimaryActionLabel,
            PrimaryNavigationUrl = card.PrimaryNavigationUrl ?? card.NavigationUrl,
            SecondaryActionLabel = "Details",
            SecondaryNavigationUrl = card.DetailsNavigationUrl ?? card.NavigationUrl,
        };
    }

    private static bool SuppressIndividualOnHome(WorkViewModel work) =>
        work.CollectionId.HasValue && GetBucket(work.MediaType) is DiscoveryBucket.Tv or DiscoveryBucket.Music;

    private static bool SuppressIndividualOnHome(JourneyItemViewModel item) =>
        item.CollectionId.HasValue && GetBucket(item.MediaType) is DiscoveryBucket.Tv or DiscoveryBucket.Music;

    private static string BuildCollectionDetailsUrl(DiscoveryBucket bucket, Guid collectionId) => bucket switch
    {
        DiscoveryBucket.Tv => $"/watch/tv/show/{collectionId}",
        _ => $"/collection/{collectionId}",
    };

    private static string BuildContinueStatus(JourneyItemViewModel item, DiscoveryBucket bucket) => bucket switch
    {
        DiscoveryBucket.Tv when !string.IsNullOrWhiteSpace(item.SeasonNumber) && !string.IsNullOrWhiteSpace(item.EpisodeNumber)
            => $"Continue S{item.SeasonNumber}:E{item.EpisodeNumber} · {Math.Max(1, item.ProgressPct):F0}%",
        DiscoveryBucket.Music when !string.IsNullOrWhiteSpace(item.TrackNumber)
            => $"Continue album · Track {item.TrackNumber}",
        DiscoveryBucket.Music => "Continue album",
        _ => item.ActionLabel,
    };

    private static IReadOnlyList<string> BuildContextLines(params string?[] values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Cast<string>()
            .ToList();

    private static string Pluralize(int count, string singular) =>
        count == 1 ? $"1 {singular}" : $"{count} {singular}s";

    private static DiscoveryCardViewModel CreateCollectionShelfCard(
        ContentGroupViewModel group,
        DiscoveryCardShape shape,
        DiscoveryCardPresentation presentation,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? groupPreviewImages) =>
        ToCollectionCard(
            group,
            shape,
            presentation,
            GetPreviewImages(groupPreviewImages, group.CollectionId));

    private static DiscoveryCardViewModel CreateTvCollectionCard(
        ContentGroupViewModel group,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? groupPreviewImages) =>
        ToCollectionCard(
            group,
            DiscoveryCardShape.Landscape,
            DiscoveryCardPresentation.TvSeries,
            GetPreviewImages(groupPreviewImages, group.CollectionId),
            navigationUrl: BuildCollectionDetailsUrl(DiscoveryBucket.Tv, group.CollectionId),
            detailsNavigationUrl: BuildCollectionDetailsUrl(DiscoveryBucket.Tv, group.CollectionId),
            primaryActionLabel: "Browse show",
            primaryNavigationUrl: BuildCollectionDetailsUrl(DiscoveryBucket.Tv, group.CollectionId));

    private static DiscoveryCardViewModel CreateAlbumGroupCard(ContentGroupViewModel group) =>
        ToSystemViewCard(
            group,
            groupType: "album",
            groupField: "album",
            routeBase: "/listen",
            routeTab: "music",
            presentation: DiscoveryCardPresentation.Album,
            shape: DiscoveryCardShape.Square);

    private static IReadOnlyList<DiscoveryShelfViewModel> BuildHomeCollectionShelves(
        IReadOnlyList<ContentGroupViewModel> contentGroups,
        IReadOnlyList<ContentGroupViewModel> musicAlbumGroups,
        IReadOnlyList<ContentGroupViewModel> musicArtistGroups,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? groupPreviewImages)
    {
        var shelves = new List<DiscoveryShelfViewModel>();

        AddHomeCollectionShelf(
            shelves,
            title: "TV Series",
            subtitle: "Shows and seasons organized from your library",
            seeAllRoute: "/watch/tv",
            items: contentGroups
                .Where(ShouldDisplayCollectionGroup)
                .Where(group => GetBucket(group.PrimaryMediaType) == DiscoveryBucket.Tv)
                .Select(group => CreateTvCollectionCard(group, groupPreviewImages)));

        AddHomeCollectionShelf(
            shelves,
            title: "Movie Series",
            subtitle: "Franchises and sequels presented as stacked movie cards",
            seeAllRoute: "/watch/movies",
            items: contentGroups
                .Where(ShouldDisplayCollectionGroup)
                .Where(group => GetBucket(group.PrimaryMediaType) == DiscoveryBucket.Movie)
                .Select(group => CreateCollectionShelfCard(
                    group,
                    DiscoveryCardShape.Landscape,
                    DiscoveryCardPresentation.MovieSeries,
                    groupPreviewImages)));

        AddHomeCollectionShelf(
            shelves,
            title: "Book Series",
            subtitle: "Reading sequences collected from the books you own",
            seeAllRoute: "/read/books",
            items: contentGroups
                .Where(ShouldDisplayCollectionGroup)
                .Where(group => GetBucket(group.PrimaryMediaType) == DiscoveryBucket.Book)
                .Select(group => CreateCollectionShelfCard(
                    group,
                    DiscoveryCardShape.Portrait,
                    DiscoveryCardPresentation.BookSeries,
                    groupPreviewImages)));

        AddHomeCollectionShelf(
            shelves,
            title: "Comic Series",
            subtitle: "Issue runs and comic arcs grouped for quick browsing",
            seeAllRoute: "/read/comics",
            items: contentGroups
                .Where(ShouldDisplayCollectionGroup)
                .Where(group => GetBucket(group.PrimaryMediaType) == DiscoveryBucket.Comic)
                .Select(group => CreateCollectionShelfCard(
                    group,
                    DiscoveryCardShape.Portrait,
                    DiscoveryCardPresentation.ComicSeries,
                    groupPreviewImages)));

        AddHomeCollectionShelf(
            shelves,
            title: "Albums",
            subtitle: "Album-first listening with cover art at the center",
            seeAllRoute: "/listen/music",
            items: musicAlbumGroups.Select(CreateAlbumGroupCard));

        AddHomeCollectionShelf(
            shelves,
            title: "Artists",
            subtitle: "Artist-led listening built from the music already in your library",
            seeAllRoute: "/listen/music",
            items: musicArtistGroups.Select(group => ToSystemViewCard(
                group,
                groupType: "artist",
                groupField: "artist",
                routeBase: "/listen",
                routeTab: "music",
                presentation: DiscoveryCardPresentation.Artist,
                shape: DiscoveryCardShape.Square)));

        AddHomeCollectionShelf(
            shelves,
            title: "Audiobook Series",
            subtitle: "Series-aware audiobook browsing without recommendations",
            seeAllRoute: "/listen/audiobooks",
            items: contentGroups
                .Where(ShouldDisplayCollectionGroup)
                .Where(group => GetBucket(group.PrimaryMediaType) == DiscoveryBucket.Audiobook)
                .Select(group => CreateCollectionShelfCard(
                    group,
                    DiscoveryCardShape.Portrait,
                    DiscoveryCardPresentation.AudiobookSeries,
                    groupPreviewImages)));

        return shelves;
    }

    private static void AddHomeCollectionShelf(
        ICollection<DiscoveryShelfViewModel> shelves,
        string title,
        string subtitle,
        string seeAllRoute,
        IEnumerable<DiscoveryCardViewModel> items)
    {
        var shelfItems = TakeShelfItems(items, 10);
        if (shelfItems.Count == 0)
        {
            return;
        }

        shelves.Add(new DiscoveryShelfViewModel
        {
            Title = title,
            Subtitle = subtitle,
            Items = shelfItems,
            SeeAllRoute = seeAllRoute,
        });
    }

    private static bool HasWideArtwork(
        string? backgroundUrl,
        string? bannerUrl,
        string? backgroundAspectClass = null,
        string? bannerAspectClass = null)
    {
        if (!string.IsNullOrWhiteSpace(backgroundUrl))
        {
            return string.IsNullOrWhiteSpace(backgroundAspectClass)
                || string.Equals(backgroundAspectClass, ArtworkAspectClasses.LandscapeWide, StringComparison.OrdinalIgnoreCase)
                || string.Equals(backgroundAspectClass, ArtworkAspectClasses.BannerStrip, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(bannerUrl))
        {
            return string.IsNullOrWhiteSpace(bannerAspectClass)
                || string.Equals(bannerAspectClass, ArtworkAspectClasses.LandscapeWide, StringComparison.OrdinalIgnoreCase)
                || string.Equals(bannerAspectClass, ArtworkAspectClasses.BannerStrip, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsSquareArtwork(string? aspectClass) =>
        string.Equals(aspectClass, ArtworkAspectClasses.Square, StringComparison.OrdinalIgnoreCase);

    private static bool ShouldDisplayCollectionGroup(ContentGroupViewModel group) =>
        group.WorkCount > 1
        || group.SeasonCount is > 1
        || group.AlbumCount is > 1;

    private static DiscoveryCardShape HomeShapeForBucket(
        DiscoveryBucket bucket,
        string? bannerUrl,
        string? backgroundUrl = null,
        string? coverAspectClass = null,
        string? squareAspectClass = null,
        string? backgroundAspectClass = null,
        string? bannerAspectClass = null) =>
        ResolveShapeForArtwork(
            bucket,
            backgroundUrl,
            bannerUrl,
            coverAspectClass,
            squareAspectClass,
            backgroundAspectClass,
            bannerAspectClass);

    private static DiscoverySurfaceSelection ResolveDiscoverySurface(
        DiscoveryBucket bucket,
        DiscoveryCardShape shape,
        DiscoveryCardPresentation presentation,
        string? coverUrl,
        string? squareUrl,
        string? backgroundUrl,
        string? bannerUrl,
        string? artistPhotoUrl = null,
        string? coverAspectClass = null,
        string? squareAspectClass = null,
        string? backgroundAspectClass = null,
        string? bannerAspectClass = null)
    {
        var artistImageUrl = FirstNonBlank(artistPhotoUrl, squareUrl, coverUrl, backgroundUrl, bannerUrl);
        if (presentation == DiscoveryCardPresentation.Artist && !string.IsNullOrWhiteSpace(artistImageUrl))
        {
            var previewImageUrl = FirstNonBlank(squareUrl, coverUrl, backgroundUrl, bannerUrl, artistImageUrl);
            return new DiscoverySurfaceSelection(
                DiscoverySurfaceKind.ArtistPhotoSquare,
                DiscoveryHoverLayout.ArtOnlyPopover,
                TileImageUrl: artistImageUrl,
                HoverImageUrl: artistImageUrl,
                HeroBackgroundImageUrl: artistImageUrl,
                PreviewImageUrl: previewImageUrl,
                TileImageFitMode: DiscoveryImageFitMode.Fill,
                HoverImageFitMode: DiscoveryImageFitMode.Contain);
        }

        if (bucket is DiscoveryBucket.Movie or DiscoveryBucket.Tv
            && shape == DiscoveryCardShape.Landscape
            && HasWideArtwork(backgroundUrl, bannerUrl, backgroundAspectClass, bannerAspectClass))
        {
            var wideImageUrl = FirstNonBlank(backgroundUrl, bannerUrl, coverUrl);
            return new DiscoverySurfaceSelection(
                DiscoverySurfaceKind.BannerLandscape,
                DiscoveryHoverLayout.BannerPopover,
                TileImageUrl: wideImageUrl,
                HoverImageUrl: wideImageUrl,
                HeroBackgroundImageUrl: FirstNonBlank(backgroundUrl, bannerUrl),
                PreviewImageUrl: FirstNonBlank(coverUrl, squareUrl, backgroundUrl, bannerUrl),
                TileImageFitMode: DiscoveryImageFitMode.Contain,
                HoverImageFitMode: DiscoveryImageFitMode.Contain);
        }

        var coverImageUrl = shape == DiscoveryCardShape.Square
            ? FirstNonBlank(squareUrl, coverUrl, artistPhotoUrl, backgroundUrl, bannerUrl)
            : FirstNonBlank(coverUrl, squareUrl, artistPhotoUrl, backgroundUrl, bannerUrl);
        var surfaceKind = shape == DiscoveryCardShape.Square
            ? DiscoverySurfaceKind.CoverSquare
            : DiscoverySurfaceKind.CoverPortrait;

        return new DiscoverySurfaceSelection(
            surfaceKind,
            DiscoveryHoverLayout.ArtOnlyPopover,
            TileImageUrl: coverImageUrl,
            HoverImageUrl: coverImageUrl,
            HeroBackgroundImageUrl: coverImageUrl,
            PreviewImageUrl: coverImageUrl,
            TileImageFitMode: DiscoveryImageFitMode.Fill,
            HoverImageFitMode: DiscoveryImageFitMode.Contain);
    }

    private static IReadOnlyList<string> BuildVibeTags(WorkViewModel? work)
    {
        if (work is null)
        {
            return [];
        }

        return work.Vibes
            .Concat(work.Moods)
            .Concat(work.Themes)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
    }

    private static IReadOnlyList<DiscoveryCardViewModel> RankHomeCards(
        IEnumerable<DiscoveryCardViewModel> cards,
        TasteProfile? tasteProfile)
    {
        return cards
            .GroupBy(GetCardIdentity)
            .Select(group => group
                .OrderByDescending(card => card.SortTimestamp)
                .ThenByDescending(card => card.SortYear)
                .First())
            .OrderByDescending(card => card.SortTimestamp.UtcDateTime.Date)
            .ThenByDescending(card => CardTasteScore(card, tasteProfile))
            .ThenByDescending(card => card.SortTimestamp)
            .ThenByDescending(card => card.SortYear)
            .ToList();
    }

    private static bool IsBannerWatchCard(DiscoveryCardViewModel card) =>
        IsWatchKind(card.MediaKind)
        && card.SurfaceKind == DiscoverySurfaceKind.BannerLandscape
        && card.Shape == DiscoveryCardShape.Landscape;

    private static bool IsPosterWatchCard(DiscoveryCardViewModel card) =>
        IsWatchKind(card.MediaKind)
        && card.SurfaceKind == DiscoverySurfaceKind.CoverPortrait
        && card.Shape == DiscoveryCardShape.Portrait;

    private static IReadOnlyList<DiscoveryCardViewModel> MergeHomeCards(
        IEnumerable<DiscoveryCardViewModel> primaryCards,
        IEnumerable<DiscoveryCardViewModel> secondaryCards)
    {
        return primaryCards
            .Concat(secondaryCards)
            .GroupBy(GetCardIdentity)
            .Select(group => group
                .OrderByDescending(card => card.SortTimestamp)
                .ThenByDescending(card => card.SortYear)
                .First())
            .OrderByDescending(card => card.SortTimestamp)
            .ThenByDescending(card => card.SortYear)
            .ToList();
    }

    private static IReadOnlyList<DiscoveryShelfViewModel> BuildShelfGroup(params DiscoveryShelfViewModel?[] shelves) =>
        shelves
            .Where(shelf => shelf is not null)
            .Cast<DiscoveryShelfViewModel>()
            .ToList();

    private static DiscoveryShelfViewModel? CreateShelf(
        string title,
        string subtitle,
        IReadOnlyList<DiscoveryCardViewModel> items,
        string? seeAllRoute = null)
    {
        if (items.Count == 0)
        {
            return null;
        }

        return new DiscoveryShelfViewModel
        {
            Title = title,
            Subtitle = subtitle,
            Items = items,
            SeeAllRoute = seeAllRoute,
        };
    }

    private static double ScoreHomeFamily(
        string familyKey,
        IEnumerable<DiscoveryCardViewModel> cards,
        TasteProfile? tasteProfile)
    {
        var familyCards = cards.ToList();
        if (familyCards.Count == 0)
        {
            return double.NegativeInfinity;
        }

        var mediaPreference = FamilyTasteScore(familyKey, tasteProfile);
        var cardPreference = familyCards.Average(card => CardTasteScore(card, tasteProfile));
        var recencyScore = familyCards
            .Select(card => Math.Max(0, 30 - (DateTimeOffset.UtcNow - card.SortTimestamp).TotalDays))
            .DefaultIfEmpty(0)
            .Max();

        return (mediaPreference * 12d) + (cardPreference * 4d) + recencyScore + Math.Min(familyCards.Count, 12);
    }

    private static string BuildHomeSubtitle(
        string fallback,
        string familyKey,
        TasteProfile? tasteProfile)
    {
        var tasteSuffix = BuildTasteShelfHint(familyKey, tasteProfile);
        return string.IsNullOrWhiteSpace(tasteSuffix)
            ? fallback
            : $"{fallback} {tasteSuffix}";
    }

    private static string? BuildTasteShelfHint(string familyKey, TasteProfile? tasteProfile)
    {
        if (tasteProfile is null)
        {
            return null;
        }

        var topMood = tasteProfile.MoodPreferences
            .OrderByDescending(entry => entry.Value)
            .Select(entry => entry.Key)
            .FirstOrDefault(key => !string.IsNullOrWhiteSpace(key));
        var topGenre = tasteProfile.GenreDistribution
            .OrderByDescending(entry => entry.Value)
            .Select(entry => entry.Key)
            .FirstOrDefault(key => !string.IsNullOrWhiteSpace(key));
        if (!string.IsNullOrWhiteSpace(topMood) && !string.IsNullOrWhiteSpace(topGenre))
        {
            return $"Because you like {topMood} {topGenre} stories.";
        }

        if (!string.IsNullOrWhiteSpace(topGenre))
        {
            return $"Because you like {topGenre} picks.";
        }

        if (!string.IsNullOrWhiteSpace(topMood))
        {
            return $"Because you like {topMood} moods.";
        }

        var summary = TrimTo(tasteProfile.Summary, 84);
        return string.IsNullOrWhiteSpace(summary)
            ? null
            : $"Because you like {summary.Trim().TrimEnd('.')}.";        
    }

    private static double GroupTasteScore(ContentGroupViewModel group, TasteProfile? tasteProfile) =>
        MediaTypeTasteScore(tasteProfile, NormalizeDisplayKind(group.PrimaryMediaType))
        + Math.Min(group.WorkCount, 24) / 12d;

    private static double FamilyTasteScore(string familyKey, TasteProfile? tasteProfile) => familyKey switch
    {
        "watch" => Math.Max(
            MediaTypeTasteScore(tasteProfile, "Movie"),
            MediaTypeTasteScore(tasteProfile, "TV")),
        "read" => Math.Max(
            MediaTypeTasteScore(tasteProfile, "Book"),
            MediaTypeTasteScore(tasteProfile, "Comic")),
        "audiobook" => MediaTypeTasteScore(tasteProfile, "Audiobook"),
        "music" => MediaTypeTasteScore(tasteProfile, "Music"),
        _ => 0,
    };

    private static double CardTasteScore(DiscoveryCardViewModel card, TasteProfile? tasteProfile)
    {
        if (tasteProfile is null)
        {
            return 0;
        }

        var mediaScore = MediaTypeTasteScore(tasteProfile, card.MediaKind);
        var genreScore = card.Genres
            .Select(genre => PreferenceScore(tasteProfile.GenreDistribution, genre))
            .DefaultIfEmpty(0)
            .Max();
        var moodScore = card.VibeTags
            .Select(tag => PreferenceScore(tasteProfile.MoodPreferences, tag))
            .DefaultIfEmpty(0)
            .Max();

        return (mediaScore * 2d) + genreScore + moodScore;
    }

    private static double MediaTypeTasteScore(TasteProfile? tasteProfile, string? mediaKind)
    {
        if (tasteProfile is null || string.IsNullOrWhiteSpace(mediaKind))
        {
            return 0;
        }

        var normalizedMediaKind = NormalizeTasteKey(mediaKind);
        return tasteProfile.MediaTypeMix
            .Where(entry => NormalizeTasteKey(entry.Key) == normalizedMediaKind)
            .Select(entry => entry.Value)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static double PreferenceScore(IReadOnlyDictionary<string, double> preferences, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var normalizedValue = NormalizeTasteKey(value);
        return preferences
            .Where(entry => NormalizeTasteKey(entry.Key) == normalizedValue)
            .Select(entry => entry.Value)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static string NormalizeTasteKey(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        normalized = normalized switch
        {
            "books" => "book",
            "movies" => "movie",
            "audiobooks" => "audiobook",
            "comics" => "comic",
            _ => normalized,
        };

        return new string(normalized
            .Where(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
            .ToArray());
    }

    private static Guid GetCardIdentity(DiscoveryCardViewModel card) =>
        card.CollectionId
        ?? card.WorkId
        ?? card.RepresentativeEntityId
        ?? card.Id;

    private static int GetAffinityScore(
        IEnumerable<JourneyItemViewModel> journey,
        Func<DiscoveryBucket, bool> matches)
    {
        var matchingJourney = journey.Count(item => matches(GetBucket(item.MediaType)));
        return (matchingJourney * 10) + 1;
    }

    private static DiscoveryHeroViewModel? BuildHero(
        IReadOnlyList<JourneyItemViewModel> journey,
        IReadOnlyList<WorkViewModel> works,
        IReadOnlyList<ContentGroupViewModel> groups,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? groupPreviewImages,
        string accentColor,
        string journeyEyebrow,
        string workEyebrow,
        string groupEyebrow)
    {
        var workLookup = works.ToDictionary(work => work.Id);
        var activeJourney = journey.OrderByDescending(item => item.LastAccessed).FirstOrDefault();
        if (activeJourney is not null)
        {
            var journeyCard = ToJourneyCard(activeJourney, workLookup.GetValueOrDefault(activeJourney.WorkId));
            return ToHeroFromCard(journeyCard, journeyEyebrow, accentColor);
        }

        var newestWork = works.OrderByDescending(GetSortTimestamp).FirstOrDefault();
        if (newestWork is not null)
        {
            var progress = journey
                .Where(item => item.WorkId == newestWork.Id)
                .Select(item => item.ProgressPct)
                .DefaultIfEmpty()
                .Max();

            return ToHeroFromWork(newestWork, accentColor, workEyebrow, progress > 0 ? progress : null);
        }

        var featuredGroup = groups.OrderByDescending(group => group.WorkCount).ThenByDescending(group => group.CreatedAt).FirstOrDefault();
        if (featuredGroup is not null)
        {
            var previewImages = GetPreviewImages(groupPreviewImages, featuredGroup.CollectionId);
            var featuredCard = ToCollectionCard(
                featuredGroup,
                OverrideShapeForGroup(featuredGroup, "home"),
                DiscoveryCardPresentation.Default,
                previewImages,
                navigationUrl: MediaNavigation.ForContentGroup(featuredGroup),
                detailsNavigationUrl: MediaNavigation.ForContentGroup(featuredGroup),
                primaryActionLabel: "Explore collection",
                primaryNavigationUrl: MediaNavigation.ForContentGroup(featuredGroup));
            return ToHeroFromCard(featuredCard, groupEyebrow, accentColor);
        }

        return null;
    }

    private static DiscoveryHeroViewModel ToHeroFromWork(
        WorkViewModel work,
        string accentColor,
        string eyebrow,
        double? progressPct = null) =>
        ToHeroFromCard(
            ToWorkCard(work, progressPct),
            eyebrow,
            work.ArtworkAccentHex ?? accentColor);

    private static DiscoveryCardViewModel ToJourneyCard(
        JourneyItemViewModel item,
        WorkViewModel? representativeWork = null,
        DiscoveryCardShape? shapeOverride = null)
    {
        var bucket = GetBucket(item.MediaType);
        var shape = shapeOverride ?? ResolveShapeForArtwork(
            bucket,
            item.BackgroundUrl,
            item.BannerUrl,
            representativeWork?.CoverAspectClass,
            representativeWork?.SquareAspectClass,
            representativeWork?.BackgroundAspectClass,
            representativeWork?.BannerAspectClass);
        var surface = ResolveDiscoverySurface(
            bucket,
            shape,
            DiscoveryCardPresentation.Default,
            item.CoverUrl,
            representativeWork?.SquareUrl,
            item.BackgroundUrl,
            item.BannerUrl,
            coverAspectClass: representativeWork?.CoverAspectClass,
            squareAspectClass: representativeWork?.SquareAspectClass,
            backgroundAspectClass: representativeWork?.BackgroundAspectClass,
            bannerAspectClass: representativeWork?.BannerAspectClass);

        return new DiscoveryCardViewModel
        {
            Id = item.WorkId,
            WorkId = item.WorkId,
            CollectionId = item.CollectionId,
            Title = item.Title,
            Subtitle = item.Author,
            Description = TrimTo(item.Description, 150),
            Tldr = TrimTo(representativeWork?.Tldr, 120),
            CoverUrl = item.CoverUrl,
            BackgroundUrl = item.BackgroundUrl,
            BannerUrl = item.BannerUrl,
            HeroUrl = null,
            LogoUrl = item.LogoUrl,
            StatusText = item.ProgressPct > 0 ? item.ActionLabel : null,
            MetaText = JoinPartsSafe(
                NormalizeDisplayKind(item.MediaType),
                item.Series,
                item.ProgressDisplay),
            ContextLines = BuildContextLines(item.Author, item.Narrator),
            VibeTags = BuildVibeTags(representativeWork),
            MediaKind = NormalizeDisplayKind(item.MediaType),
            AccentColor = representativeWork?.ArtworkAccentHex ?? AccentForBucket(bucket),
            Shape = shape,
            SurfaceKind = surface.SurfaceKind,
            HoverLayout = surface.HoverLayout,
            TileImageUrl = surface.TileImageUrl,
            HoverImageUrl = surface.HoverImageUrl,
            HeroBackgroundImageUrl = surface.HeroBackgroundImageUrl,
            PreviewImageUrl = surface.PreviewImageUrl,
            TileImageFitMode = surface.TileImageFitMode,
            HoverImageFitMode = surface.HoverImageFitMode,
            RepresentativeEntityId = representativeWork?.Id ?? item.WorkId,
            NavigationUrl = BuildJourneyDetailsUrl(item),
            PrimaryNavigationUrl = MediaNavigation.ForJourney(item),
            DetailsNavigationUrl = BuildJourneyDetailsUrl(item),
            PrimaryActionLabel = item.ActionVerb,
            ProgressPct = item.ProgressPct,
            Creator = item.Author,
            CollectionKey = item.Series,
            SortYear = 0,
            SortTimestamp = item.LastAccessed,
        };
    }

    private static DiscoveryCardViewModel ToWorkCard(
        WorkViewModel work,
        double? progressPct = null,
        DiscoveryCardShape? shapeOverride = null)
    {
        var bucket = GetBucket(work.MediaType);
        var shape = shapeOverride ?? ResolveShapeForArtwork(
            bucket,
            work.BackgroundUrl,
            work.BannerUrl,
            work.CoverAspectClass,
            work.SquareAspectClass,
            work.BackgroundAspectClass,
            work.BannerAspectClass);
        var surface = ResolveDiscoverySurface(
            bucket,
            shape,
            DiscoveryCardPresentation.Default,
            work.CoverUrl,
            work.SquareUrl,
            work.BackgroundUrl,
            work.BannerUrl,
            coverAspectClass: work.CoverAspectClass,
            squareAspectClass: work.SquareAspectClass,
            backgroundAspectClass: work.BackgroundAspectClass,
            bannerAspectClass: work.BannerAspectClass);

        return new DiscoveryCardViewModel
        {
            Id = work.Id,
            WorkId = work.Id,
            CollectionId = work.CollectionId,
            Title = work.Title,
            Subtitle = work.Author,
            Description = TrimTo(work.Description, 150),
            Tldr = TrimTo(work.Tldr, 120),
            CoverUrl = work.CoverUrl,
            BackgroundUrl = work.BackgroundUrl,
            BannerUrl = work.BannerUrl,
            HeroUrl = null,
            LogoUrl = work.LogoUrl,
            MetaText = JoinPartsSafe(
                NormalizeDisplayKind(work.MediaType),
                work.Year,
                work.Genres.FirstOrDefault()),
            ContextLines = BuildContextLines(
                work.Author,
                string.Equals(work.MediaType, "TV", StringComparison.OrdinalIgnoreCase) ? work.Network : work.Director,
                work.Network ?? work.Artist),
            VibeTags = BuildVibeTags(work),
            MediaKind = NormalizeDisplayKind(work.MediaType),
            AccentColor = work.ArtworkAccentHex ?? AccentForBucket(bucket),
            Shape = shape,
            SurfaceKind = surface.SurfaceKind,
            HoverLayout = surface.HoverLayout,
            TileImageUrl = surface.TileImageUrl,
            HoverImageUrl = surface.HoverImageUrl,
            HeroBackgroundImageUrl = surface.HeroBackgroundImageUrl,
            PreviewImageUrl = surface.PreviewImageUrl,
            TileImageFitMode = surface.TileImageFitMode,
            HoverImageFitMode = surface.HoverImageFitMode,
            RepresentativeEntityId = work.Id,
            NavigationUrl = BuildWorkDetailsUrl(work),
            PrimaryNavigationUrl = MediaNavigation.ForWork(work),
            DetailsNavigationUrl = BuildWorkDetailsUrl(work),
            PrimaryActionLabel = progressPct is > 0 ? ContinueLabel(bucket) : "Open",
            ProgressPct = progressPct,
            Creator = work.Author,
            CollectionKey = work.Series,
            Genres = work.Genres,
            SortYear = ParseYear(work.Year),
            SortTimestamp = GetSortTimestamp(work),
        };
    }

    private static DiscoveryCardViewModel ToHomeJourneyCard(
        JourneyItemViewModel item,
        WorkViewModel? representativeWork = null)
        => ToJourneyCard(
            item,
            representativeWork,
            HomeShapeForBucket(
                GetBucket(item.MediaType),
                item.BannerUrl,
                item.BackgroundUrl,
                representativeWork?.CoverAspectClass,
                representativeWork?.SquareAspectClass,
                representativeWork?.BackgroundAspectClass,
                representativeWork?.BannerAspectClass));

    private static DiscoveryCardViewModel ToHomeWorkCard(WorkViewModel work)
        => ToWorkCard(
            work,
            shapeOverride: HomeShapeForBucket(
                GetBucket(work.MediaType),
                work.BannerUrl,
                work.BackgroundUrl,
                work.CoverAspectClass,
                work.SquareAspectClass,
                work.BackgroundAspectClass,
                work.BannerAspectClass));

    private static DiscoveryCardViewModel ToCollectionCard(
        ContentGroupViewModel group,
        DiscoveryCardShape shape,
        IReadOnlyList<string>? previewImages = null)
        => ToCollectionCard(group, shape, DiscoveryCardPresentation.Default, previewImages);

    private static DiscoveryCardViewModel ToCollectionCard(
        ContentGroupViewModel group,
        DiscoveryCardShape shape,
        DiscoveryCardPresentation presentation,
        IReadOnlyList<string>? previewImages = null,
        string? navigationUrl = null,
        string? detailsNavigationUrl = null,
        string? primaryActionLabel = null,
        string? primaryNavigationUrl = null,
        string? statusText = null)
    {
        var bucket = GetBucket(group.PrimaryMediaType);
        var prefersArtistImage = presentation == DiscoveryCardPresentation.Artist;
        var coverUrl = prefersArtistImage
            ? group.ArtistPhotoUrl ?? group.CoverUrl
            : group.CoverUrl ?? group.ArtistPhotoUrl;
        var backgroundUrl = prefersArtistImage
            ? group.ArtistPhotoUrl
            : group.BackgroundUrl;
        var surface = ResolveDiscoverySurface(
            bucket,
            shape,
            presentation,
            coverUrl,
            null,
            backgroundUrl,
            group.BannerUrl,
            group.ArtistPhotoUrl);

        return new DiscoveryCardViewModel
        {
            Id = group.CollectionId,
            CollectionId = group.CollectionId,
            Title = group.DisplayName,
            Subtitle = group.Creator ?? group.Network,
            Description = TrimTo(group.Description ?? group.Tagline ?? BuildGroupDescriptionSafe(group), 150),
            CoverUrl = coverUrl,
            BackgroundUrl = backgroundUrl,
            BannerUrl = group.BannerUrl,
            HeroUrl = null,
            LogoUrl = group.LogoUrl,
            PreviewImages = previewImages ?? [],
            StatusText = statusText,
            MetaText = JoinPartsSafe(group.Year, CountLabel(group)),
            ContextLines = BuildContextLines(group.Network, group.Creator, group.Writer),
            MediaKind = NormalizeDisplayKind(group.PrimaryMediaType),
            AccentColor = !string.IsNullOrWhiteSpace(group.MediaTypeColor) ? group.MediaTypeColor : AccentForBucket(bucket),
            Shape = shape,
            Presentation = presentation,
            SurfaceKind = surface.SurfaceKind,
            HoverLayout = surface.HoverLayout,
            TileImageUrl = surface.TileImageUrl,
            HoverImageUrl = surface.HoverImageUrl,
            HeroBackgroundImageUrl = surface.HeroBackgroundImageUrl,
            PreviewImageUrl = surface.PreviewImageUrl,
            TileImageFitMode = surface.TileImageFitMode,
            HoverImageFitMode = surface.HoverImageFitMode,
            RepresentativeEntityId = group.CollectionId,
            NavigationUrl = navigationUrl ?? MediaNavigation.ForContentGroup(group),
            PrimaryNavigationUrl = primaryNavigationUrl ?? navigationUrl ?? MediaNavigation.ForContentGroup(group),
            DetailsNavigationUrl = detailsNavigationUrl ?? navigationUrl ?? MediaNavigation.ForContentGroup(group),
            PrimaryActionLabel = primaryActionLabel ?? "Explore",
            Creator = group.Creator,
            CollectionKey = group.DisplayName,
            SortYear = ParseYear(group.Year),
            SortTimestamp = group.CreatedAt,
            IsCollection = true,
        };
    }

    private static DiscoveryCardViewModel ToSystemViewCard(
        ContentGroupViewModel group,
        string groupType,
        string groupField,
        string routeBase,
        string routeTab,
        DiscoveryCardPresentation presentation,
        DiscoveryCardShape shape)
    {
        var navigationUrl = BuildSystemViewNavigationUrl(routeBase, routeTab, groupType, groupField, group.DisplayName, group.PrimaryMediaType);
        return ToCollectionCard(
            group,
            shape,
            presentation,
            previewImages: [],
            navigationUrl: navigationUrl,
            detailsNavigationUrl: navigationUrl,
            primaryActionLabel: "Browse",
            primaryNavigationUrl: navigationUrl);
    }

    private static DiscoveryHubViewModel ToHub(ContentGroupViewModel group, IReadOnlyList<string>? previewImages = null) => new()
    {
        Id = group.CollectionId,
        Title = group.DisplayName,
        Subtitle = group.Creator ?? group.Network,
        Description = BuildGroupDescriptionSafe(group),
        ImageUrl = group.CoverUrl ?? group.ArtistPhotoUrl,
        PreviewImages = previewImages ?? [],
        AccentColor = group.MediaTypeColor,
        Badge = NormalizeDisplayKind(group.PrimaryMediaType),
        CountLabel = CountLabel(group),
        NavigationUrl = MediaNavigation.ForContentGroup(group),
    };

    private static string BuildSystemViewNavigationUrl(
        string routeBase,
        string routeTab,
        string groupType,
        string groupField,
        string groupName,
        string mediaType)
    {
        if (string.Equals(routeBase, "/listen", StringComparison.OrdinalIgnoreCase)
            && string.Equals(routeTab, "music", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(groupType, "artist", StringComparison.OrdinalIgnoreCase))
            {
                return $"/listen/music/artists/{Uri.EscapeDataString(groupName)}";
            }

            if (string.Equals(groupType, "album", StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(groupName, out var albumId))
            {
                return $"/listen/music/albums/{albumId}";
            }
        }

        var path = $"{routeBase.TrimEnd('/')}/{routeTab}";
        var groupId = CreateDeterministicGuid($"{groupField}:{groupName}");
        return $"{path}?group={groupId}&groupType={Uri.EscapeDataString(groupType)}&groupName={Uri.EscapeDataString(groupName)}&groupField={Uri.EscapeDataString(groupField)}&groupMediaType={Uri.EscapeDataString(mediaType)}";
    }

    private static string BuildJourneyDetailsUrl(JourneyItemViewModel item)
        => item.CollectionId.HasValue
            ? MediaNavigation.ForCollectionMedia(item.MediaType, item.CollectionId.Value, item.WorkId)
            : MediaNavigation.ForJourney(item);

    private static string BuildWorkDetailsUrl(WorkViewModel work)
        => work.CollectionId.HasValue
            ? MediaNavigation.ForCollectionMedia(work.MediaType, work.CollectionId.Value, work.Id)
            : MediaNavigation.ForWork(work);

    private static Dictionary<Guid, double> BuildProgressLookup(IEnumerable<JourneyItemViewModel> journey) =>
        journey
            .GroupBy(item => item.WorkId)
            .ToDictionary(group => group.Key, group => group.Max(item => item.ProgressPct));

    private static DiscoveryCardShape OverrideShapeForGroup(ContentGroupViewModel group, string pageKey)
    {
        var bucket = GetBucket(group.PrimaryMediaType);
        return pageKey switch
        {
            "listen" when bucket == DiscoveryBucket.Music => DiscoveryCardShape.Square,
            "watch" when bucket is DiscoveryBucket.Movie or DiscoveryBucket.Tv
                => HasWideArtwork(group.BackgroundUrl, group.BannerUrl) ? DiscoveryCardShape.Landscape : DiscoveryCardShape.Portrait,
            "home" when bucket is DiscoveryBucket.Movie or DiscoveryBucket.Tv
                => HasWideArtwork(group.BackgroundUrl, group.BannerUrl) ? DiscoveryCardShape.Landscape : DiscoveryCardShape.Portrait,
            _ => ShapeForBucket(bucket),
        };
    }

    private static DiscoveryCardShape ShapeForBucket(DiscoveryBucket bucket) =>
        ResolveShapeForArtwork(bucket, null, null, null, null, null, null);

    private static DiscoveryCardShape ResolveShapeForArtwork(
        DiscoveryBucket bucket,
        string? backgroundUrl,
        string? bannerUrl,
        string? coverAspectClass,
        string? squareAspectClass,
        string? backgroundAspectClass,
        string? bannerAspectClass) => bucket switch
    {
        DiscoveryBucket.Movie or DiscoveryBucket.Tv when HasWideArtwork(backgroundUrl, bannerUrl, backgroundAspectClass, bannerAspectClass)
            => DiscoveryCardShape.Landscape,
        DiscoveryBucket.Movie or DiscoveryBucket.Tv
            => DiscoveryCardShape.Portrait,
        DiscoveryBucket.Music
            => DiscoveryCardShape.Square,
        DiscoveryBucket.Audiobook when IsSquareArtwork(squareAspectClass) || IsSquareArtwork(coverAspectClass)
            => DiscoveryCardShape.Square,
        DiscoveryBucket.Audiobook
            => DiscoveryCardShape.Portrait,
        _ => DiscoveryCardShape.Portrait,
    };

    private static string AccentForBucket(DiscoveryBucket bucket) => bucket switch
    {
        DiscoveryBucket.Book => "#5DCAA5",
        DiscoveryBucket.Comic => "#FB923C",
        DiscoveryBucket.Audiobook => "#84CC16",
        DiscoveryBucket.Movie => "#60A5FA",
        DiscoveryBucket.Tv => "#38BDF8",
        DiscoveryBucket.Music => "#1ED760",
        _ => "#C9922E",
    };

    private static string ContinueLabel(DiscoveryBucket bucket) => bucket switch
    {
        DiscoveryBucket.Book or DiscoveryBucket.Comic => "Continue reading",
        DiscoveryBucket.Audiobook or DiscoveryBucket.Music => "Continue listening",
        DiscoveryBucket.Movie or DiscoveryBucket.Tv => "Continue watching",
        _ => "Continue",
    };

    private static Guid CreateDeterministicGuid(string value)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes);
    }

    private static string NormalizeDisplayKind(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return "Unknown";
        }

        return MediaTypeClassifier.GetDisplayLabel(mediaType) switch
        {
            "Books" => "Book",
            "Movies" => "Movie",
            "Audiobooks" => "Audiobook",
            _ => MediaTypeClassifier.GetDisplayLabel(mediaType),
        };
    }

    private static DiscoveryBucket GetBucket(string? mediaType)
    {
        var value = (mediaType ?? string.Empty).ToLowerInvariant();
        if (value.Contains("comic") || value.Contains("cbz") || value.Contains("cbr"))
        {
            return DiscoveryBucket.Comic;
        }

        if (value.Contains("audiobook") || value.Contains("m4b"))
        {
            return DiscoveryBucket.Audiobook;
        }

        if (value.Contains("music"))
        {
            return DiscoveryBucket.Music;
        }

        if (value.Contains("movie") || value.Contains("video"))
        {
            return DiscoveryBucket.Movie;
        }

        if (value.Contains("tv"))
        {
            return DiscoveryBucket.Tv;
        }

        if (value.Contains("book") || value.Contains("epub"))
        {
            return DiscoveryBucket.Book;
        }

        return DiscoveryBucket.Other;
    }

    private static bool IsReadBucket(DiscoveryBucket bucket) =>
        bucket is DiscoveryBucket.Book or DiscoveryBucket.Comic;

    private static bool IsWatchBucket(DiscoveryBucket bucket) =>
        bucket is DiscoveryBucket.Movie or DiscoveryBucket.Tv;

    private static bool IsListenBucket(DiscoveryBucket bucket) =>
        bucket is DiscoveryBucket.Music or DiscoveryBucket.Audiobook;

    private static bool IsReadKind(string? mediaKind) =>
        mediaKind is "Book" or "Comic";

    private static bool IsWatchKind(string? mediaKind) =>
        mediaKind is "Movie" or "TV";

    private static bool IsListenKind(string? mediaKind) =>
        mediaKind is "Music" or "Audiobook";

    private async Task<TasteProfile?> LoadActiveTasteProfileAsync(CancellationToken ct)
    {
        if (_api is null)
        {
            return null;
        }

        try
        {
            var activeProfile = (await _api.GetProfilesAsync(ct)).FirstOrDefault();
            return activeProfile is null
                ? null
                : await _api.GetTasteProfileAsync(activeProfile.Id, ct);
        }
        catch
        {
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> LoadCollectionPreviewImagesAsync(
        IEnumerable<ContentGroupViewModel> groups,
        CancellationToken ct)
    {
        var targetGroups = groups
            .Where(group => group.CollectionId != Guid.Empty)
            .DistinctBy(group => group.CollectionId)
            .ToList();

        if (targetGroups.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<string>>();
        }

        var previewTasks = targetGroups.Select(async group =>
        {
            var items = await _api.GetCollectionItemsAsync(group.CollectionId, limit: 4, ct: ct);
            var previewImages = items
                .Select(item => item.CoverUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .Cast<string>()
                .ToList();

            return new KeyValuePair<Guid, IReadOnlyList<string>>(group.CollectionId, previewImages);
        });

        var previews = await Task.WhenAll(previewTasks);
        return previews
            .Where(entry => entry.Value.Count > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Value);
    }

    private static IReadOnlyList<string> GetPreviewImages(
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? previewImages,
        Guid collectionId) =>
        previewImages is not null && previewImages.TryGetValue(collectionId, out var images)
            ? images
            : [];

    private static List<DiscoveryCardViewModel> TakeShelfItems(IEnumerable<DiscoveryCardViewModel> cards, int count) =>
        cards
            .Select((card, index) => new { Card = card, Index = index })
            .OrderByDescending(entry => HasArtwork(entry.Card))
            .ThenBy(entry => entry.Index)
            .Take(count)
            .Select(entry => entry.Card)
            .ToList();

    private static bool HasArtwork(DiscoveryCardViewModel card) =>
        !string.IsNullOrWhiteSpace(card.TileImageUrl)
        || !string.IsNullOrWhiteSpace(card.HoverImageUrl)
        || !string.IsNullOrWhiteSpace(card.HeroBackgroundImageUrl)
        || !string.IsNullOrWhiteSpace(card.PreviewImageUrl)
        || !string.IsNullOrWhiteSpace(card.CoverUrl)
        || !string.IsNullOrWhiteSpace(card.BackgroundUrl)
        || !string.IsNullOrWhiteSpace(card.BannerUrl)
        || !string.IsNullOrWhiteSpace(card.LogoUrl)
        || card.PreviewImages.Count > 0;

    private static string BuildGroupDescriptionSafe(ContentGroupViewModel group)
    {
        if (group.SeasonCount is > 1)
        {
            return $"{group.SeasonCount} seasons / {group.WorkCount} items";
        }

        if (group.AlbumCount is > 1)
        {
            return $"{group.AlbumCount} albums / {group.WorkCount} items";
        }

        return $"{group.WorkCount} items";
    }

    private static string JoinPartsSafe(params string?[] parts) =>
        string.Join(" / ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static string BuildGroupDescription(ContentGroupViewModel group)
    {
        if (group.SeasonCount is > 1)
        {
            return $"{group.SeasonCount} seasons · {group.WorkCount} items";
        }

        if (group.AlbumCount is > 1)
        {
            return $"{group.AlbumCount} albums · {group.WorkCount} items";
        }

        return $"{group.WorkCount} items";
    }

    private static string CountLabel(ContentGroupViewModel group)
    {
        if (group.SeasonCount is > 1)
        {
            return $"{group.SeasonCount} seasons";
        }

        if (group.AlbumCount is > 1)
        {
            return $"{group.AlbumCount} albums";
        }

        return $"{group.WorkCount} items";
    }

    private static DateTimeOffset GetSortTimestamp(WorkViewModel work) =>
        work.CanonicalValues
            .Select(value => value.LastScoredAt)
            .Append(work.CreatedAt)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();

    private static DateTimeOffset GetHomeSortTimestamp(WorkViewModel work)
    {
        var sortTimestamp = GetSortTimestamp(work);
        return sortTimestamp != DateTimeOffset.MinValue ? sortTimestamp : work.CreatedAt;
    }

    private static int ParseYear(string? year)
    {
        if (string.IsNullOrWhiteSpace(year))
        {
            return 0;
        }

        var digits = new string(year.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var parsed) ? parsed : 0;
    }

    private static string JoinParts(params string?[] parts) =>
        string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static string? TrimTo(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text.Length <= maxLength ? text : $"{text[..maxLength].TrimEnd()}...";
    }

    private enum DiscoveryBucket
    {
        Other,
        Book,
        Comic,
        Audiobook,
        Movie,
        Tv,
        Music,
    }

    private sealed record DiscoverySurfaceSelection(
        DiscoverySurfaceKind SurfaceKind,
        DiscoveryHoverLayout HoverLayout,
        string? TileImageUrl,
        string? HoverImageUrl,
        string? HeroBackgroundImageUrl,
        string? PreviewImageUrl,
        DiscoveryImageFitMode TileImageFitMode,
        DiscoveryImageFitMode HoverImageFitMode);

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
