using MediaEngine.Contracts.Display;

namespace MediaEngine.Api.Services.Display;

public sealed class DisplayShelfBuilder
{
    private readonly DisplayCardBuilder _cards;

    public DisplayShelfBuilder(DisplayCardBuilder cards)
    {
        _cards = cards;
    }

    public IReadOnlyList<DisplayShelfDto> BuildReadShelves(
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

    public IReadOnlyList<DisplayShelfDto> BuildWatchShelves(
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

    public IReadOnlyList<DisplayShelfDto> BuildListenShelves(
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

    public static void AddShelf(List<DisplayShelfDto> shelves, string key, string title, string subtitle, IReadOnlyList<DisplayCardDto> cards, string? route)
    {
        if (cards.Count == 0)
        {
            return;
        }

        shelves.Add(new DisplayShelfDto(key, title, subtitle, cards, route));
    }
}
