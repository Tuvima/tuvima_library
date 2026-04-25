using MediaEngine.Contracts.Display;
using MediaEngine.Web.Services.Navigation;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Adapter that unifies Collection and Work display for PosterCard/PosterSwimlane.
/// Home page uses FromCollection(); lane pages use FromWork().
/// </summary>
public sealed record PosterItemViewModel
{
    public required Guid    Id            { get; init; }
    public required string  Title         { get; init; }
    public string?          Subtitle      { get; init; }
    public string?          CoverUrl      { get; init; }
    public string?          Year          { get; init; }
    public string?          FormatBadge   { get; init; }
    public bool             IsNew         { get; init; }
    public double?          Progress      { get; init; }
    public required string  NavigationUrl { get; init; }
    public string           DominantHexColor { get; init; } = "#1A2040";
    public PosterSourceType SourceType    { get; init; }

    public static PosterItemViewModel FromDisplayCard(DisplayCardDto card) => new()
    {
        Id = card.CollectionId ?? card.WorkId ?? card.AssetId ?? card.Id,
        Title = card.Title,
        Subtitle = card.Subtitle ?? card.Facts.FirstOrDefault(),
        CoverUrl = card.Artwork.CoverUrl ?? card.Artwork.SquareUrl ?? card.Artwork.BannerUrl ?? card.Artwork.BackgroundUrl,
        Year = card.Facts.FirstOrDefault(IsYearFact),
        FormatBadge = card.MediaType,
        IsNew = DateTimeOffset.UtcNow - card.SortTimestamp < TimeSpan.FromDays(7),
        Progress = card.Progress?.Percent,
        NavigationUrl = card.Actions.FirstOrDefault(action => !string.IsNullOrWhiteSpace(action.WebUrl))?.WebUrl
            ?? FallbackNavigation(card),
        DominantHexColor = card.Artwork.AccentColor ?? "#1A2040",
        SourceType = card.Flags.IsCollection || card.GroupingType is "collection" or "album" or "artist"
            ? PosterSourceType.Collection
            : PosterSourceType.Work,
    };

    public static PosterItemViewModel FromCollection(CollectionViewModel collection) => new()
    {
        Id               = collection.Id,
        Title            = collection.DisplayName,
        Subtitle         = collection.Author ?? collection.Series,
        CoverUrl         = collection.CoverUrl,
        Year             = collection.Year,
        FormatBadge      = collection.PrimaryMediaType,
        IsNew            = DateTimeOffset.UtcNow - collection.CreatedAt < TimeSpan.FromDays(7),
        Progress         = null,
        NavigationUrl    = MediaNavigation.ForCollection(collection),
        DominantHexColor = collection.DominantHexColor,
        SourceType       = PosterSourceType.Collection,
    };

    public static PosterItemViewModel FromWork(WorkViewModel work, string? fallbackCoverUrl = null, string? dominantHexColor = null) =>
        FromDisplayCard(ToDisplayCard(work, fallbackCoverUrl, dominantHexColor)) with
        {
            IsNew = false,
            SourceType = PosterSourceType.Work,
        };

    public static PosterItemViewModel FromJourney(JourneyItemViewModel item) => new()
    {
        Id               = item.WorkId,
        Title            = item.Title,
        Subtitle         = item.Author,
        CoverUrl         = item.CoverUrl,
        Year             = null,
        FormatBadge      = item.MediaType,
        IsNew            = false,
        Progress         = item.ProgressPct > 0 ? item.ProgressPct : null,
        NavigationUrl    = MediaNavigation.ForJourney(item),
        DominantHexColor = "#1A2040",
        SourceType       = PosterSourceType.Work,
    };

    private static DisplayCardDto ToDisplayCard(WorkViewModel work, string? fallbackCoverUrl, string? dominantHexColor)
    {
        var action = new DisplayActionDto(
            Type: IsReadable(work.MediaType) ? "openWork" : "playAsset",
            Label: "Open",
            WorkId: work.Id,
            AssetId: work.AssetId,
            CollectionId: work.CollectionId,
            WebUrl: MediaNavigation.ForWork(work));

        return new DisplayCardDto(
            Id: work.Id,
            WorkId: work.Id,
            AssetId: work.AssetId,
            CollectionId: work.CollectionId,
            MediaType: work.MediaType,
            GroupingType: "work",
            Title: work.Title,
            Subtitle: FirstNonBlank(work.Author, work.Artist, work.Narrator),
            Facts: CompactFacts(work),
            Artwork: new DisplayArtworkDto(
                CoverUrl: work.CoverUrl ?? fallbackCoverUrl,
                SquareUrl: work.SquareUrl,
                BannerUrl: work.BannerUrl,
                BackgroundUrl: work.BackgroundUrl,
                LogoUrl: work.LogoUrl,
                CoverWidthPx: work.CoverWidthPx,
                CoverHeightPx: work.CoverHeightPx,
                SquareWidthPx: work.SquareWidthPx,
                SquareHeightPx: work.SquareHeightPx,
                BannerWidthPx: work.BannerWidthPx,
                BannerHeightPx: work.BannerHeightPx,
                BackgroundWidthPx: work.BackgroundWidthPx,
                BackgroundHeightPx: work.BackgroundHeightPx,
                AccentColor: dominantHexColor ?? work.ArtworkAccentHex),
            PreferredShape: IsListen(work.MediaType) ? "square" : "portrait",
            Presentation: work.MediaType,
            TileTextMode: "caption",
            PreviewPlacement: IsReadable(work.MediaType) ? "bottom" : "smart",
            Progress: null,
            Actions: [action],
            Flags: new DisplayCardFlagsDto(IsPlayable(work.MediaType), IsReadable(work.MediaType), true, false, false),
            SortTimestamp: work.CreatedAt);
    }

    private static IReadOnlyList<string> CompactFacts(WorkViewModel work)
    {
        var facts = new List<string>();
        AddFact(facts, work.Year);
        AddFact(facts, FirstNonBlank(work.Genre, work.Genres.FirstOrDefault()));
        AddFact(facts, work.Album);
        AddFact(facts, work.Series);
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

    private static string FallbackNavigation(DisplayCardDto card)
    {
        if (card.CollectionId.HasValue && card.Flags.IsCollection)
        {
            return $"/collection/{card.CollectionId.Value}";
        }

        return card.WorkId.HasValue ? $"/book/{card.WorkId.Value}" : "/";
    }

    private static bool IsYearFact(string value) =>
        value.Length == 4 && value.All(char.IsDigit);

    private static bool IsReadable(string? mediaType)
    {
        var normalized = mediaType ?? string.Empty;
        return normalized.Contains("book", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("comic", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("epub", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsListen(string? mediaType)
    {
        var normalized = mediaType ?? string.Empty;
        return normalized.Contains("music", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("audio", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlayable(string? mediaType)
    {
        var normalized = mediaType ?? string.Empty;
        return normalized.Contains("movie", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("tv", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("video", StringComparison.OrdinalIgnoreCase)
               || IsListen(mediaType);
    }
}

public enum PosterSourceType { Collection, Work }
