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
            PreviewPlacement: "smart",
            Progress: progressDto,
            Actions: [action, DetailsAction(row.WorkId, row.CollectionId, mediaKind)],
            Flags: FlagsFor(row.MediaType, isCollection: false),
            SortTimestamp: row.CreatedAt)
        {
            Badges = BuildBadges(mediaKind, row.Quality, FirstNonBlank(row.Network, row.Source)),
        };
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
            PreviewPlacement: "smart",
            Progress: ToProgress(row, action),
            Actions: [action, DetailsAction(row.WorkId, row.CollectionId, mediaKind)],
            Flags: FlagsFor(row.MediaType, isCollection: false),
            SortTimestamp: row.LastAccessed)
        {
            Badges = BuildBadges(mediaKind, row.Quality, FirstNonBlank(row.Network, row.Source)),
        };
    }

    public IReadOnlyList<DisplayCardDto> BuildCollectionCards(IReadOnlyList<DisplayWorkRow> works, string lane, int minimumSeriesItems = 2) =>
        works
            .Where(work => work.CollectionId.HasValue)
            .GroupBy(work => work.CollectionId!.Value)
            .Select(group => ToCollectionCard(group.Key, group.ToList(), lane, minimumSeriesItems))
            .Where(card => card is not null)
            .Cast<DisplayCardDto>()
            .OrderByDescending(card => card.SortTimestamp)
            .ToList();

    public static DisplayHeroDto ToHero(DisplayCardDto card, string eyebrow) =>
        new(card.Title, card.Subtitle, eyebrow, card.Artwork, card.Progress, card.Actions)
        {
            Facts = card.Facts,
        };

    public static DisplayCardDto FromHomeCollection(DisplayHomeCollectionRow row)
    {
        var action = new DisplayActionDto("openCollection", "Open", null, null, row.CollectionId, $"/collection/{row.CollectionId:D}");
        return new DisplayCardDto(
            Id: row.CollectionId,
            WorkId: null,
            AssetId: null,
            CollectionId: row.CollectionId,
            MediaType: "Collection",
            GroupingType: "collection",
            Title: row.Title,
            Subtitle: row.Subtitle ?? CollectionCountLabel("Collection", row.ItemCount),
            Facts: HomeCollectionFacts(row),
            Artwork: ArtworkFor(row),
            PreferredShape: "landscape",
            Presentation: "default",
            TileTextMode: "caption",
            PreviewPlacement: "smart",
            Progress: null,
            Actions: [action],
            Flags: new DisplayCardFlagsDto(false, false, false, true, false),
            SortTimestamp: row.CreatedAt);
    }

    private DisplayCardDto? ToCollectionCard(Guid collectionId, IReadOnlyList<DisplayWorkRow> works, string lane, int minimumSeriesItems)
    {
        if (works.Count == 0)
        {
            return null;
        }

        var representative = works
            .OrderByDescending(CollectionArtworkScore)
            .ThenByDescending(work => !string.IsNullOrWhiteSpace(work.BackgroundUrl) || !string.IsNullOrWhiteSpace(work.BannerUrl))
            .ThenByDescending(work => !string.IsNullOrWhiteSpace(work.CoverUrl) || !string.IsNullOrWhiteSpace(work.SquareUrl))
            .ThenByDescending(work => work.CreatedAt)
            .First();
        var mediaKind = DisplayMediaRules.NormalizeDisplayKind(representative.MediaType);
        if (!ShouldRenderCollectionCard(mediaKind, works, minimumSeriesItems))
        {
            return null;
        }

        var title = FirstNonBlank(representative.CollectionTitle, representative.ShowName, representative.Series, representative.Album, representative.Artist, representative.Title) ?? "Collection";
        var action = new DisplayActionDto(CollectionActionKind(mediaKind), CollectionActionLabel(mediaKind), null, null, collectionId, CollectionUrlFor(collectionId, representative.WorkId, mediaKind, title));
        var artwork = CollectionArtworkFor(representative);
        var presentation = CollectionPresentation(lane, mediaKind);

        return new DisplayCardDto(
            Id: collectionId,
            WorkId: null,
            AssetId: null,
            CollectionId: collectionId,
            MediaType: mediaKind,
            GroupingType: CollectionPresentation(lane, mediaKind),
            Title: title,
            Subtitle: CollectionSubtitle(mediaKind, works),
            Facts: CollectionFacts(mediaKind, works, representative.Genre),
            Artwork: artwork,
            PreferredShape: CollectionShape(lane, mediaKind, representative.MediaType, artwork),
            Presentation: presentation,
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

    private static string CollectionUrlFor(Guid collectionId, Guid representativeWorkId, string mediaKind, string title) =>
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

    private static DisplayArtworkDto CollectionArtworkFor(DisplayWorkRow representative)
    {
        var collectionCover = representative.CollectionCoverUrl;
        var collectionSquare = representative.CollectionSquareUrl;
        var collectionBanner = representative.CollectionBannerUrl;
        var collectionBackground = representative.CollectionBackgroundUrl;
        var collectionLogo = representative.CollectionLogoUrl;

        var row = new ArtworkProjection
        {
            CoverUrl = FirstNonBlank(collectionCover, representative.RootCoverUrl, representative.CoverUrl),
            CoverSmallUrl = FirstNonBlank(collectionCover, representative.RootCoverSmallUrl, representative.CoverSmallUrl),
            CoverMediumUrl = FirstNonBlank(collectionCover, representative.RootCoverMediumUrl, representative.CoverMediumUrl),
            CoverLargeUrl = FirstNonBlank(collectionCover, representative.RootCoverLargeUrl, representative.CoverLargeUrl),
            SquareUrl = FirstNonBlank(collectionSquare, representative.RootSquareUrl, representative.SquareUrl),
            SquareSmallUrl = FirstNonBlank(collectionSquare, representative.RootSquareSmallUrl, representative.SquareSmallUrl),
            SquareMediumUrl = FirstNonBlank(collectionSquare, representative.RootSquareMediumUrl, representative.SquareMediumUrl),
            SquareLargeUrl = FirstNonBlank(collectionSquare, representative.RootSquareLargeUrl, representative.SquareLargeUrl),
            BannerUrl = FirstNonBlank(collectionBanner, representative.RootBannerUrl, collectionBackground, representative.RootBackgroundUrl, representative.BannerUrl),
            BannerSmallUrl = FirstNonBlank(collectionBanner, representative.RootBannerSmallUrl, collectionBackground, representative.RootBackgroundSmallUrl, representative.BannerSmallUrl),
            BannerMediumUrl = FirstNonBlank(collectionBanner, representative.RootBannerMediumUrl, collectionBackground, representative.RootBackgroundMediumUrl, representative.BannerMediumUrl),
            BannerLargeUrl = FirstNonBlank(collectionBanner, representative.RootBannerLargeUrl, collectionBackground, representative.RootBackgroundLargeUrl, representative.BannerLargeUrl),
            BackgroundUrl = FirstNonBlank(collectionBackground, representative.RootBackgroundUrl, collectionBanner, representative.RootBannerUrl, representative.BackgroundUrl),
            BackgroundSmallUrl = FirstNonBlank(collectionBackground, representative.RootBackgroundSmallUrl, collectionBanner, representative.RootBannerSmallUrl, representative.BackgroundSmallUrl),
            BackgroundMediumUrl = FirstNonBlank(collectionBackground, representative.RootBackgroundMediumUrl, collectionBanner, representative.RootBannerMediumUrl, representative.BackgroundMediumUrl),
            BackgroundLargeUrl = FirstNonBlank(collectionBackground, representative.RootBackgroundLargeUrl, collectionBanner, representative.RootBannerLargeUrl, representative.BackgroundLargeUrl),
            LogoUrl = FirstNonBlank(collectionLogo, representative.RootLogoUrl, representative.LogoUrl),
            CoverWidthPx = representative.RootCoverWidthPx ?? representative.CoverWidthPx,
            CoverHeightPx = representative.RootCoverHeightPx ?? representative.CoverHeightPx,
            SquareWidthPx = representative.RootSquareWidthPx ?? representative.SquareWidthPx,
            SquareHeightPx = representative.RootSquareHeightPx ?? representative.SquareHeightPx,
            BannerWidthPx = representative.RootBannerWidthPx ?? representative.BannerWidthPx,
            BannerHeightPx = representative.RootBannerHeightPx ?? representative.BannerHeightPx,
            BackgroundWidthPx = representative.RootBackgroundWidthPx ?? representative.BackgroundWidthPx,
            BackgroundHeightPx = representative.RootBackgroundHeightPx ?? representative.BackgroundHeightPx,
            AccentColor = FirstNonBlank(representative.CollectionAccentColor, representative.RootAccentColor, representative.AccentColor),
        };

        return ArtworkFor(row);
    }

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

    private static string CollectionShape(string lane, string mediaKind, string mediaType, DisplayArtworkDto artwork)
    {
        if (lane == "listen" || mediaKind is "Music" or "Audiobook")
        {
            return "square";
        }

        return PreferredShape(mediaType, artwork.BackgroundUrl, artwork.BannerUrl, artwork.SquareUrl);
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

    private static IReadOnlyList<string> CollectionFacts(string mediaKind, IReadOnlyList<DisplayWorkRow> works, string? genre)
    {
        var facts = new List<string>();
        if (mediaKind == "TV" && HasRecentlyAddedEpisodes(works))
        {
            AddFact(facts, "New episodes added");
        }

        AddFact(facts, CollectionSubtitle(mediaKind, works));
        facts.AddRange(DisplayMediaRules.SplitValues(genre).Where(value => !string.Equals(value, mediaKind, StringComparison.OrdinalIgnoreCase)).Take(2));
        return facts;
    }

    private static IReadOnlyList<string> HomeCollectionFacts(DisplayHomeCollectionRow row)
    {
        var facts = new List<string>();
        AddFact(facts, CollectionCountLabel("Collection", row.ItemCount));
        AddFact(facts, row.CollectionType);

        if (row.WatchCount > 0)
        {
            AddFact(facts, $"{row.WatchCount} watch");
        }

        if (row.ReadCount > 0)
        {
            AddFact(facts, $"{row.ReadCount} read");
        }

        if (row.ListenCount > 0)
        {
            AddFact(facts, $"{row.ListenCount} listen");
        }

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

    private static string CollectionSubtitle(string mediaKind, IReadOnlyList<DisplayWorkRow> works)
    {
        if (mediaKind == "TV")
        {
            var seasonCount = works
                .Select(work => work.SeasonNumber)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            if (seasonCount > 0)
            {
                return seasonCount == 1 ? "1 season" : $"{seasonCount} seasons";
            }
        }

        return CollectionCountLabel(mediaKind, works.Count);
    }

    private static bool HasRecentlyAddedEpisodes(IReadOnlyList<DisplayWorkRow> works)
    {
        if (works.Count == 0)
        {
            return false;
        }

        var threshold = DateTimeOffset.UtcNow.AddDays(-30);
        return works.Any(work => DisplayMediaRules.NormalizeDisplayKind(work.MediaType) == "TV" && work.CreatedAt >= threshold);
    }

    private static bool ShouldRenderCollectionCard(string mediaKind, IReadOnlyList<DisplayWorkRow> works, int minimumSeriesItems)
    {
        if (mediaKind is "TV" or "Music")
        {
            return true;
        }

        return DistinctOwnedSeriesMemberCount(works) >= Math.Max(2, minimumSeriesItems);
    }

    private static int DistinctOwnedSeriesMemberCount(IReadOnlyList<DisplayWorkRow> works) =>
        works
            .Select(work => FirstNonBlank(work.IdentityQid, work.Title))
            .Select(NormalizeSeriesMemberKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static int CollectionArtworkScore(DisplayWorkRow work)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(work.CollectionBackgroundUrl) || !string.IsNullOrWhiteSpace(work.CollectionBannerUrl))
        {
            score += 8;
        }

        if (!string.IsNullOrWhiteSpace(work.RootBackgroundUrl) || !string.IsNullOrWhiteSpace(work.RootBannerUrl))
        {
            score += 4;
        }

        if (!string.IsNullOrWhiteSpace(work.CollectionCoverUrl) || !string.IsNullOrWhiteSpace(work.CollectionSquareUrl))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(work.RootCoverUrl) || !string.IsNullOrWhiteSpace(work.RootSquareUrl))
        {
            score += 1;
        }

        return score;
    }

    private static string CollectionActionKind(string mediaKind) => mediaKind switch
    {
        "TV" => "openShow",
        "Movie" or "Book" or "Comic" or "Audiobook" => "openSeries",
        "Music" => "openAlbum",
        _ => "openCollection",
    };

    private static string CollectionActionLabel(string mediaKind) => mediaKind switch
    {
        "TV" => "Open Show",
        "Movie" or "Book" or "Comic" or "Audiobook" => "Open Series",
        "Music" => "Open Album",
        _ => "Open",
    };

    private static string ContinueLabel(string mediaKind) => mediaKind switch
    {
        "Movie" or "TV" => "Continue Watching",
        "Book" or "Comic" => "Continue Reading",
        "Music" or "Audiobook" => "Continue Listening",
        _ => "Continue",
    };

    private static IReadOnlyList<DisplayCardBadgeDto> BuildBadges(string mediaKind, string? quality, string? source)
    {
        if (mediaKind is not ("Movie" or "TV"))
        {
            return [];
        }

        var badges = new List<DisplayCardBadgeDto>();
        if (NormalizeWatchQualityLabel(quality) is { } qualityLabel)
        {
            badges.Add(new DisplayCardBadgeDto("quality", qualityLabel));
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            badges.Add(new DisplayCardBadgeDto("source", source.Trim()));
        }

        return badges;
    }

    private static string? NormalizeWatchQualityLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Equals("2160p", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("UHD", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Ultra HD", StringComparison.OrdinalIgnoreCase))
        {
            return "4K";
        }

        if (normalized.Contains("3840", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("2160", StringComparison.OrdinalIgnoreCase))
        {
            return "4K";
        }

        if (normalized.Contains("1080", StringComparison.OrdinalIgnoreCase))
        {
            return "HD";
        }

        return normalized;
    }

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

    private static string? NormalizeSeriesMemberKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var chars = value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
        return chars.Length == 0 ? null : new string(chars);
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static void AddFact(List<string> facts, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (!facts.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            facts.Add(trimmed);
        }
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;

    private sealed class ArtworkProjection : IDisplayArtworkRow
    {
        public string? CoverUrl { get; init; }
        public string? CoverSmallUrl { get; init; }
        public string? CoverMediumUrl { get; init; }
        public string? CoverLargeUrl { get; init; }
        public string? SquareUrl { get; init; }
        public string? SquareSmallUrl { get; init; }
        public string? SquareMediumUrl { get; init; }
        public string? SquareLargeUrl { get; init; }
        public string? BannerUrl { get; init; }
        public string? BannerSmallUrl { get; init; }
        public string? BannerMediumUrl { get; init; }
        public string? BannerLargeUrl { get; init; }
        public string? BackgroundUrl { get; init; }
        public string? BackgroundSmallUrl { get; init; }
        public string? BackgroundMediumUrl { get; init; }
        public string? BackgroundLargeUrl { get; init; }
        public string? LogoUrl { get; init; }
        public string? CoverWidthPx { get; init; }
        public string? CoverHeightPx { get; init; }
        public string? SquareWidthPx { get; init; }
        public string? SquareHeightPx { get; init; }
        public string? BannerWidthPx { get; init; }
        public string? BannerHeightPx { get; init; }
        public string? BackgroundWidthPx { get; init; }
        public string? BackgroundHeightPx { get; init; }
        public string? AccentColor { get; init; }
    }
}
