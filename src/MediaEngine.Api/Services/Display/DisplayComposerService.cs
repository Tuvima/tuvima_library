using MediaEngine.Contracts.Display;

namespace MediaEngine.Api.Services.Display;

public sealed class DisplayComposerService
{
    private readonly IDisplayProjectionRepository _repository;
    private readonly DisplayCardBuilder _cards;

    public DisplayComposerService(IDisplayProjectionRepository repository, DisplayCardBuilder cards)
    {
        _repository = repository;
        _cards = cards;
    }

    public async Task<DisplayPageDto> BuildHomeAsync(bool includeCatalog = true, CancellationToken ct = default, int shelfLimit = 18)
    {
        var works = await _repository.LoadWorksAsync(ct);
        var journey = await _repository.LoadJourneyAsync(null, ct);
        var progressByWork = LatestProgressByWork(journey);

        var continueCards = journey
            .OrderByDescending(item => item.LastAccessed)
            .Take(Math.Max(1, shelfLimit))
            .Select(item => _cards.FromJourney(item, "home"))
            .ToList();

        var freshCards = works
            .OrderByDescending(work => work.CreatedAt)
            .Take(Math.Max(1, shelfLimit))
            .Select(work => _cards.FromWork(work, "home", progressByWork.GetValueOrDefault(work.WorkId)))
            .ToList();

        var readCards = works
            .Where(work => DisplayMediaRules.IsReadKind(work.MediaType))
            .OrderByDescending(work => progressByWork.TryGetValue(work.WorkId, out var p) ? p.LastAccessed : work.CreatedAt)
            .Take(Math.Max(1, shelfLimit))
            .Select(work => _cards.FromWork(work, "home", progressByWork.GetValueOrDefault(work.WorkId)))
            .ToList();

        var watchCards = works
            .Where(work => DisplayMediaRules.IsWatchKind(work.MediaType))
            .OrderByDescending(work => progressByWork.TryGetValue(work.WorkId, out var p) ? p.LastAccessed : work.CreatedAt)
            .Take(Math.Max(1, shelfLimit))
            .Select(work => _cards.FromWork(work, "home", progressByWork.GetValueOrDefault(work.WorkId)))
            .ToList();

        var listenCards = works
            .Where(work => DisplayMediaRules.IsListenKind(work.MediaType))
            .OrderByDescending(work => progressByWork.TryGetValue(work.WorkId, out var p) ? p.LastAccessed : work.CreatedAt)
            .Take(Math.Max(1, shelfLimit))
            .Select(work => _cards.FromWork(work, "home", progressByWork.GetValueOrDefault(work.WorkId)))
            .ToList();

        var shelves = new List<DisplayShelfDto>();
        AddShelf(shelves, "continue", "Continue", "Pick up where you left off", continueCards, null);
        AddShelf(shelves, "fresh", "Fresh in your library", "Recently added across every media type", freshCards, null);
        AddShelf(shelves, "watch-next", "Watch next", "Movies and shows ready to play", watchCards, "/watch");
        AddShelf(shelves, "read-next", "Read next", "Books and comics ready to open", readCards, "/read");
        AddShelf(shelves, "listen-next", "Listen next", "Music and audiobooks ready to resume", listenCards, "/listen");

        var heroCard = continueCards.FirstOrDefault() ?? freshCards.FirstOrDefault();

        return new DisplayPageDto(
            Key: "home",
            Title: "Home",
            Subtitle: "A cross-media view of your local library",
            Hero: heroCard is null ? null : DisplayCardBuilder.ToHero(heroCard, continueCards.Count > 0 ? "Continue with your library" : "Fresh in your library"),
            Shelves: shelves,
            Catalog: includeCatalog
                ? works.Select(work => _cards.FromWork(work, "home", progressByWork.GetValueOrDefault(work.WorkId))).ToList()
                : []);
    }

    public async Task<DisplayPageDto> BuildBrowseAsync(
        string? lane,
        string? mediaType,
        string? grouping,
        string? search,
        int offset,
        int limit,
        bool includeCatalog = true,
        Guid? profileId = null,
        CancellationToken ct = default)
    {
        var normalizedLane = DisplayMediaRules.NormalizeLane(lane);
        if (normalizedLane is not null &&
            string.IsNullOrWhiteSpace(mediaType) &&
            string.IsNullOrWhiteSpace(search) &&
            (string.IsNullOrWhiteSpace(grouping) || string.Equals(grouping, "all", StringComparison.OrdinalIgnoreCase)) &&
            offset <= 0)
        {
            return await BuildLaneAsync(normalizedLane, includeCatalog, ct);
        }

        if (string.Equals(normalizedLane, "listen", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(DisplayMediaRules.NormalizeMediaType(mediaType ?? string.Empty), "Music", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(grouping, "home", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(search) &&
            offset <= 0)
        {
            return await BuildMusicHomeAsync(includeCatalog, profileId, ct);
        }

        var works = await _repository.LoadWorksAsync(ct);
        var journey = await _repository.LoadJourneyAsync(null, ct);
        var progressByWork = LatestProgressByWork(journey);

        var filtered = works.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            filtered = filtered.Where(work => string.Equals(DisplayMediaRules.NormalizeMediaType(work.MediaType), DisplayMediaRules.NormalizeMediaType(mediaType), StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            filtered = normalizedLane switch
            {
                "watch" => filtered.Where(work => DisplayMediaRules.IsWatchKind(work.MediaType)),
                "read" => filtered.Where(work => DisplayMediaRules.IsReadKind(work.MediaType)),
                "listen" => filtered.Where(work => DisplayMediaRules.IsListenKind(work.MediaType)),
                _ => filtered,
            };
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(work =>
                Contains(work.Title, search) ||
                Contains(work.Author, search) ||
                Contains(work.Series, search) ||
                Contains(work.Genre, search) ||
                Contains(work.Album, search) ||
                Contains(work.Artist, search));
        }

        var context = normalizedLane ?? "browse";
        var cards = filtered
            .OrderByDescending(work => work.CreatedAt)
            .Skip(Math.Max(0, offset))
            .Take(Math.Clamp(limit <= 0 ? 48 : limit, 1, 200))
            .Select(work => _cards.FromWork(work, context, progressByWork.GetValueOrDefault(work.WorkId)))
            .ToList();

        return new DisplayPageDto(
            Key: $"browse-{normalizedLane ?? "all"}-{grouping ?? "all"}",
            Title: TitleForLane(lane),
            Subtitle: null,
            Hero: cards.FirstOrDefault() is { } hero ? DisplayCardBuilder.ToHero(hero, "From your library") : null,
            Shelves: [new DisplayShelfDto("results", "Results", null, cards, null)],
            Catalog: includeCatalog ? cards : []);
    }

    public async Task<DisplayPageDto> BuildContinueAsync(string? lane, int limit, bool includeCatalog = true, CancellationToken ct = default)
    {
        var normalizedLane = DisplayMediaRules.NormalizeLane(lane);
        var journey = await _repository.LoadJourneyAsync(normalizedLane, ct);
        var cards = journey
            .OrderByDescending(item => item.LastAccessed)
            .Take(Math.Clamp(limit <= 0 ? 24 : limit, 1, 100))
            .Select(item => _cards.FromJourney(item, normalizedLane ?? "continue"))
            .ToList();

        return new DisplayPageDto(
            Key: $"continue-{normalizedLane ?? "all"}",
            Title: "Continue",
            Subtitle: null,
            Hero: cards.FirstOrDefault() is { } hero ? DisplayCardBuilder.ToHero(hero, "Continue") : null,
            Shelves: [new DisplayShelfDto("continue", "Continue", null, cards, null)],
            Catalog: includeCatalog ? cards : []);
    }

    public Task<DisplayPageDto> BuildSearchAsync(string? query, int limit, bool includeCatalog = true, CancellationToken ct = default) =>
        BuildBrowseAsync(null, null, "all", query, 0, limit <= 0 ? 48 : limit, includeCatalog, null, ct);

    public async Task<DisplayShelfPageDto?> BuildShelfPageAsync(
        string shelfKey,
        string? lane,
        string? mediaType,
        string? grouping,
        string? search,
        string? cursor,
        int? offset,
        int limit,
        Guid? profileId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(shelfKey))
        {
            return null;
        }

        var start = ResolveShelfOffset(cursor, offset);
        var take = Math.Clamp(limit <= 0 ? 24 : limit, 1, 100);
        var sourceLimit = start + take + 1;
        var page = string.IsNullOrWhiteSpace(lane)
                   && string.IsNullOrWhiteSpace(mediaType)
                   && string.IsNullOrWhiteSpace(grouping)
                   && string.IsNullOrWhiteSpace(search)
            ? await BuildHomeAsync(includeCatalog: false, ct: ct, shelfLimit: sourceLimit)
            : await BuildShelfSourcePageAsync(lane, mediaType, grouping, search, sourceLimit, profileId, ct);

        var shelf = page.Shelves.FirstOrDefault(item => string.Equals(item.Key, shelfKey, StringComparison.OrdinalIgnoreCase));
        if (shelf is null)
        {
            return null;
        }

        var items = shelf.Items;
        var pagedItems = items.Skip(start).Take(take).ToList();
        var nextOffset = start + pagedItems.Count;
        var hasMore = nextOffset < items.Count;
        var nextCursor = hasMore ? nextOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

        return new DisplayShelfPageDto(
            shelf with { Items = pagedItems },
            nextCursor,
            start,
            take,
            hasMore);
    }

    public async Task<DisplayPageDto?> BuildGroupAsync(Guid groupId, bool includeCatalog = true, CancellationToken ct = default)
    {
        var works = (await _repository.LoadWorksAsync(ct))
            .Where(work => work.CollectionId == groupId)
            .OrderBy(work => DisplayMediaRules.ParseDouble(work.SeriesPosition) ?? double.MaxValue)
            .ThenBy(work => work.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (works.Count == 0)
        {
            return null;
        }

        var cards = works.Select(work => _cards.FromWork(work, "collection", null)).ToList();
        var title = works.FirstOrDefault(work => !string.IsNullOrWhiteSpace(work.Series))?.Series ?? "Collection";
        return new DisplayPageDto(
            Key: $"group-{groupId:N}",
            Title: title,
            Subtitle: $"{works.Count} items",
            Hero: cards.FirstOrDefault() is { } hero ? DisplayCardBuilder.ToHero(hero, "Collection") : null,
            Shelves: [new DisplayShelfDto("items", title, $"{works.Count} items", cards, null)],
            Catalog: includeCatalog ? cards : []);
    }

    private Task<DisplayPageDto> BuildShelfSourcePageAsync(
        string? lane,
        string? mediaType,
        string? grouping,
        string? search,
        int shelfLimit,
        Guid? profileId,
        CancellationToken ct)
    {
        var normalizedLane = DisplayMediaRules.NormalizeLane(lane);
        if (string.Equals(normalizedLane, "listen", StringComparison.OrdinalIgnoreCase)
            && string.Equals(DisplayMediaRules.NormalizeMediaType(mediaType ?? string.Empty), "Music", StringComparison.OrdinalIgnoreCase)
            && string.Equals(grouping, "home", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(search))
        {
            return BuildMusicHomeAsync(includeCatalog: false, profileId, ct, shelfLimit);
        }

        if (normalizedLane is not null
            && string.IsNullOrWhiteSpace(mediaType)
            && string.IsNullOrWhiteSpace(search)
            && (string.IsNullOrWhiteSpace(grouping) || string.Equals(grouping, "all", StringComparison.OrdinalIgnoreCase)))
        {
            return BuildLaneAsync(normalizedLane, includeCatalog: false, ct, shelfLimit);
        }

        return BuildBrowseAsync(lane, mediaType, grouping, search, 0, Math.Max(1, shelfLimit), includeCatalog: false, profileId, ct);
    }

    private async Task<DisplayPageDto> BuildLaneAsync(string lane, bool includeCatalog, CancellationToken ct, int shelfLimit = 18)
    {
        var works = await _repository.LoadWorksAsync(ct);
        var journey = await _repository.LoadJourneyAsync(lane, ct);
        var progressByWork = LatestProgressByWork(journey);

        var laneWorks = works
            .Where(work => lane switch
            {
                "watch" => DisplayMediaRules.IsWatchKind(work.MediaType),
                "read" => DisplayMediaRules.IsReadKind(work.MediaType),
                "listen" => DisplayMediaRules.IsListenKind(work.MediaType),
                _ => true,
            })
            .OrderByDescending(work => work.CreatedAt)
            .ThenByDescending(work => DisplayMediaRules.ParseDouble(work.Year) ?? 0)
            .ToList();

        var laneJourney = journey
            .OrderByDescending(item => item.LastAccessed)
            .ToList();

        var catalog = laneWorks
            .Select(work => _cards.FromWork(work, lane, progressByWork.GetValueOrDefault(work.WorkId)))
            .ToList();

        var shelves = lane switch
        {
            "watch" => BuildWatchShelves(laneWorks, laneJourney, progressByWork, shelfLimit),
            "read" => BuildReadShelves(laneWorks, laneJourney, progressByWork, shelfLimit),
            "listen" => BuildListenShelves(laneWorks, laneJourney, progressByWork, shelfLimit),
            _ => [new DisplayShelfDto("results", "Results", null, catalog, null)],
        };

        var heroCard = laneJourney.FirstOrDefault() is { } journeyHero
            ? _cards.FromJourney(journeyHero, lane)
            : catalog.FirstOrDefault();

        return new DisplayPageDto(
            Key: lane,
            Title: TitleForLane(lane),
            Subtitle: SubtitleForLane(lane),
            Hero: heroCard is null ? null : DisplayCardBuilder.ToHero(heroCard, EyebrowForLane(lane, laneJourney.Count > 0)),
            Shelves: shelves,
            Catalog: includeCatalog ? catalog : []);
    }

    private async Task<DisplayPageDto> BuildMusicHomeAsync(bool includeCatalog, Guid? profileId, CancellationToken ct, int shelfLimit = 18)
    {
        var works = (await _repository.LoadWorksAsync(ct))
            .Where(work => DisplayMediaRules.NormalizeDisplayKind(work.MediaType) == "Music")
            .OrderByDescending(work => work.CreatedAt)
            .ThenBy(work => work.Artist, StringComparer.OrdinalIgnoreCase)
            .ThenBy(work => work.Album, StringComparer.OrdinalIgnoreCase)
            .ThenBy(work => DisplayMediaRules.ParseDouble(work.TrackNumber) ?? double.MaxValue)
            .ThenBy(work => work.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var journey = (await _repository.LoadJourneyAsync("listen", ct))
            .Where(item => DisplayMediaRules.NormalizeDisplayKind(item.MediaType) == "Music")
            .OrderByDescending(item => item.LastAccessed)
            .ToList();
        var favoriteWorkIds = await _repository.LoadFavoriteWorkIdsAsync(profileId, ct);
        var progressByWork = LatestProgressByWork(journey);
        var catalog = works
            .Select(work => MarkFavorite(_cards.FromWork(work, "listen", progressByWork.GetValueOrDefault(work.WorkId)), favoriteWorkIds))
            .ToList();

        var shelves = new List<DisplayShelfDto>();
        AddShelf(shelves, "recently-played", "Recently Played", "Pick up albums and tracks from your latest listening sessions",
            journey.Take(Math.Max(1, shelfLimit)).Select(item => _cards.FromJourney(item, "listen")).ToList(), "/listen/music/songs");
        AddShelf(shelves, "favorite-songs", "Favorite Songs", "Tracks you marked as favorites",
            catalog.Where(card => card.WorkId.HasValue && favoriteWorkIds.Contains(card.WorkId.Value)).Take(Math.Max(1, shelfLimit)).ToList(), "/listen/music/playlists/system/favorite-songs");
        AddShelf(shelves, "recently-added", "Recently Added", "Fresh arrivals from your music library",
            works.Take(Math.Max(1, shelfLimit)).Select(work => MarkFavorite(_cards.FromWork(work, "listen", progressByWork.GetValueOrDefault(work.WorkId)), favoriteWorkIds)).ToList(), "/listen/music/playlists/system/recently-added");
        AddShelf(shelves, "albums", "Albums", "Album-first browsing with cover art at the center",
            BuildMusicAlbumCards(works).Take(Math.Max(1, shelfLimit)).ToList(), "/listen/music/albums");
        AddShelf(shelves, "artists", "Artists", "Artist-led listening built from your library",
            BuildMusicArtistCards(works).Take(Math.Max(1, shelfLimit)).ToList(), "/listen/music/artists");

        var heroCard = journey.FirstOrDefault() is { } journeyHero
            ? _cards.FromJourney(journeyHero, "listen")
            : BuildMusicAlbumCards(works).FirstOrDefault() ?? catalog.FirstOrDefault();

        return new DisplayPageDto(
            Key: "listen-music",
            Title: "Music",
            Subtitle: "Albums, artists, songs, and playlists from your local library",
            Hero: heroCard is null ? null : DisplayCardBuilder.ToHero(heroCard, journey.Count > 0 ? "Recently Played" : "Featured album"),
            Shelves: shelves,
            Catalog: includeCatalog ? catalog : []);
    }

    private IReadOnlyList<DisplayCardDto> BuildMusicAlbumCards(IReadOnlyList<DisplayWorkRow> works) =>
        works
            .Where(work => !string.IsNullOrWhiteSpace(work.Album))
            .GroupBy(work => AlbumGroupKey(work), StringComparer.OrdinalIgnoreCase)
            .Select(group => ToMusicAlbumCard(group.ToList()))
            .Where(card => card is not null)
            .Cast<DisplayCardDto>()
            .OrderByDescending(card => card.SortTimestamp)
            .ToList();

    private IReadOnlyList<DisplayCardDto> BuildMusicArtistCards(IReadOnlyList<DisplayWorkRow> works) =>
        works
            .Select(work => new { Artist = FirstNonBlank(work.Artist, work.Author), Work = work })
            .Where(item => !string.IsNullOrWhiteSpace(item.Artist))
            .GroupBy(item => item.Artist!, StringComparer.OrdinalIgnoreCase)
            .Select(group => ToMusicArtistCard(group.Key, group.Select(item => item.Work).ToList()))
            .OrderByDescending(card => card.SortTimestamp)
            .ToList();

    private static DisplayCardDto MarkFavorite(DisplayCardDto card, IReadOnlySet<Guid> favoriteWorkIds)
    {
        if (!card.WorkId.HasValue || !favoriteWorkIds.Contains(card.WorkId.Value))
        {
            return card;
        }

        return card with { Flags = card.Flags with { IsFavorite = true } };
    }

    private static DisplayCardDto? ToMusicAlbumCard(IReadOnlyList<DisplayWorkRow> works)
    {
        if (works.Count == 0)
        {
            return null;
        }

        var representative = works
            .OrderByDescending(work => !string.IsNullOrWhiteSpace(work.SquareUrl) || !string.IsNullOrWhiteSpace(work.CoverUrl))
            .ThenByDescending(work => work.CreatedAt)
            .First();
        var title = representative.Album ?? representative.Title;
        var artist = FirstNonBlank(representative.Artist, representative.Author);
        var route = representative.CollectionId.HasValue
            ? $"/listen/music/albums/{representative.CollectionId.Value:D}"
            : $"/listen/music/albums/by-name/{Uri.EscapeDataString(title)}{(string.IsNullOrWhiteSpace(artist) ? string.Empty : $"?artist={Uri.EscapeDataString(artist)}")}";
        var action = new DisplayActionDto("openCollection", "Browse album", representative.WorkId, representative.AssetId, representative.CollectionId, route);

        return new DisplayCardDto(
            Id: representative.CollectionId ?? representative.WorkId,
            WorkId: representative.WorkId,
            AssetId: representative.AssetId,
            CollectionId: representative.CollectionId,
            MediaType: "Music",
            GroupingType: "album",
            Title: title,
            Subtitle: artist,
            Facts: AlbumFacts(works, artist, representative.Year, representative.Genre),
            Artwork: ArtworkFor(representative),
            PreferredShape: "square",
            Presentation: "album",
            TileTextMode: "caption",
            PreviewPlacement: "smart",
            Progress: null,
            Actions: [action],
            Flags: new DisplayCardFlagsDto(true, false, false, true, false),
            SortTimestamp: works.Max(work => work.CreatedAt));
    }

    private static DisplayCardDto ToMusicArtistCard(string artist, IReadOnlyList<DisplayWorkRow> works)
    {
        var representative = works
            .OrderByDescending(work => !string.IsNullOrWhiteSpace(work.SquareUrl) || !string.IsNullOrWhiteSpace(work.CoverUrl))
            .ThenByDescending(work => work.CreatedAt)
            .First();
        var albumCount = works
            .Select(work => work.Album)
            .Where(album => !string.IsNullOrWhiteSpace(album))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var action = new DisplayActionDto("openCollection", "Browse artist", representative.WorkId, representative.AssetId, representative.CollectionId, $"/listen/music/artists/{Uri.EscapeDataString(artist)}");

        return new DisplayCardDto(
            Id: representative.CollectionId ?? representative.WorkId,
            WorkId: representative.WorkId,
            AssetId: representative.AssetId,
            CollectionId: representative.CollectionId,
            MediaType: "Music",
            GroupingType: "artist",
            Title: artist,
            Subtitle: $"{works.Count} tracks",
            Facts: ArtistFacts(albumCount, works.Count, representative.Genre),
            Artwork: ArtworkFor(representative),
            PreferredShape: "square",
            Presentation: "artist",
            TileTextMode: "caption",
            PreviewPlacement: "smart",
            Progress: null,
            Actions: [action],
            Flags: new DisplayCardFlagsDto(true, false, false, true, false),
            SortTimestamp: works.Max(work => work.CreatedAt));
    }

    private IReadOnlyList<DisplayShelfDto> BuildReadShelves(
        IReadOnlyList<DisplayWorkRow> works,
        IReadOnlyList<DisplayJourneyRow> journey,
        IReadOnlyDictionary<Guid, DisplayJourneyRow> progressByWork,
        int shelfLimit)
    {
        var take = Math.Max(1, shelfLimit);
        var shelves = new List<DisplayShelfDto>();
        AddShelf(shelves, "continue-reading", "Continue reading", "Books, comics, and audiobooks already in motion",
            journey.Take(take).Select(item => _cards.FromJourney(item, "read")).ToList(), null);
        AddShelf(shelves, "reading-collections", "Collections to explore", "Series and grouped reading pulled from your library",
            _cards.BuildCollectionCards(works, "read").Take(take).ToList(), "/collections");
        AddShelf(shelves, "recently-added", "Recently added to read", "Fresh pages ready to pick up",
            works.Take(take).Select(work => _cards.FromWork(work, "read", progressByWork.GetValueOrDefault(work.WorkId))).ToList(), null);

        var authorShelves = works
            .Where(work => !string.IsNullOrWhiteSpace(work.Author))
            .GroupBy(work => work.Author!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= 2)
            .OrderByDescending(group => group.Count())
            .Take(3)
            .Select(group => new DisplayShelfDto(
                Key: $"author-{DisplayMediaRules.StableKey(group.Key)}",
                Title: group.Key,
                Subtitle: $"{group.Count()} titles",
                Items: group.Take(12).Select(work => _cards.FromWork(work, "read", progressByWork.GetValueOrDefault(work.WorkId))).ToList(),
                SeeAllRoute: null));

        shelves.AddRange(authorShelves);
        return shelves;
    }

    private IReadOnlyList<DisplayShelfDto> BuildWatchShelves(
        IReadOnlyList<DisplayWorkRow> works,
        IReadOnlyList<DisplayJourneyRow> journey,
        IReadOnlyDictionary<Guid, DisplayJourneyRow> progressByWork,
        int shelfLimit)
    {
        var take = Math.Max(1, shelfLimit);
        var shelves = new List<DisplayShelfDto>();
        AddShelf(shelves, "continue-watching", "Continue watching", "Movies and shows already in progress",
            journey.Take(take).Select(item => _cards.FromJourney(item, "watch")).ToList(), null);
        AddShelf(shelves, "watch-collections", "Collections to watch", "Shows, series, and grouped franchises from your library",
            _cards.BuildCollectionCards(works, "watch").Take(take).ToList(), "/collections");
        AddShelf(shelves, "movies", "Movies in your library", "Feature films ready to play",
            works.Where(work => DisplayMediaRules.NormalizeDisplayKind(work.MediaType) == "Movie").Take(take).Select(work => _cards.FromWork(work, "watch", progressByWork.GetValueOrDefault(work.WorkId))).ToList(), null);
        AddShelf(shelves, "tv", "TV in your library", "Shows and episodes ready to continue",
            works.Where(work => DisplayMediaRules.NormalizeDisplayKind(work.MediaType) == "TV").Take(take).Select(work => _cards.FromWork(work, "watch", progressByWork.GetValueOrDefault(work.WorkId))).ToList(), null);

        var genreShelves = works
            .SelectMany(work => DisplayMediaRules.SplitValues(work.Genre).Select(genre => (Genre: genre, Work: work)))
            .GroupBy(item => item.Genre, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= 3)
            .OrderByDescending(group => group.Count())
            .Take(3)
            .Select(group => new DisplayShelfDto(
                Key: $"genre-{DisplayMediaRules.StableKey(group.Key)}",
                Title: group.Key,
                Subtitle: $"{group.Count()} titles",
                Items: group.Take(12).Select(item => _cards.FromWork(item.Work, "watch", progressByWork.GetValueOrDefault(item.Work.WorkId))).ToList(),
                SeeAllRoute: null));

        shelves.AddRange(genreShelves);
        return shelves;
    }

    private IReadOnlyList<DisplayShelfDto> BuildListenShelves(
        IReadOnlyList<DisplayWorkRow> works,
        IReadOnlyList<DisplayJourneyRow> journey,
        IReadOnlyDictionary<Guid, DisplayJourneyRow> progressByWork,
        int shelfLimit)
    {
        var take = Math.Max(1, shelfLimit);
        var shelves = new List<DisplayShelfDto>();
        AddShelf(shelves, "continue-listening", "Continue listening", "Resume music and audiobooks already in progress",
            journey.Take(take).Select(item => _cards.FromJourney(item, "listen")).ToList(), null);
        AddShelf(shelves, "listen-collections", "Collections and mixes", "Albums, artists, and audiobook series from your library",
            _cards.BuildCollectionCards(works, "listen").Take(take).ToList(), "/collections");
        AddShelf(shelves, "music", "New music in your library", "Album art first for recent music",
            works.Where(work => DisplayMediaRules.NormalizeDisplayKind(work.MediaType) == "Music").Take(take).Select(work => _cards.FromWork(work, "listen", progressByWork.GetValueOrDefault(work.WorkId))).ToList(), null);
        AddShelf(shelves, "audiobooks", "Audiobooks on deck", "Spoken-word titles ready to continue",
            works.Where(work => DisplayMediaRules.NormalizeDisplayKind(work.MediaType) == "Audiobook").Take(take).Select(work => _cards.FromWork(work, "listen", progressByWork.GetValueOrDefault(work.WorkId))).ToList(), null);
        return shelves;
    }

    private static IReadOnlyDictionary<Guid, DisplayJourneyRow> LatestProgressByWork(IReadOnlyList<DisplayJourneyRow> journey) =>
        journey
            .GroupBy(item => item.WorkId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.LastAccessed).First());

    private static void AddShelf(List<DisplayShelfDto> shelves, string key, string title, string subtitle, IReadOnlyList<DisplayCardDto> cards, string? route)
    {
        if (cards.Count == 0)
        {
            return;
        }

        shelves.Add(new DisplayShelfDto(key, title, subtitle, cards, route));
    }

    private static string TitleForLane(string? lane) => DisplayMediaRules.NormalizeLane(lane) switch
    {
        "watch" => "Watch",
        "read" => "Read",
        "listen" => "Listen",
        _ => "Browse",
    };

    private static string SubtitleForLane(string lane) => lane switch
    {
        "watch" => "Movies and shows from your local library",
        "read" => "Books, comics, and audiobooks from your local library",
        "listen" => "Music and audiobooks from your local library",
        _ => "Browse your local library",
    };

    private static string EyebrowForLane(string lane, bool hasProgress) => lane switch
    {
        "watch" => hasProgress ? "Continue watching" : "Featured from your library",
        "read" => hasProgress ? "Continue reading" : "New on your shelf",
        "listen" => hasProgress ? "Continue listening" : "Featured from your library",
        _ => "From your library",
    };

    private static bool Contains(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static int ResolveShelfOffset(string? cursor, int? offset)
    {
        if (!string.IsNullOrWhiteSpace(cursor)
            && int.TryParse(cursor, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedCursor))
        {
            return Math.Max(0, parsedCursor);
        }

        return Math.Max(0, offset ?? 0);
    }

    private static string AlbumGroupKey(DisplayWorkRow work) =>
        $"{work.Album?.Trim()}|{FirstNonBlank(work.Artist, work.Author)?.Trim()}";

    private static DisplayArtworkDto ArtworkFor(IDisplayArtworkRow row) =>
        new(row.CoverUrl, row.SquareUrl, row.BannerUrl, row.BackgroundUrl, row.LogoUrl, ParseInt(row.CoverWidthPx), ParseInt(row.CoverHeightPx), ParseInt(row.SquareWidthPx), ParseInt(row.SquareHeightPx), ParseInt(row.BannerWidthPx), ParseInt(row.BannerHeightPx), ParseInt(row.BackgroundWidthPx), ParseInt(row.BackgroundHeightPx), row.AccentColor);

    private static IReadOnlyList<string> AlbumFacts(IReadOnlyList<DisplayWorkRow> works, string? artist, string? year, string? genre)
    {
        var facts = new List<string>();
        AddFact(facts, artist);
        AddFact(facts, year);
        AddFact(facts, $"{works.Count} tracks");
        foreach (var item in DisplayMediaRules.SplitValues(genre).Take(2))
        {
            AddFact(facts, item);
        }

        return facts;
    }

    private static IReadOnlyList<string> ArtistFacts(int albumCount, int trackCount, string? genre)
    {
        var facts = new List<string>();
        if (albumCount > 0)
        {
            AddFact(facts, $"{albumCount} albums");
        }

        AddFact(facts, $"{trackCount} tracks");
        foreach (var item in DisplayMediaRules.SplitValues(genre).Take(2))
        {
            AddFact(facts, item);
        }

        return facts;
    }

    private static void AddFact(List<string> facts, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var cleaned = value.Trim();
        if (!facts.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
        {
            facts.Add(cleaned);
        }
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;
}
