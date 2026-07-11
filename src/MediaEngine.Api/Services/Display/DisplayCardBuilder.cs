using System.Globalization;
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
            Facts: BuildFacts(mediaKind, title, row.Year, row.Author, row.Artist, row.ContentRating, row.Runtime, row.Duration, row.PageCount, row.Rating),
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
            Description = row.Description,
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
            Facts: BuildFacts(mediaKind, title, row.Year, row.Author, row.Artist, row.ContentRating, row.Runtime, row.Duration, row.PageCount, row.Rating),
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
            Description = row.Description,
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

    public IReadOnlyList<DisplayCardDto> BuildWatchGroupCards(IReadOnlyList<DisplayWorkRow> works, int minimumSeriesItems = 2) =>
        BuildTvShowCards(works)
            .Concat(BuildMovieSeriesCards(works, minimumSeriesItems))
            .OrderByDescending(card => card.SortTimestamp)
            .ToList();

    public IReadOnlyList<DisplayCardDto> BuildTvShowCards(IReadOnlyList<DisplayWorkRow> works) =>
        works
            .Where(work => DisplayMediaRules.NormalizeDisplayKind(work.MediaType) == "TV")
            .GroupBy(TvShowGroupId)
            .Select(group => ToTvShowCard(group.Key, group.ToList()))
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
        var previewItems = BuildSeriesPreviewItems(mediaKind, works);
        var ownedCount = DistinctOwnedSeriesMemberCount(works);
        var knownTotalCount = KnownSeriesMemberCount(works, ownedCount);

        return new DisplayCardDto(
            Id: collectionId,
            WorkId: null,
            AssetId: null,
            CollectionId: collectionId,
            MediaType: mediaKind,
            GroupingType: CollectionPresentation(lane, mediaKind),
            Title: title,
            Subtitle: CollectionSubtitle(mediaKind, works, ownedCount, knownTotalCount),
            Facts: CollectionFacts(mediaKind, works, representative.Genre, ownedCount, knownTotalCount, representative.Rating),
            Artwork: artwork,
            PreferredShape: CollectionShape(lane, mediaKind, representative.MediaType, artwork),
            Presentation: presentation,
            TileTextMode: "caption",
            PreviewPlacement: "smart",
            Progress: null,
            Actions: [action],
            Flags: FlagsFor(representative.MediaType, isCollection: true),
            SortTimestamp: works.Max(work => work.CreatedAt))
        {
            Description = representative.Description,
            PreviewItems = previewItems,
            PreviewTotalCount = previewItems.Count > 0 ? knownTotalCount ?? ownedCount : null,
        };
    }

    private IReadOnlyList<DisplayCardDto> BuildMovieSeriesCards(IReadOnlyList<DisplayWorkRow> works, int minimumSeriesItems) =>
        works
            .Where(work => work.CollectionId.HasValue && DisplayMediaRules.NormalizeDisplayKind(work.MediaType) == "Movie")
            .GroupBy(work => work.CollectionId!.Value)
            .Select(group => ToCollectionCard(group.Key, group.ToList(), "watch", minimumSeriesItems))
            .Where(card => card is not null)
            .Cast<DisplayCardDto>()
            .ToList();

    private DisplayCardDto? ToTvShowCard(Guid showRootWorkId, IReadOnlyList<DisplayWorkRow> works)
    {
        if (works.Count == 0)
        {
            return null;
        }

        var representative = works
            .OrderByDescending(RootArtworkScore)
            .ThenByDescending(work => !string.IsNullOrWhiteSpace(work.RootBackgroundUrl) || !string.IsNullOrWhiteSpace(work.RootBannerUrl))
            .ThenByDescending(work => !string.IsNullOrWhiteSpace(work.BackgroundUrl) || !string.IsNullOrWhiteSpace(work.BannerUrl))
            .ThenByDescending(work => work.CreatedAt)
            .First();
        var artwork = RootArtworkFor(representative);
        var title = FirstNonBlank(representative.ShowName, representative.Series, representative.Title, representative.CollectionTitle) ?? "TV Show";
        var action = new DisplayActionDto("openShow", "Open Show", showRootWorkId, null, null, $"/watch/tv/show/{showRootWorkId:D}");

        return new DisplayCardDto(
            Id: showRootWorkId,
            WorkId: null,
            AssetId: null,
            CollectionId: null,
            MediaType: "TV",
            GroupingType: "tvSeries",
            Title: title,
            Subtitle: CollectionSubtitle("TV", works),
            Facts: CollectionFacts("TV", works, representative.Genre, rating: representative.Rating),
            Artwork: artwork,
            PreferredShape: PreferredShape("TV", artwork.BackgroundUrl, artwork.BannerUrl, artwork.SquareUrl),
            Presentation: "tvSeries",
            TileTextMode: "caption",
            PreviewPlacement: "smart",
            Progress: null,
            Actions: [action],
            Flags: FlagsFor("TV", isCollection: true),
            SortTimestamp: works.Max(work => work.CreatedAt))
        {
            Description = representative.Description,
            Badges = BuildBadges("TV", representative.Quality, FirstNonBlank(representative.Network, representative.Source)),
        };
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
                "Audiobook" => $"/listen/audiobook/{workId}",
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
            "Audiobook" => $"/listen/audiobook/{workId}",
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
            "Audiobook" => $"/listen/audiobook/{representativeWorkId}",
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

    private static DisplayArtworkDto RootArtworkFor(DisplayWorkRow representative)
    {
        var row = new ArtworkProjection
        {
            CoverUrl = FirstNonBlank(representative.RootCoverUrl, representative.CoverUrl),
            CoverSmallUrl = FirstNonBlank(representative.RootCoverSmallUrl, representative.CoverSmallUrl),
            CoverMediumUrl = FirstNonBlank(representative.RootCoverMediumUrl, representative.CoverMediumUrl),
            CoverLargeUrl = FirstNonBlank(representative.RootCoverLargeUrl, representative.CoverLargeUrl),
            SquareUrl = FirstNonBlank(representative.RootSquareUrl, representative.SquareUrl),
            SquareSmallUrl = FirstNonBlank(representative.RootSquareSmallUrl, representative.SquareSmallUrl),
            SquareMediumUrl = FirstNonBlank(representative.RootSquareMediumUrl, representative.SquareMediumUrl),
            SquareLargeUrl = FirstNonBlank(representative.RootSquareLargeUrl, representative.SquareLargeUrl),
            BannerUrl = FirstNonBlank(representative.RootBannerUrl, representative.RootBackgroundUrl, representative.BannerUrl),
            BannerSmallUrl = FirstNonBlank(representative.RootBannerSmallUrl, representative.RootBackgroundSmallUrl, representative.BannerSmallUrl),
            BannerMediumUrl = FirstNonBlank(representative.RootBannerMediumUrl, representative.RootBackgroundMediumUrl, representative.BannerMediumUrl),
            BannerLargeUrl = FirstNonBlank(representative.RootBannerLargeUrl, representative.RootBackgroundLargeUrl, representative.BannerLargeUrl),
            BackgroundUrl = FirstNonBlank(representative.RootBackgroundUrl, representative.RootBannerUrl, representative.BackgroundUrl),
            BackgroundSmallUrl = FirstNonBlank(representative.RootBackgroundSmallUrl, representative.RootBannerSmallUrl, representative.BackgroundSmallUrl),
            BackgroundMediumUrl = FirstNonBlank(representative.RootBackgroundMediumUrl, representative.RootBannerMediumUrl, representative.BackgroundMediumUrl),
            BackgroundLargeUrl = FirstNonBlank(representative.RootBackgroundLargeUrl, representative.RootBannerLargeUrl, representative.BackgroundLargeUrl),
            LogoUrl = FirstNonBlank(representative.RootLogoUrl, representative.LogoUrl),
            CoverWidthPx = representative.RootCoverWidthPx ?? representative.CoverWidthPx,
            CoverHeightPx = representative.RootCoverHeightPx ?? representative.CoverHeightPx,
            SquareWidthPx = representative.RootSquareWidthPx ?? representative.SquareWidthPx,
            SquareHeightPx = representative.RootSquareHeightPx ?? representative.SquareHeightPx,
            BannerWidthPx = representative.RootBannerWidthPx ?? representative.BannerWidthPx,
            BannerHeightPx = representative.RootBannerHeightPx ?? representative.BannerHeightPx,
            BackgroundWidthPx = representative.RootBackgroundWidthPx ?? representative.BackgroundWidthPx,
            BackgroundHeightPx = representative.RootBackgroundHeightPx ?? representative.BackgroundHeightPx,
            AccentColor = FirstNonBlank(representative.RootAccentColor, representative.AccentColor),
        };

        return ArtworkFor(row);
    }

    private static IReadOnlyList<string> BuildFacts(
        string mediaKind,
        string title,
        string? year,
        string? author,
        string? artist,
        string? contentRating,
        string? runtime,
        string? duration,
        string? pageCount,
        string? starRating)
        => DisplayFactBuilder.Build(
            mediaKind,
            title,
            year: year,
            author: author,
            artist: artist,
            contentRating: contentRating,
            runtime: runtime,
            duration: duration,
            pageCount: pageCount,
            starRating: starRating);

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

    private static IReadOnlyList<string> CollectionFacts(
        string mediaKind,
        IReadOnlyList<DisplayWorkRow> works,
        string? genre,
        int? ownedCount = null,
        int? knownTotalCount = null,
        string? rating = null)
    {
        var facts = new List<string>();
        if (mediaKind == "TV" && HasRecentlyAddedEpisodes(works))
        {
            AddFact(facts, "New episodes added");
        }

        AddFact(facts, CollectionSubtitle(mediaKind, works, ownedCount, knownTotalCount));
        AddFact(facts, DisplayFactBuilder.Build(mediaKind, string.Empty, rating: rating).FirstOrDefault());
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

    private static IReadOnlyList<DisplayCardPreviewItemDto> BuildSeriesPreviewItems(string mediaKind, IReadOnlyList<DisplayWorkRow> works)
    {
        if (mediaKind is not ("Book" or "Comic" or "Movie"))
        {
            return [];
        }

        return works
            .Select(work => new
            {
                Work = work,
                ImageUrl = SeriesPreviewImage(work),
                SortPosition = ParseSeriesPosition(work.SeriesPosition),
                DisplayTitle = DisplayTitleFor(mediaKind, work.Title, work.Series, work.SeriesPosition),
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.ImageUrl))
            .GroupBy(item => item.Work.WorkId)
            .Select(group => group.First())
            .OrderBy(item => item.SortPosition is null)
            .ThenBy(item => item.SortPosition ?? double.MaxValue)
            .ThenBy(item => item.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Work.CreatedAt)
            .Take(4)
            .Select(item => new DisplayCardPreviewItemDto(
                item.Work.WorkId,
                item.Work.AssetId,
                item.DisplayTitle,
                item.ImageUrl!,
                SeriesPreviewShape(item.Work, item.ImageUrl),
                item.Work.SeriesPosition))
            .ToList();
    }

    private static string? SeriesPreviewImage(DisplayWorkRow work) =>
        FirstNonBlank(
            work.CoverSmallUrl,
            work.CoverMediumUrl,
            work.CoverLargeUrl,
            work.CoverUrl,
            work.SquareSmallUrl,
            work.SquareMediumUrl,
            work.SquareLargeUrl,
            work.SquareUrl);

    private static string SeriesPreviewShape(DisplayWorkRow work, string? imageUrl)
    {
        if (MatchesAny(imageUrl, work.SquareUrl, work.SquareSmallUrl, work.SquareMediumUrl, work.SquareLargeUrl))
        {
            return "square";
        }

        var width = ParseInt(work.CoverWidthPx);
        var height = ParseInt(work.CoverHeightPx);
        if (width is > 0 && height is > 0)
        {
            var ratio = width.Value / (double)height.Value;
            if (ratio >= 1.32)
            {
                return "wide";
            }

            if (ratio >= 0.86)
            {
                return "square";
            }
        }

        return "portrait";
    }

    private static double? ParseSeriesPosition(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var started = false;
        var seenDecimal = false;
        var chars = new List<char>();
        foreach (var character in value.Trim())
        {
            if (char.IsDigit(character))
            {
                started = true;
                chars.Add(character);
                continue;
            }

            if (character == '.' && started && !seenDecimal)
            {
                seenDecimal = true;
                chars.Add(character);
                continue;
            }

            if (started)
            {
                break;
            }
        }

        return chars.Count > 0
            && double.TryParse(new string(chars.ToArray()), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
    }

    private static string CollectionSubtitle(
        string mediaKind,
        IReadOnlyList<DisplayWorkRow> works,
        int? ownedCount = null,
        int? knownTotalCount = null)
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

        if (mediaKind is "Book" or "Comic" or "Movie" or "Audiobook")
        {
            return OwnedSeriesCountLabel(mediaKind, ownedCount ?? DistinctOwnedSeriesMemberCount(works), knownTotalCount);
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

    private static int? KnownSeriesMemberCount(IReadOnlyList<DisplayWorkRow> works, int ownedCount)
    {
        var manifestTotal = works.Max(work => work.CollectionManifestTotalCount);
        return manifestTotal > ownedCount ? manifestTotal : null;
    }

    private static string OwnedSeriesCountLabel(string mediaKind, int ownedCount, int? knownTotalCount)
    {
        var noun = mediaKind switch
        {
            "Comic" => knownTotalCount is > 1 ? "issues" : ownedCount == 1 ? "issue" : "issues",
            _ => knownTotalCount is > 1 ? "titles" : ownedCount == 1 ? "title" : "titles",
        };

        return knownTotalCount is > 0
            ? $"{ownedCount} of {knownTotalCount.Value} {noun} owned"
            : $"{ownedCount} owned {noun}";
    }

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

    private static int RootArtworkScore(DisplayWorkRow work)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(work.RootBackgroundUrl) || !string.IsNullOrWhiteSpace(work.RootBannerUrl))
        {
            score += 8;
        }

        if (!string.IsNullOrWhiteSpace(work.RootCoverUrl) || !string.IsNullOrWhiteSpace(work.RootSquareUrl))
        {
            score += 4;
        }

        if (!string.IsNullOrWhiteSpace(work.BackgroundUrl) || !string.IsNullOrWhiteSpace(work.BannerUrl))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(work.CoverUrl) || !string.IsNullOrWhiteSpace(work.SquareUrl))
        {
            score += 1;
        }

        return score;
    }

    private static Guid TvShowGroupId(DisplayWorkRow work)
    {
        if (work.RootWorkId != Guid.Empty)
        {
            return work.RootWorkId;
        }

        return work.CollectionId ?? work.WorkId;
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
            return SeriesSubtitle(series, seriesPosition, "Issue") ?? creator;
        }

        if (mediaKind is "Book" or "Audiobook")
        {
            return SeriesSubtitle(series, seriesPosition, "Book") ?? creator;
        }

        if (mediaKind == "Movie")
        {
            return SeriesSubtitle(series, seriesPosition, "Film");
        }

        return FirstNonBlank(creator, series);
    }

    private static string? SeriesSubtitle(string? series, string? position, string memberLabel)
    {
        if (string.IsNullOrWhiteSpace(series))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(position)
            ? series.Trim()
            : $"{series.Trim()}, {memberLabel} {position.Trim()}";
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

    private static bool MatchesAny(string? value, params string?[] candidates) =>
        !string.IsNullOrWhiteSpace(value)
        && candidates.Any(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));

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
