using MediaEngine.Contracts.Display;

namespace MediaEngine.Api.Services.Display;

public sealed class DisplayCardBuilder
{
    public DisplayCardDto FromWork(DisplayWorkRow row, string context, DisplayJourneyRow? progress)
    {
        var mediaKind = DisplayMediaRules.NormalizeDisplayKind(row.MediaType);
        var title = DisplayTitleFor(mediaKind, row.Title, row.Series, row.SeriesPosition);
        var action = PrimaryAction(row.AssetId, row.WorkId, row.CollectionId, mediaKind, progress?.ProgressPct);
        var progressDto = progress is null ? null : ToProgress(progress, action);
        return new DisplayCardDto(
            Id: row.WorkId,
            WorkId: row.WorkId,
            AssetId: row.AssetId,
            CollectionId: row.CollectionId,
            MediaType: mediaKind,
            GroupingType: "work",
            Title: title,
            Subtitle: SubtitleFor(mediaKind, CreatorFor(row), row.Series, row.SeriesPosition, row.ShowName, row.SeasonNumber, row.EpisodeNumber),
            Facts: BuildFacts(mediaKind, title, row.Year, row.Genre, row.Author, row.Artist, row.Narrator, row.Series, row.SeriesPosition, row.ShowName, row.SeasonNumber, row.EpisodeNumber, row.TrackNumber, row.Album),
            Artwork: ArtworkFor(row),
            PreferredShape: PreferredShape(row.MediaType, row.BackgroundUrl, row.BannerUrl, row.SquareUrl),
            Presentation: PresentationFor(mediaKind),
            TileTextMode: string.Equals(context, "home", StringComparison.OrdinalIgnoreCase) ? "coverOnly" : "caption",
            PreviewPlacement: DisplayMediaRules.IsReadKind(row.MediaType) ? "bottom" : "smart",
            Progress: progressDto,
            Actions: [action, DetailsAction(row.WorkId, row.CollectionId, mediaKind)],
            Flags: FlagsFor(row.MediaType, isCollection: false),
            SortTimestamp: row.CreatedAt);
    }

    public DisplayCardDto FromJourney(DisplayJourneyRow row, string context)
    {
        var mediaKind = DisplayMediaRules.NormalizeDisplayKind(row.MediaType);
        var title = DisplayTitleFor(mediaKind, row.Title, row.Series, row.SeriesPosition);
        var action = PrimaryAction(row.AssetId, row.WorkId, row.CollectionId, mediaKind, row.ProgressPct);
        return new DisplayCardDto(
            Id: row.WorkId,
            WorkId: row.WorkId,
            AssetId: row.AssetId,
            CollectionId: row.CollectionId,
            MediaType: mediaKind,
            GroupingType: "work",
            Title: title,
            Subtitle: SubtitleFor(mediaKind, FirstNonBlank(row.Author, row.Artist, row.Narrator), row.Series, row.SeriesPosition, row.ShowName, row.SeasonNumber, row.EpisodeNumber),
            Facts: BuildFacts(mediaKind, title, row.Year, row.Genre, row.Author, row.Artist, row.Narrator, row.Series, row.SeriesPosition, row.ShowName, row.SeasonNumber, row.EpisodeNumber, row.TrackNumber, row.Album),
            Artwork: ArtworkFor(row),
            PreferredShape: PreferredShape(row.MediaType, row.BackgroundUrl, row.BannerUrl, row.SquareUrl),
            Presentation: PresentationFor(mediaKind),
            TileTextMode: string.Equals(context, "home", StringComparison.OrdinalIgnoreCase) ? "coverOnly" : "caption",
            PreviewPlacement: DisplayMediaRules.IsReadKind(row.MediaType) ? "bottom" : "smart",
            Progress: ToProgress(row, action),
            Actions: [action, DetailsAction(row.WorkId, row.CollectionId, mediaKind)],
            Flags: FlagsFor(row.MediaType, isCollection: false),
            SortTimestamp: row.LastAccessed);
    }

    public IReadOnlyList<DisplayCardDto> BuildCollectionCards(IReadOnlyList<DisplayWorkRow> works, string lane) =>
        works
            .Where(work => work.CollectionId.HasValue)
            .GroupBy(work => work.CollectionId!.Value)
            .Select(group => ToCollectionCard(group.Key, group.ToList(), lane))
            .Where(card => card is not null)
            .Cast<DisplayCardDto>()
            .OrderByDescending(card => card.SortTimestamp)
            .ToList();

    public static DisplayHeroDto ToHero(DisplayCardDto card, string eyebrow) =>
        new(card.Title, card.Subtitle, eyebrow, card.Artwork, card.Progress, card.Actions);

    private DisplayCardDto? ToCollectionCard(Guid collectionId, IReadOnlyList<DisplayWorkRow> works, string lane)
    {
        if (works.Count == 0)
        {
            return null;
        }

        var representative = works
            .OrderByDescending(work => !string.IsNullOrWhiteSpace(work.BackgroundUrl) || !string.IsNullOrWhiteSpace(work.BannerUrl))
            .ThenByDescending(work => !string.IsNullOrWhiteSpace(work.CoverUrl) || !string.IsNullOrWhiteSpace(work.SquareUrl))
            .ThenByDescending(work => work.CreatedAt)
            .First();
        var mediaKind = DisplayMediaRules.NormalizeDisplayKind(representative.MediaType);
        var title = FirstNonBlank(representative.ShowName, representative.Series, representative.Album, representative.Artist, representative.Title) ?? "Collection";
        var action = new DisplayActionDto("openCollection", "Open", null, null, collectionId, CollectionUrlFor(collectionId, representative.WorkId, mediaKind));

        return new DisplayCardDto(
            Id: collectionId,
            WorkId: null,
            AssetId: null,
            CollectionId: collectionId,
            MediaType: mediaKind,
            GroupingType: "collection",
            Title: title,
            Subtitle: CollectionCountLabel(mediaKind, works.Count),
            Facts: CollectionFacts(mediaKind, works.Count, representative.Genre),
            Artwork: ArtworkFor(representative),
            PreferredShape: CollectionShape(lane, mediaKind, representative),
            Presentation: CollectionPresentation(lane, mediaKind),
            TileTextMode: "caption",
            PreviewPlacement: "smart",
            Progress: null,
            Actions: [action],
            Flags: FlagsFor(representative.MediaType, isCollection: true),
            SortTimestamp: works.Max(work => work.CreatedAt));
    }

    private static DisplayProgressDto ToProgress(DisplayJourneyRow row, DisplayActionDto resumeAction) =>
        new(Math.Clamp(row.ProgressPct, 0, 100), $"{Math.Max(1, row.ProgressPct):F0}%", row.LastAccessed, resumeAction);

    private static DisplayActionDto PrimaryAction(Guid? assetId, Guid workId, Guid? collectionId, string mediaKind, double? progressPct)
    {
        var isContinue = progressPct is > 0 and < 99.5;
        if (mediaKind is "Book" or "Comic")
        {
            return new DisplayActionDto("readWork", isContinue ? "Continue Reading" : "Read", workId, assetId, collectionId, WebUrlFor(workId, collectionId, mediaKind));
        }

        if (mediaKind is "Movie" or "TV" or "Music" or "Audiobook")
        {
            return new DisplayActionDto("playAsset", isContinue ? ContinueLabel(mediaKind) : "Play", workId, assetId, collectionId, WebUrlFor(workId, collectionId, mediaKind));
        }

        return new DisplayActionDto("openWork", "Open", workId, assetId, collectionId, WebUrlFor(workId, collectionId, mediaKind));
    }

    private static DisplayActionDto DetailsAction(Guid workId, Guid? collectionId, string mediaKind) =>
        new("openWork", "Details", workId, null, collectionId, WebUrlFor(workId, collectionId, mediaKind));

    private static string WebUrlFor(Guid workId, Guid? collectionId, string mediaKind)
    {
        if (collectionId.HasValue)
        {
            return mediaKind switch
            {
                "Movie" => $"/watch/movie/{workId}?collectionId={collectionId.Value}",
                "TV" => $"/watch/tv/show/{collectionId.Value}",
                "Music" => $"/listen/music/albums/{collectionId}",
                "Audiobook" => $"/book/{workId}?mode=listen",
                "Comic" => $"/book/{workId}?mode=read",
                "Book" => $"/book/{workId}?mode=read",
                _ => $"/collection/{collectionId}",
            };
        }

        return mediaKind switch
        {
            "Movie" => $"/watch/movie/{workId}",
            "TV" => $"/details/tvepisode/{workId}?context=watch",
            "Music" => $"/listen/music/tracks/{workId}",
            "Audiobook" => $"/book/{workId}?mode=listen",
            "Comic" => $"/book/{workId}?mode=read",
            "Book" => $"/book/{workId}?mode=read",
            _ => $"/book/{workId}",
        };
    }

    private static string CollectionUrlFor(Guid collectionId, Guid representativeWorkId, string mediaKind) =>
        mediaKind switch
        {
            "Movie" => $"/watch/movie/{representativeWorkId}?collectionId={collectionId}",
            "TV" => $"/watch/tv/show/{collectionId}",
            "Music" => $"/listen/music/albums/{collectionId}",
            "Audiobook" => $"/book/{representativeWorkId}?mode=listen",
            "Comic" => $"/details/comicseries/{collectionId}?context=comics",
            "Book" => $"/details/bookseries/{collectionId}?context=read",
            _ => $"/collection/{collectionId}",
        };

    private static DisplayCardFlagsDto FlagsFor(string mediaType, bool isCollection) =>
        new(DisplayMediaRules.IsWatchKind(mediaType) || DisplayMediaRules.IsListenKind(mediaType), DisplayMediaRules.IsReadKind(mediaType), !isCollection, isCollection, false);

    private static DisplayArtworkDto ArtworkFor(IDisplayArtworkRow row) =>
        new(
            row.CoverUrl,
            row.CoverSmallUrl,
            row.CoverMediumUrl,
            row.CoverLargeUrl,
            row.SquareUrl,
            row.SquareSmallUrl,
            row.SquareMediumUrl,
            row.SquareLargeUrl,
            row.BannerUrl,
            row.BannerSmallUrl,
            row.BannerMediumUrl,
            row.BannerLargeUrl,
            row.BackgroundUrl,
            row.BackgroundSmallUrl,
            row.BackgroundMediumUrl,
            row.BackgroundLargeUrl,
            row.LogoUrl,
            ParseInt(row.CoverWidthPx),
            ParseInt(row.CoverHeightPx),
            ParseInt(row.SquareWidthPx),
            ParseInt(row.SquareHeightPx),
            ParseInt(row.BannerWidthPx),
            ParseInt(row.BannerHeightPx),
            ParseInt(row.BackgroundWidthPx),
            ParseInt(row.BackgroundHeightPx),
            row.AccentColor);

    private static IReadOnlyList<string> BuildFacts(string mediaKind, string title, string? year, string? genre, string? author, string? artist, string? narrator, string? series, string? seriesPosition, string? showName, string? season, string? episode, string? track, string? album)
        => DisplayFactBuilder.Build(mediaKind, title, year, genre, author, artist, narrator, series, seriesPosition, showName, season, episode, track, album);

    private static string PreferredShape(string mediaType, string? backgroundUrl, string? bannerUrl, string? squareUrl)
    {
        var mediaKind = DisplayMediaRules.NormalizeDisplayKind(mediaType);
        if (mediaKind is "Music" or "Audiobook")
        {
            return "square";
        }

        if (mediaKind is "Movie" or "TV")
        {
            return !string.IsNullOrWhiteSpace(backgroundUrl) || !string.IsNullOrWhiteSpace(bannerUrl)
                ? "landscape"
                : "portrait";
        }

        if (mediaKind is not "Book" and not "Comic" && !string.IsNullOrWhiteSpace(squareUrl))
        {
            return "square";
        }

        return "portrait";
    }

    private static string PresentationFor(string mediaKind) => mediaKind switch
    {
        "TV" => "tvSeries",
        "Movie" => "movie",
        "Music" => "album",
        "Audiobook" => "audiobook",
        "Comic" => "comic",
        "Book" => "book",
        _ => "default",
    };

    private static string CollectionShape(string lane, string mediaKind, DisplayWorkRow representative)
    {
        if (lane == "listen" || mediaKind is "Music" or "Audiobook")
        {
            return "square";
        }

        return PreferredShape(representative.MediaType, representative.BackgroundUrl, representative.BannerUrl, representative.SquareUrl);
    }

    private static string CollectionPresentation(string lane, string mediaKind) => mediaKind switch
    {
        "TV" => "tvSeries",
        "Movie" => "movieSeries",
        "Book" => "bookSeries",
        "Comic" => "comicSeries",
        "Audiobook" => "audiobookSeries",
        "Music" when lane == "listen" => "album",
        "Music" => "album",
        _ => "default",
    };

    private static IReadOnlyList<string> CollectionFacts(string mediaKind, int count, string? genre)
    {
        var facts = new List<string> { CollectionCountLabel(mediaKind, count) };
        facts.AddRange(DisplayMediaRules.SplitValues(genre).Where(value => !string.Equals(value, mediaKind, StringComparison.OrdinalIgnoreCase)).Take(2));
        return facts;
    }

    private static string CollectionCountLabel(string mediaKind, int count)
    {
        var noun = mediaKind switch
        {
            "TV" => count == 1 ? "episode" : "episodes",
            "Comic" => count == 1 ? "issue" : "issues",
            "Music" => count == 1 ? "track" : "tracks",
            _ => count == 1 ? "title" : "titles",
        };

        return $"{count} {noun}";
    }

    private static string ContinueLabel(string mediaKind) => mediaKind switch
    {
        "Movie" or "TV" => "Continue Watching",
        "Book" or "Comic" => "Continue Reading",
        "Music" or "Audiobook" => "Continue Listening",
        _ => "Continue",
    };

    private static string? CreatorFor(DisplayWorkRow row) =>
        FirstNonBlank(row.Author, row.Artist, row.Director, row.Narrator);

    private static string? SubtitleFor(
        string mediaKind,
        string? creator,
        string? series,
        string? seriesPosition,
        string? showName,
        string? season,
        string? episode)
    {
        if (mediaKind == "TV")
        {
            return string.Join(" - ", new[] { showName, FormatSeasonEpisode(season, episode) }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        if (mediaKind == "Comic")
        {
            return string.Join(" - ", new[] { series, FormatIssue(seriesPosition), creator }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        return FirstNonBlank(creator, series);
    }

    private static string? FormatSeasonEpisode(string? season, string? episode)
    {
        if (string.IsNullOrWhiteSpace(season) && string.IsNullOrWhiteSpace(episode))
            return null;

        if (string.IsNullOrWhiteSpace(season))
            return $"Episode {episode}";

        if (string.IsNullOrWhiteSpace(episode))
            return $"Season {season}";

        return $"S{season} E{episode}";
    }

    private static string? FormatIssue(string? seriesPosition)
        => string.IsNullOrWhiteSpace(seriesPosition) ? null : $"Issue #{seriesPosition}";

    private static string DisplayTitleFor(string mediaKind, string title, string? series, string? seriesPosition)
    {
        if (mediaKind == "Comic" && IsGeneratedComicTitle(title, series, seriesPosition))
        {
            return FormatIssue(seriesPosition) ?? title;
        }

        return title;
    }

    private static bool IsGeneratedComicTitle(string? title, string? series, string? issueNumber)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(series) || string.IsNullOrWhiteSpace(issueNumber))
            return false;

        var normalizedTitle = NormalizeOrdinalTitle(title);
        var normalizedSeries = NormalizeOrdinalTitle(series);
        var normalizedIssue = NormalizeOrdinalTitle(issueNumber);
        return normalizedTitle == $"{normalizedSeries}{normalizedIssue}"
            || normalizedTitle == $"{normalizedSeries}issue{normalizedIssue}"
            || normalizedTitle == $"{normalizedSeries}no{normalizedIssue}"
            || (normalizedTitle.StartsWith(normalizedSeries, StringComparison.OrdinalIgnoreCase)
                && normalizedTitle.EndsWith(normalizedIssue, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeOrdinalTitle(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;
}
