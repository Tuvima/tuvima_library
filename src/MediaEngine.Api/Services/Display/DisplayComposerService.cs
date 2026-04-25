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

    public async Task<DisplayPageDto> BuildHomeAsync(bool includeCatalog = true, CancellationToken ct = default)
    {
        var works = await _repository.LoadWorksAsync(ct);
        var journey = await _repository.LoadJourneyAsync(null, ct);
        var progressByWork = LatestProgressByWork(journey);

        var continueCards = journey
            .OrderByDescending(item => item.LastAccessed)
            .Take(18)
            .Select(item => _cards.FromJourney(item, "home"))
            .ToList();

        var freshCards = works
            .OrderByDescending(work => work.CreatedAt)
            .Take(18)
            .Select(work => _cards.FromWork(work, "home", progressByWork.GetValueOrDefault(work.WorkId)))
            .ToList();

        var readCards = works
            .Where(work => DisplayMediaRules.IsReadKind(work.MediaType))
            .OrderByDescending(work => progressByWork.TryGetValue(work.WorkId, out var p) ? p.LastAccessed : work.CreatedAt)
            .Take(18)
            .Select(work => _cards.FromWork(work, "home", progressByWork.GetValueOrDefault(work.WorkId)))
            .ToList();

        var watchCards = works
            .Where(work => DisplayMediaRules.IsWatchKind(work.MediaType))
            .OrderByDescending(work => progressByWork.TryGetValue(work.WorkId, out var p) ? p.LastAccessed : work.CreatedAt)
            .Take(18)
            .Select(work => _cards.FromWork(work, "home", progressByWork.GetValueOrDefault(work.WorkId)))
            .ToList();

        var listenCards = works
            .Where(work => DisplayMediaRules.IsListenKind(work.MediaType))
            .OrderByDescending(work => progressByWork.TryGetValue(work.WorkId, out var p) ? p.LastAccessed : work.CreatedAt)
            .Take(18)
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

    public async Task<DisplayPageDto> BuildBrowseAsync(string? lane, string? mediaType, string? grouping, string? search, int offset, int limit, bool includeCatalog = true, CancellationToken ct = default)
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
        BuildBrowseAsync(null, null, "all", query, 0, limit <= 0 ? 48 : limit, includeCatalog, ct);

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

    private async Task<DisplayPageDto> BuildLaneAsync(string lane, bool includeCatalog, CancellationToken ct)
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
            "watch" => BuildWatchShelves(laneWorks, laneJourney, progressByWork),
            "read" => BuildReadShelves(laneWorks, laneJourney, progressByWork),
            "listen" => BuildListenShelves(laneWorks, laneJourney, progressByWork),
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

    private IReadOnlyList<DisplayShelfDto> BuildReadShelves(
        IReadOnlyList<DisplayWorkRow> works,
        IReadOnlyList<DisplayJourneyRow> journey,
        IReadOnlyDictionary<Guid, DisplayJourneyRow> progressByWork)
    {
        var shelves = new List<DisplayShelfDto>();
        AddShelf(shelves, "continue-reading", "Continue reading", "Books, comics, and audiobooks already in motion",
            journey.Take(12).Select(item => _cards.FromJourney(item, "read")).ToList(), null);
        AddShelf(shelves, "reading-collections", "Collections to explore", "Series and grouped reading pulled from your library",
            _cards.BuildCollectionCards(works, "read").Take(12).ToList(), "/collections");
        AddShelf(shelves, "recently-added", "Recently added to read", "Fresh pages ready to pick up",
            works.Take(18).Select(work => _cards.FromWork(work, "read", progressByWork.GetValueOrDefault(work.WorkId))).ToList(), null);

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
        IReadOnlyDictionary<Guid, DisplayJourneyRow> progressByWork)
    {
        var shelves = new List<DisplayShelfDto>();
        AddShelf(shelves, "continue-watching", "Continue watching", "Movies and shows already in progress",
            journey.Take(12).Select(item => _cards.FromJourney(item, "watch")).ToList(), null);
        AddShelf(shelves, "watch-collections", "Collections to watch", "Shows, series, and grouped franchises from your library",
            _cards.BuildCollectionCards(works, "watch").Take(12).ToList(), "/collections");
        AddShelf(shelves, "movies", "Movies in your library", "Feature films ready to play",
            works.Where(work => DisplayMediaRules.NormalizeDisplayKind(work.MediaType) == "Movie").Take(18).Select(work => _cards.FromWork(work, "watch", progressByWork.GetValueOrDefault(work.WorkId))).ToList(), null);
        AddShelf(shelves, "tv", "TV in your library", "Shows and episodes ready to continue",
            works.Where(work => DisplayMediaRules.NormalizeDisplayKind(work.MediaType) == "TV").Take(18).Select(work => _cards.FromWork(work, "watch", progressByWork.GetValueOrDefault(work.WorkId))).ToList(), null);

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
        IReadOnlyDictionary<Guid, DisplayJourneyRow> progressByWork)
    {
        var shelves = new List<DisplayShelfDto>();
        AddShelf(shelves, "continue-listening", "Continue listening", "Resume music and audiobooks already in progress",
            journey.Take(12).Select(item => _cards.FromJourney(item, "listen")).ToList(), null);
        AddShelf(shelves, "listen-collections", "Collections and mixes", "Albums, artists, and audiobook series from your library",
            _cards.BuildCollectionCards(works, "listen").Take(12).ToList(), "/collections");
        AddShelf(shelves, "music", "New music in your library", "Album art first for recent music",
            works.Where(work => DisplayMediaRules.NormalizeDisplayKind(work.MediaType) == "Music").Take(18).Select(work => _cards.FromWork(work, "listen", progressByWork.GetValueOrDefault(work.WorkId))).ToList(), null);
        AddShelf(shelves, "audiobooks", "Audiobooks on deck", "Spoken-word titles ready to continue",
            works.Where(work => DisplayMediaRules.NormalizeDisplayKind(work.MediaType) == "Audiobook").Take(18).Select(work => _cards.FromWork(work, "listen", progressByWork.GetValueOrDefault(work.WorkId))).ToList(), null);
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
}
