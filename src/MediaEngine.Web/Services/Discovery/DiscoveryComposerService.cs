using MediaEngine.Domain.Services;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;

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

        await Task.WhenAll(worksTask, journeyTask, groupsTask);

        var works = await worksTask;
        var journey = await journeyTask;
        var groups = await groupsTask;
        var previewImages = await LoadCollectionPreviewImagesAsync(
            groups
                .OrderByDescending(group => group.WorkCount)
                .ThenByDescending(group => group.CreatedAt)
                .Where(group => string.IsNullOrWhiteSpace(group.CoverUrl) && string.IsNullOrWhiteSpace(group.ArtistPhotoUrl))
                .Take(12),
            ct);

        return ComposeHome(works, journey, groups, previewImages);
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
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? groupPreviewImages = null)
    {
        var orderedWorks = works.OrderByDescending(GetSortTimestamp).ThenByDescending(work => ParseYear(work.Year)).ToList();
        var progressLookup = BuildProgressLookup(journey);
        var catalog = orderedWorks.Select(work => ToWorkCard(work, progressLookup.GetValueOrDefault(work.Id))).ToList();

        var continueCards = journey
            .OrderByDescending(item => item.LastAccessed)
            .Select(ToJourneyCard)
            .ToList();

        var collectionCards = groups
            .OrderByDescending(group => group.WorkCount)
            .ThenByDescending(group => group.CreatedAt)
            .Select(group => ToCollectionCard(group, OverrideShapeForGroup(group, "home"), GetPreviewImages(groupPreviewImages, group.CollectionId)))
            .ToList();

        var shelves = new List<DiscoveryShelfViewModel>();

        if (continueCards.Count > 0)
        {
            shelves.Add(new DiscoveryShelfViewModel
            {
                Title = "Continue where you left off",
                Subtitle = "Personalized from your recent activity",
                Items = TakeShelfItems(continueCards, 12),
            });
        }

        if (collectionCards.Count > 0)
        {
            shelves.Add(new DiscoveryShelfViewModel
            {
                Title = "Collections built from your library",
                Subtitle = "Dynamic series, shows, and albums surfaced automatically",
                Items = TakeShelfItems(collectionCards, 10),
                SeeAllRoute = "/collections",
            });
        }

        foreach (var shelf in BuildAffinityShelves(
                     catalog.Where(card => IsReadKind(card.MediaKind)).ToList(),
                     catalog.Where(card => IsWatchKind(card.MediaKind)).ToList(),
                     catalog.Where(card => IsListenKind(card.MediaKind)).ToList(),
                     journey))
        {
            shelves.Add(shelf);
        }

        if (catalog.Count > 0)
        {
            shelves.Add(new DiscoveryShelfViewModel
            {
                Title = "Recently added",
                Subtitle = "Fresh arrivals across the whole library",
                Items = TakeShelfItems(catalog, 14),
            });
        }

        return new DiscoveryPageViewModel
        {
            Key = "home",
            AccentColor = "#1CE783",
            Hero = BuildHero(
                journey: journey,
                works: orderedWorks,
                groups: groups,
                groupPreviewImages: groupPreviewImages,
                accentColor: "#1CE783",
                journeyEyebrow: "Continue with your library",
                workEyebrow: "Recently added for you",
                groupEyebrow: "Featured collection"),
            Hubs = groups
                .OrderByDescending(group => group.WorkCount)
                .ThenByDescending(group => group.CreatedAt)
                .Take(8)
                .Select(group => ToHub(group, GetPreviewImages(groupPreviewImages, group.CollectionId)))
                .ToList(),
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
                Items = TakeShelfItems(readJourney.Select(ToJourneyCard), 10),
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
                Items = TakeShelfItems(listenJourney.Select(ToJourneyCard), 10),
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
        var activeJourney = journey.OrderByDescending(item => item.LastAccessed).FirstOrDefault();
        if (activeJourney is not null)
        {
            return new DiscoveryHeroViewModel
            {
                Eyebrow = journeyEyebrow,
                Title = activeJourney.Title,
                Subtitle = activeJourney.CollectionDisplayName ?? activeJourney.Author,
                Description = TrimTo(activeJourney.Description, 240),
                BackgroundImageUrl = activeJourney.HeroUrl,
                PreviewImageUrl = activeJourney.CoverUrl,
                AccentColor = accentColor,
                MetaText = JoinPartsSafe(
                    NormalizeDisplayKind(activeJourney.MediaType),
                    activeJourney.Series,
                    activeJourney.ProgressDisplay),
                ProgressPct = activeJourney.ProgressPct,
                PrimaryActionLabel = activeJourney.ActionVerb,
                PrimaryNavigationUrl = $"/book/{activeJourney.WorkId}",
                SecondaryActionLabel = "Details",
                SecondaryNavigationUrl = activeJourney.CollectionId.HasValue
                    ? $"/collection/{activeJourney.CollectionId.Value}"
                    : $"/book/{activeJourney.WorkId}",
            };
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
            return new DiscoveryHeroViewModel
            {
                Eyebrow = groupEyebrow,
                Title = featuredGroup.DisplayName,
                Subtitle = featuredGroup.Creator ?? featuredGroup.Network,
                Description = BuildGroupDescriptionSafe(featuredGroup),
                BackgroundImageUrl = featuredGroup.ArtistPhotoUrl ?? featuredGroup.CoverUrl,
                PreviewImageUrl = featuredGroup.CoverUrl ?? featuredGroup.ArtistPhotoUrl ?? previewImages.FirstOrDefault(),
                AccentColor = accentColor,
                MetaText = JoinPartsSafe(featuredGroup.PrimaryMediaType, featuredGroup.Year, featuredGroup.WorkCount.ToString()),
                PrimaryActionLabel = "Explore collection",
                PrimaryNavigationUrl = $"/collection/{featuredGroup.CollectionId}",
                SecondaryActionLabel = "Details",
                SecondaryNavigationUrl = $"/collection/{featuredGroup.CollectionId}",
            };
        }

        return null;
    }

    private static DiscoveryHeroViewModel ToHeroFromWork(
        WorkViewModel work,
        string accentColor,
        string eyebrow,
        double? progressPct = null)
    {
        var bucket = GetBucket(work.MediaType);
        return new DiscoveryHeroViewModel
        {
            Eyebrow = eyebrow,
            Title = work.Title,
            Subtitle = work.Author,
            Description = TrimTo(work.Description, 240),
            BackgroundImageUrl = work.HeroUrl,
            PreviewImageUrl = work.CoverUrl,
            AccentColor = accentColor,
            MetaText = JoinPartsSafe(
                NormalizeDisplayKind(work.MediaType),
                work.Year,
                work.Genres.FirstOrDefault()),
            ProgressPct = progressPct,
            PrimaryActionLabel = progressPct is > 0 ? ContinueLabel(bucket) : "Open",
            PrimaryNavigationUrl = $"/book/{work.Id}",
            SecondaryActionLabel = "Details",
            SecondaryNavigationUrl = work.CollectionId.HasValue
                ? $"/collection/{work.CollectionId.Value}"
                : $"/book/{work.Id}",
        };
    }

    private static DiscoveryCardViewModel ToJourneyCard(JourneyItemViewModel item)
    {
        var bucket = GetBucket(item.MediaType);
        return new DiscoveryCardViewModel
        {
            Id = item.WorkId,
            WorkId = item.WorkId,
            CollectionId = item.CollectionId,
            Title = item.Title,
            Subtitle = item.Author,
            Description = TrimTo(item.Description, 150),
            CoverUrl = item.CoverUrl,
            BackdropUrl = item.HeroUrl,
            MetaText = JoinPartsSafe(
                NormalizeDisplayKind(item.MediaType),
                item.Series,
                item.ProgressDisplay),
            MediaKind = NormalizeDisplayKind(item.MediaType),
            AccentColor = AccentForBucket(bucket),
            Shape = ShapeForBucket(bucket),
            NavigationUrl = $"/book/{item.WorkId}",
            DetailsNavigationUrl = item.CollectionId.HasValue
                ? $"/collection/{item.CollectionId.Value}"
                : $"/book/{item.WorkId}",
            PrimaryActionLabel = item.ActionVerb,
            ProgressPct = item.ProgressPct,
            Creator = item.Author,
            CollectionKey = item.Series,
            SortYear = 0,
            SortTimestamp = item.LastAccessed,
        };
    }

    private static DiscoveryCardViewModel ToWorkCard(WorkViewModel work, double? progressPct = null)
    {
        var bucket = GetBucket(work.MediaType);
        return new DiscoveryCardViewModel
        {
            Id = work.Id,
            WorkId = work.Id,
            CollectionId = work.CollectionId,
            Title = work.Title,
            Subtitle = work.Author,
            Description = TrimTo(work.Description, 150),
            CoverUrl = work.CoverUrl,
            BackdropUrl = work.HeroUrl,
            MetaText = JoinPartsSafe(
                NormalizeDisplayKind(work.MediaType),
                work.Year,
                work.Genres.FirstOrDefault()),
            MediaKind = NormalizeDisplayKind(work.MediaType),
            AccentColor = AccentForBucket(bucket),
            Shape = ShapeForBucket(bucket),
            NavigationUrl = $"/book/{work.Id}",
            DetailsNavigationUrl = work.CollectionId.HasValue
                ? $"/collection/{work.CollectionId.Value}"
                : $"/book/{work.Id}",
            PrimaryActionLabel = progressPct is > 0 ? ContinueLabel(bucket) : "Open",
            ProgressPct = progressPct,
            Creator = work.Author,
            CollectionKey = work.Series,
            Genres = work.Genres,
            SortYear = ParseYear(work.Year),
            SortTimestamp = GetSortTimestamp(work),
        };
    }

    private static DiscoveryCardViewModel ToCollectionCard(
        ContentGroupViewModel group,
        DiscoveryCardShape shape,
        IReadOnlyList<string>? previewImages = null)
    {
        var bucket = GetBucket(group.PrimaryMediaType);
        return new DiscoveryCardViewModel
        {
            Id = group.CollectionId,
            CollectionId = group.CollectionId,
            Title = group.DisplayName,
            Subtitle = group.Creator ?? group.Network,
            Description = BuildGroupDescriptionSafe(group),
            CoverUrl = group.CoverUrl ?? group.ArtistPhotoUrl,
            BackdropUrl = group.CoverUrl ?? group.ArtistPhotoUrl,
            PreviewImages = previewImages ?? [],
            MetaText = JoinPartsSafe(group.PrimaryMediaType, group.Year, CountLabel(group)),
            MediaKind = NormalizeDisplayKind(group.PrimaryMediaType),
            AccentColor = !string.IsNullOrWhiteSpace(group.MediaTypeColor) ? group.MediaTypeColor : AccentForBucket(bucket),
            Shape = shape,
            NavigationUrl = $"/collection/{group.CollectionId}",
            DetailsNavigationUrl = $"/collection/{group.CollectionId}",
            PrimaryActionLabel = "Explore",
            Creator = group.Creator,
            CollectionKey = group.DisplayName,
            SortYear = ParseYear(group.Year),
            SortTimestamp = group.CreatedAt,
            IsCollection = true,
        };
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
        NavigationUrl = $"/collection/{group.CollectionId}",
    };

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
            "watch" => DiscoveryCardShape.Landscape,
            "home" when bucket is DiscoveryBucket.Movie or DiscoveryBucket.Tv => DiscoveryCardShape.Landscape,
            _ => ShapeForBucket(bucket),
        };
    }

    private static DiscoveryCardShape ShapeForBucket(DiscoveryBucket bucket) => bucket switch
    {
        DiscoveryBucket.Movie or DiscoveryBucket.Tv => DiscoveryCardShape.Landscape,
        DiscoveryBucket.Music => DiscoveryCardShape.Square,
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
        !string.IsNullOrWhiteSpace(card.CoverUrl)
        || !string.IsNullOrWhiteSpace(card.BackdropUrl)
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
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();

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
}
