using Dapper;
using System.Globalization;
using System.Text;
using MediaEngine.Api.Endpoints;
using MediaEngine.Api.Services.Display;
using MediaEngine.Contracts.Details;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Details;

public sealed class DetailComposerService
{
    private readonly IDatabaseConnection _db;
    private readonly ILibraryItemRepository _libraryItems;
    private readonly IPersonRepository _persons;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly ICanonicalValueArrayRepository _canonicalArrays;

    public DetailComposerService(
        IDatabaseConnection db,
        ILibraryItemRepository libraryItems,
        IPersonRepository persons,
        IEntityAssetRepository entityAssets,
        ICanonicalValueArrayRepository canonicalArrays)
    {
        _db = db;
        _libraryItems = libraryItems;
        _persons = persons;
        _entityAssets = entityAssets;
        _canonicalArrays = canonicalArrays;
    }

    public async Task<DetailPageViewModel?> BuildAsync(
        DetailEntityType entityType,
        Guid id,
        DetailPresentationContext context,
        CancellationToken ct = default)
    {
        var isAdminView = context is DetailPresentationContext.Admin;

        return entityType switch
        {
            DetailEntityType.Person or DetailEntityType.MusicArtist => await BuildPersonAsync(id, entityType, context, isAdminView, ct),
            DetailEntityType.Collection or DetailEntityType.TvShow or DetailEntityType.MovieSeries or DetailEntityType.BookSeries
                or DetailEntityType.ComicSeries or DetailEntityType.MusicAlbum => await BuildCollectionAsync(id, entityType, context, isAdminView, ct),
            DetailEntityType.Character => await BuildCharacterAsync(id, context, isAdminView, ct),
            DetailEntityType.Universe => await BuildUniverseAsync(id, context, isAdminView, ct),
            _ => await BuildWorkAsync(id, entityType, context, isAdminView, ct),
        };
    }

    public static bool TryParseEntityType(string value, out DetailEntityType entityType)
    {
        entityType = default;
        if (value.Contains("podcast", StringComparison.OrdinalIgnoreCase))
            return false;

        var normalized = value.Replace("-", string.Empty).Replace("_", string.Empty);
        return Enum.TryParse(normalized, ignoreCase: true, out entityType);
    }

    public static DetailPresentationContext ParseContext(string? value)
        => Enum.TryParse<DetailPresentationContext>(value, ignoreCase: true, out var parsed)
            ? parsed
            : DetailPresentationContext.Default;

    public static ArtworkPresentationMode ResolveArtworkPresentationMode(
        DetailEntityType entityType,
        string? backdropUrl,
        string? bannerUrl,
        string? coverUrl,
        string? posterUrl,
        string? portraitUrl,
        int relatedArtworkCount,
        int ownedFormatCount)
    {
        if (ownedFormatCount > 1 && entityType is DetailEntityType.Work or DetailEntityType.Book or DetailEntityType.Audiobook)
            return ArtworkPresentationMode.PairedEditionGradient;

        if (!string.IsNullOrWhiteSpace(backdropUrl) || !string.IsNullOrWhiteSpace(bannerUrl))
            return ArtworkPresentationMode.CinematicBackdrop;

        if (entityType is DetailEntityType.Person or DetailEntityType.MusicArtist && !string.IsNullOrWhiteSpace(portraitUrl))
            return ArtworkPresentationMode.PortraitEcho;

        if (!string.IsNullOrWhiteSpace(coverUrl) || !string.IsNullOrWhiteSpace(posterUrl))
            return ArtworkPresentationMode.ColorGradientFromArtwork;

        if (relatedArtworkCount > 1)
            return ArtworkPresentationMode.CollageGradient;

        return ArtworkPresentationMode.GeneratedIdentity;
    }

    private async Task<DetailPageViewModel?> BuildWorkAsync(
        Guid workId,
        DetailEntityType requestedType,
        DetailPresentationContext context,
        bool isAdminView,
        CancellationToken ct)
    {
        var detail = await _libraryItems.GetDetailAsync(workId, ct);
        if (detail is null)
            return null;

        var values = detail.CanonicalValues.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
        var entityType = requestedType == DetailEntityType.Work ? InferWorkEntityType(detail.MediaType, detail) : requestedType;
        var ownedFormats = await LoadOwnedFormatsAsync(workId, detail, ct);
        var artworkFallback = await LoadWorkArtworkFallbackAsync(workId, ct);
        var multiFormatState = ownedFormats.Count > 1
            ? MultiFormatState.MultipleFormatsSeparateProgress
            : MultiFormatState.SingleFormat;
        var ownedCoverUrls = ownedFormats
            .Select(f => f.CoverUrl)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Cast<string>()
            .ToList();
        var foregroundArtworkUrl = FirstNonBlank(
            detail.CoverUrl,
            GetValue(values, "cover_url"),
            GetValue(values, "cover"),
            GetValue(values, "poster_url"),
            GetValue(values, "poster"),
            artworkFallback.CoverUrl,
            artworkFallback.SquareUrl,
            ownedCoverUrls.FirstOrDefault());
        var backdropUrl = FirstNonBlank(
            detail.BackgroundUrl,
            detail.HeroUrl,
            artworkFallback.BackgroundUrl);
        var bannerUrl = FirstNonBlank(detail.BannerUrl, artworkFallback.BannerUrl);

        var artwork = BuildArtwork(
            entityType,
            backdropUrl,
            bannerUrl,
            foregroundArtworkUrl,
            foregroundArtworkUrl,
            null,
            values,
            ownedCoverUrls,
            ownedFormats.Count,
            detail.ArtworkSource);

        var contributors = await BuildWorkContributorsAsync(workId, detail, entityType, ct);
        var characters = BuildCharacterGroupsFromCast(contributors.CastCredits);
        var contributorGroups = await BuildContributorGroupsAsync(workId, detail, entityType, contributors.CastCredits, values, ct);
        var seriesPlacement = await BuildSeriesPlacementAsync(workId, detail, entityType, ct);
        var mediaGroups = await BuildWorkMediaGroupsAsync(workId, entityType, ct);
        var longDescription = ResolveLongDescription(detail.Description, values, entityType);
        var heroSummary = await BuildHeroSummaryAsync(detail.Tagline, longDescription, detail.WikidataQid, values, ct);

        return new DetailPageViewModel
        {
            Id = workId.ToString("D"),
            EntityType = entityType,
            PresentationContext = context,
            Title = entityType == DetailEntityType.TvEpisode
                ? FirstNonBlank(detail.EpisodeTitle, GetValue(values, MetadataFieldConstants.EpisodeTitle), detail.Title, detail.FileName, "Untitled")
                : FirstNonBlank(detail.Title, detail.EpisodeTitle, detail.FileName, "Untitled"),
            Subtitle = BuildSubtitle(detail, entityType, multiFormatState),
            Tagline = heroSummary,
            Description = longDescription,
            Artwork = artwork,
            HeroBrand = BuildHeroBrand(
                entityType,
                FirstNonBlank(GetValue(values, "network"), GetValue(values, "studio"), GetValue(values, "broadcaster")),
                FirstNonBlank(GetValue(values, "network_logo_url"), GetValue(values, "network_logo"), GetValue(values, "studio_logo_url"), GetValue(values, "broadcaster_logo_url"))),
            OwnedFormats = ownedFormats,
            MultiFormatState = multiFormatState,
            SyncCapability = BuildSyncCapability(workId, ownedFormats, multiFormatState),
            SeriesPlacement = seriesPlacement,
            Metadata = BuildMetadataPills(detail, entityType),
            PrimaryActions = BuildPrimaryActions(workId, entityType, context, ownedFormats),
            SecondaryActions = BuildSecondaryActions(workId, entityType, ownedFormats),
            OverflowActions = BuildOverflowActions(workId, entityType, isAdminView),
            ContributorGroups = contributorGroups,
            PreviewContributors = BuildPreviewContributors(entityType, contributorGroups),
            CharacterGroups = characters,
            PreviewCharacters = characters.SelectMany(g => g.Characters).Take(12).ToList(),
            RelationshipStrip = BuildRelationshipStrip(detail, seriesPlacement),
            Tabs = BuildTabs(entityType, context, isAdminView, seriesPlacement is not null),
            MediaGroups = mediaGroups,
            IdentityStatus = ResolveIdentityStatus(detail.WikidataQid, detail.Status, detail.Confidence),
            LibraryStatus = LibraryStatus.Owned,
            IsAdminView = isAdminView,
        };
    }

    private async Task<DetailPageViewModel?> BuildCollectionAsync(
        Guid collectionId,
        DetailEntityType entityType,
        DetailPresentationContext context,
        bool isAdminView,
        CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var rawRow = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(
            """
            SELECT c.id AS Id,
                   c.display_name AS DisplayName,
                   c.wikidata_qid AS WikidataQid,
                   (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = c.id AND key IN ('description', 'overview') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1) AS Description,
                   (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = c.id AND key = 'tagline' AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1) AS Tagline,
                   (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = c.id AND key IN ('cover_url', 'cover', 'poster_url', 'poster') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1) AS CoverUrl,
                   (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = c.id AND key IN ('background_url', 'background', 'hero_url', 'hero') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1) AS BackgroundUrl,
                   (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = c.id AND key IN ('banner_url', 'banner', 'hero_url', 'hero') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1) AS BannerUrl,
                   (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = c.id AND key IN ('logo_url', 'logo') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1) AS LogoUrl,
                   (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = c.id AND key IN ('network', 'studio', 'broadcaster') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1) AS HeroBrandLabel,
                   (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = c.id AND key IN ('network_logo_url', 'network_logo', 'studio_logo_url', 'broadcaster_logo_url') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1) AS HeroBrandImageUrl
            FROM collections c
            WHERE c.id = @collectionId
            LIMIT 1;
            """,
            new { collectionId = collectionId.ToString() },
            cancellationToken: ct));

        if (rawRow is null)
            return null;

        var row = new CollectionDetailRow(
            Guid.Parse(StringValue(rawRow.Id) ?? collectionId.ToString("D")),
            StringValue(rawRow.DisplayName),
            StringValue(rawRow.WikidataQid),
            StringValue(rawRow.Description),
            StringValue(rawRow.Tagline),
            StringValue(rawRow.CoverUrl),
            StringValue(rawRow.BackgroundUrl),
            StringValue(rawRow.BannerUrl),
            StringValue(rawRow.LogoUrl),
            StringValue(rawRow.HeroBrandLabel),
            StringValue(rawRow.HeroBrandImageUrl));

        var works = await LoadCollectionWorksAsync(collectionId, ct);
        if (entityType == DetailEntityType.Collection)
            entityType = InferCollectionEntityType(works);

        var relatedArt = works
            .SelectMany(w => new[] { w.BackgroundUrl, w.ArtworkUrl })
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        var collectionValues = await LoadCanonicalMapAsync(collectionId, ct);
        var rootValues = await LoadCollectionRootCanonicalMapAsync(
            collectionId,
            requireRootWithChildren: entityType is DetailEntityType.TvShow or DetailEntityType.MovieSeries or DetailEntityType.BookSeries,
            ct);
        var values = MergeCanonicalMaps(collectionValues, rootValues);
        var longDescription = FirstText(
            GetValue(values, MetadataFieldConstants.Description),
            GetValue(values, "overview"),
            GetValue(values, "plot_summary"),
            row.Description);
        var heroSummary = await BuildHeroSummaryAsync(row.Tagline, longDescription, row.WikidataQid, values, ct);
        var fallbackBackdrop = works.Select(w => w.BackgroundUrl).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
        var fallbackCover = works.Select(w => w.ArtworkUrl).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
        var collectionBackdrop = FirstNonBlank(
            row.BackgroundUrl,
            GetValue(values, "background_url"),
            GetValue(values, "background"),
            GetValue(values, "hero_url"),
            GetValue(values, "hero"),
            fallbackBackdrop);
        var collectionBanner = FirstNonBlank(
            row.BannerUrl,
            GetValue(values, "banner_url"),
            GetValue(values, "banner"));
        var collectionCover = FirstNonBlank(
            row.CoverUrl,
            GetValue(values, "cover_url"),
            GetValue(values, "cover"),
            GetValue(values, "poster_url"),
            GetValue(values, "poster"),
            fallbackCover);
        var collectionLogo = FirstNonBlank(row.LogoUrl, GetValue(values, "logo_url"), GetValue(values, "logo"));
        var contributorGroups = await BuildCollectionCreditsAsync(collectionId, works, entityType, ct);
        var characterGroups = await BuildCollectionCharactersAsync(collectionId, row.WikidataQid, ct);
        var artwork = BuildArtwork(
            entityType,
            collectionBackdrop,
            collectionBanner,
            collectionCover,
            collectionCover,
            null,
            values,
            relatedArt,
            0,
            null,
            collectionLogo);

        return new DetailPageViewModel
        {
            Id = collectionId.ToString("D"),
            EntityType = entityType,
            PresentationContext = context,
            Title = FirstNonBlank(row.DisplayName, "Collection"),
            Subtitle = BuildCollectionSubtitle(entityType, works),
            Tagline = heroSummary,
            Description = longDescription,
            Artwork = artwork,
            HeroBrand = BuildHeroBrand(entityType, row.HeroBrandLabel, row.HeroBrandImageUrl),
            Metadata = BuildCollectionMetadata(entityType, works),
            PrimaryActions = BuildCollectionActions(collectionId, entityType, context),
            SecondaryActions = BuildSecondaryActions(collectionId, entityType),
            OverflowActions = BuildOverflowActions(collectionId, entityType, isAdminView),
            ContributorGroups = contributorGroups,
            PreviewContributors = BuildPreviewContributors(entityType, contributorGroups),
            CharacterGroups = characterGroups,
            PreviewCharacters = characterGroups.SelectMany(g => g.Characters).Take(12).ToList(),
            RelationshipStrip = BuildCollectionRelationships(row, entityType),
            Tabs = BuildTabs(entityType, context, isAdminView),
            MediaGroups = BuildCollectionMediaGroups(entityType, works),
            IdentityStatus = ResolveIdentityStatus(row.WikidataQid, null, null),
            LibraryStatus = LibraryStatus.Owned,
            IsAdminView = isAdminView,
        };
    }

    private async Task<DetailPageViewModel?> BuildPersonAsync(
        Guid personId,
        DetailEntityType entityType,
        DetailPresentationContext context,
        bool isAdminView,
        CancellationToken ct)
    {
        var person = await _persons.FindByIdAsync(personId, ct);
        if (person is null)
            return null;

        var credits = await PersonCreditQueries.GetLibraryCreditsAsync(personId, _db, ct);
        var characterRoles = await PersonCreditQueries.GetCharacterRolesAsync(personId, _db, ct);
        var artworkAssets = await _entityAssets.GetByEntityAsync(personId.ToString(), null, ct);
        var banner = PreferredAssetUrl(artworkAssets, "Banner");
        var background = PreferredAssetUrl(artworkAssets, "Background");
        var logo = PreferredAssetUrl(artworkAssets, "Logo");
        var portrait = ApiImageUrls.BuildPersonHeadshotUrl(person.Id, person.LocalHeadshotPath, person.HeadshotUrl);
        var relatedArt = credits.Select(c => c.CoverUrl).Where(url => !string.IsNullOrWhiteSpace(url)).Cast<string>().Take(8).ToList();
        var groups = BuildPersonCreditGroups(credits, context);

        return new DetailPageViewModel
        {
            Id = personId.ToString("D"),
            EntityType = entityType,
            PresentationContext = context,
            Title = person.Name,
            Subtitle = person.IsGroup ? "Group" : string.Join(" • ", person.Roles.Take(3)),
            Description = person.Biography,
            Artwork = BuildArtwork(entityType, background, banner, null, null, portrait, new Dictionary<string, string>(), relatedArt, 0, null, logo),
            Metadata = BuildPersonMetadata(person.Roles, credits.Count),
            PrimaryActions = BuildPersonActions(personId, entityType, context),
            SecondaryActions = [],
            OverflowActions = BuildOverflowActions(personId, entityType, isAdminView),
            ContributorGroups = groups,
            PreviewContributors = groups.SelectMany(g => g.Credits).Take(12).ToList(),
            CharacterGroups = BuildPersonCharacterGroups(characterRoles),
            PreviewCharacters = BuildPersonCharacterGroups(characterRoles).SelectMany(g => g.Characters).Take(12).ToList(),
            Tabs = BuildTabs(entityType, context, isAdminView),
            MediaGroups = BuildPersonMediaGroups(credits, context),
            IdentityStatus = ResolveIdentityStatus(person.WikidataQid, null, null),
            LibraryStatus = credits.Count > 0 ? LibraryStatus.Owned : LibraryStatus.Unknown,
            IsAdminView = isAdminView,
        };
    }

    private async Task<DetailPageViewModel?> BuildCharacterAsync(Guid characterId, DetailPresentationContext context, bool isAdminView, CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<CharacterDetailRow>(new CommandDefinition(
            """
            SELECT id AS Id,
                   label AS Label,
                   wikidata_qid AS WikidataQid,
                   fictional_universe_qid AS UniverseQid,
                   fictional_universe_label AS UniverseLabel,
                   image_url AS ImageUrl,
                   entity_sub_type AS EntitySubType
            FROM fictional_entities
            WHERE id = @id
            LIMIT 1;
            """,
            new { id = characterId.ToString() },
            cancellationToken: ct));

        if (row is null)
            return null;

        var portraits = await conn.QueryAsync<CharacterPortraitRow>(new CommandDefinition(
            """
            SELECT id AS Id,
                   image_url AS ImageUrl,
                   local_image_path AS LocalImagePath,
                   is_default AS IsDefault
            FROM character_portraits
            WHERE fictional_entity_id = @id
            ORDER BY is_default DESC, updated_at DESC
            LIMIT 1;
            """,
            new { id = characterId.ToString() },
            cancellationToken: ct));

        var portrait = portraits.Select(p => ApiImageUrls.BuildCharacterPortraitUrl(p.Id, p.LocalImagePath, p.ImageUrl)).FirstOrDefault();
        var artwork = BuildArtwork(DetailEntityType.Character, null, null, null, null, portrait ?? row.ImageUrl, new Dictionary<string, string>(), [], 0, null);

        return new DetailPageViewModel
        {
            Id = characterId.ToString("D"),
            EntityType = DetailEntityType.Character,
            PresentationContext = context,
            Title = row.Label,
            Subtitle = FirstNonBlank(row.EntitySubType, "Character") + (string.IsNullOrWhiteSpace(row.UniverseLabel) ? "" : $" • {row.UniverseLabel}"),
            Artwork = artwork,
            Metadata = [new MetadataPill { Label = "Character" }, .. MaybePill(row.UniverseLabel)],
            PrimaryActions = [new DetailAction { Key = "appearances", Label = "View Appearances", Icon = "auto_stories", IsPrimary = true }],
            OverflowActions = BuildOverflowActions(characterId, DetailEntityType.Character, isAdminView),
            RelationshipStrip = string.IsNullOrWhiteSpace(row.UniverseQid)
                ? []
                : [new RelationshipGroup { Title = "Universe", Items = [new RelatedEntityChip { Id = row.UniverseQid!, EntityType = RelatedEntityType.Universe, Label = row.UniverseLabel ?? row.UniverseQid! }] }],
            Tabs = BuildTabs(DetailEntityType.Character, context, isAdminView),
            IdentityStatus = ResolveIdentityStatus(row.WikidataQid, null, null),
            LibraryStatus = LibraryStatus.Owned,
            IsAdminView = isAdminView,
        };
    }

    private async Task<DetailPageViewModel?> BuildUniverseAsync(Guid id, DetailPresentationContext context, bool isAdminView, CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<CollectionDetailRow>(new CommandDefinition(
            """
            SELECT id AS Id,
                   display_name AS DisplayName,
                   wikidata_qid AS WikidataQid,
                   (SELECT value FROM canonical_values WHERE entity_id = collections.id AND key IN ('description', 'overview') LIMIT 1) AS Description,
                   (SELECT value FROM canonical_values WHERE entity_id = collections.id AND key = 'tagline' LIMIT 1) AS Tagline,
                   (SELECT value FROM canonical_values WHERE entity_id = collections.id AND key IN ('background_url', 'background', 'hero_url', 'hero') LIMIT 1) AS BackgroundUrl,
                   (SELECT value FROM canonical_values WHERE entity_id = collections.id AND key IN ('banner_url', 'banner', 'hero_url', 'hero') LIMIT 1) AS BannerUrl,
                   (SELECT value FROM canonical_values WHERE entity_id = collections.id AND key IN ('cover_url', 'cover') LIMIT 1) AS CoverUrl,
                   (SELECT value FROM canonical_values WHERE entity_id = collections.id AND key IN ('logo_url', 'logo') LIMIT 1) AS LogoUrl,
                   NULL AS HeroBrandLabel,
                   NULL AS HeroBrandImageUrl
            FROM collections
            WHERE id = @id
            LIMIT 1;
            """,
            new { id = id.ToString() },
            cancellationToken: ct));

        if (row is null)
            return null;

        var works = await LoadCollectionWorksAsync(id, ct);
        var relatedArt = works.Select(w => w.ArtworkUrl).Where(url => !string.IsNullOrWhiteSpace(url)).Cast<string>().Take(10).ToList();
        var characterGroups = await BuildCollectionCharactersAsync(id, row.WikidataQid, ct);
        var contributorGroups = await BuildUniverseCastGroupsAsync(row.WikidataQid, ct);
        var relationships = await BuildUniverseRelationshipGroupsAsync(row.WikidataQid, ct);

        return new DetailPageViewModel
        {
            Id = id.ToString("D"),
            EntityType = DetailEntityType.Universe,
            PresentationContext = context,
            Title = row.DisplayName ?? "Universe",
            Subtitle = $"Universe • {works.Count} item{(works.Count == 1 ? "" : "s")} in library",
            Description = row.Description,
            Artwork = BuildArtwork(DetailEntityType.Universe, row.BackgroundUrl, row.BannerUrl, row.CoverUrl, row.CoverUrl, null, new Dictionary<string, string>(), relatedArt, 0, null),
            Metadata = [new MetadataPill { Label = "Universe" }, new MetadataPill { Label = $"{works.Count} items" }],
            PrimaryActions = [new DetailAction { Key = "timeline", Label = "Explore Timeline", Icon = "account_tree", Route = string.IsNullOrWhiteSpace(row.WikidataQid) ? null : $"/universe/{row.WikidataQid}/explore", IsPrimary = true }],
            OverflowActions = BuildOverflowActions(id, DetailEntityType.Universe, isAdminView),
            ContributorGroups = contributorGroups,
            PreviewContributors = BuildPreviewContributors(DetailEntityType.Universe, contributorGroups),
            CharacterGroups = characterGroups,
            PreviewCharacters = characterGroups.SelectMany(g => g.Characters).Take(12).ToList(),
            RelationshipStrip = relationships,
            Tabs = BuildTabs(DetailEntityType.Universe, context, isAdminView),
            MediaGroups = BuildCollectionMediaGroups(DetailEntityType.Universe, works),
            IdentityStatus = ResolveIdentityStatus(row.WikidataQid, null, null),
            LibraryStatus = LibraryStatus.Owned,
            IsAdminView = isAdminView,
        };
    }

    private async Task<IReadOnlyList<OwnedFormatViewModel>> LoadOwnedFormatsAsync(Guid workId, LibraryItemDetail detail, CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync<OwnedFormatRow>(new CommandDefinition(
            """
            SELECT e.id AS EditionId,
                   e.format_label AS FormatLabel,
                   ma.id AS AssetId,
                   ma.file_path_root AS FilePathRoot,
                   (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key IN ('cover_url', 'cover') LIMIT 1) AS AssetCoverUrl,
                   (SELECT value FROM canonical_values WHERE entity_id = e.id AND key IN ('cover_url', 'cover') LIMIT 1) AS EditionCoverUrl,
                   (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key = 'runtime' LIMIT 1) AS Runtime,
                   (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key = 'page_count' LIMIT 1) AS PageCount,
                   (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key = 'narrator' LIMIT 1) AS Narrator
            FROM editions e
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            WHERE e.work_id = @workId
              AND ma.status = 'Normal'
            ORDER BY COALESCE(e.format_label, ''), ma.file_path_root;
            """,
            new { workId = workId.ToString() },
            cancellationToken: ct))).ToList();

        if (rows.Count == 0)
        {
            return
            [
                new OwnedFormatViewModel
                {
                    Id = workId.ToString("D"),
                    FormatType = ToFormatType(detail.MediaType, null),
                    DisplayName = ToFormatDisplay(detail.MediaType, null),
                    CoverUrl = detail.CoverUrl,
                    Runtime = detail.Runtime,
                    PrimaryContributor = detail.Narrator ?? detail.Author ?? detail.Director,
                    Actions = BuildFormatActions(workId, ToFormatType(detail.MediaType, null)),
                }
            ];
        }

        return rows.Select(row =>
        {
            var format = ToFormatType(detail.MediaType, row.FormatLabel ?? Path.GetExtension(row.FilePathRoot));
            return new OwnedFormatViewModel
            {
                Id = row.EditionId.ToString("D"),
                FormatType = format,
                DisplayName = ToFormatDisplay(detail.MediaType, row.FormatLabel),
                CoverUrl = FirstNonBlank(row.AssetCoverUrl, row.EditionCoverUrl, detail.CoverUrl),
                PrimaryContributor = row.Narrator ?? detail.Narrator ?? detail.Author ?? detail.Director,
                FileFormat = Path.GetExtension(row.FilePathRoot)?.TrimStart('.').ToUpperInvariant(),
                Runtime = row.Runtime ?? detail.Runtime,
                PageCount = int.TryParse(row.PageCount, out var pages) ? pages : null,
                Actions = BuildFormatActions(workId, format),
            };
        }).ToList();
    }

    private async Task<WorkContributorResult> BuildWorkContributorsAsync(Guid workId, LibraryItemDetail detail, DetailEntityType entityType, CancellationToken ct)
    {
        var cast = entityType is DetailEntityType.Movie or DetailEntityType.TvEpisode or DetailEntityType.TvShow
            ? await CastCreditQueries.BuildForWorkAsync(workId, _canonicalArrays, _persons, _db, ct)
            : [];

        return new WorkContributorResult(cast);
    }

    private async Task<IReadOnlyList<CreditGroupViewModel>> BuildContributorGroupsAsync(
        Guid workId,
        LibraryItemDetail detail,
        DetailEntityType entityType,
        IReadOnlyList<MediaEngine.Api.Models.CastCreditDto> cast,
        IReadOnlyDictionary<string, string> canonicalValues,
        CancellationToken ct)
    {
        var groups = new List<CreditGroupViewModel>();
        async Task AddTextCreditAsync(string title, CreditGroupType type, string? value, string role, string canonicalArrayKey)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var keyedEntries = await _canonicalArrays.GetValuesAsync(workId, canonicalArrayKey, ct);
            var credits = new List<EntityCreditViewModel>();
            foreach (var (name, index) in SplitNames(value).Select((name, index) => (name, index)))
            {
                var qid = keyedEntries.FirstOrDefault(entry => entry.Value.Equals(name, StringComparison.OrdinalIgnoreCase))?.ValueQid;
                qid ??= GetValue(canonicalValues, $"{canonicalArrayKey}_qid");
                var person = string.IsNullOrWhiteSpace(qid) ? null : await _persons.FindByQidAsync(qid, ct);
                person ??= await _persons.FindByNameAsync(name, ct);
                var imageUrl = person is null
                    ? FirstNonBlank(
                        GetValue(canonicalValues, $"{canonicalArrayKey}_headshot_url"),
                        GetValue(canonicalValues, $"{canonicalArrayKey}_image_url"),
                        GetValue(canonicalValues, $"{canonicalArrayKey}_photo_url"),
                        SplitNames(value).Count == 1 ? GetValue(canonicalValues, "headshot_url") : null)
                    : ApiImageUrls.BuildPersonHeadshotUrl(person.Id, person.LocalHeadshotPath, person.HeadshotUrl);
                credits.Add(new EntityCreditViewModel
                {
                    EntityId = person?.Id.ToString("D") ?? name,
                    EntityType = RelatedEntityType.Person,
                    DisplayName = person?.Name ?? name,
                    ImageUrl = imageUrl,
                    FallbackInitials = Initials(person?.Name ?? name),
                    PrimaryRole = role,
                    SortOrder = index,
                    IsPrimary = index == 0,
                    IsCanonical = !string.IsNullOrWhiteSpace(person?.WikidataQid),
                });
            }

            groups.Add(new CreditGroupViewModel
            {
                Title = title,
                GroupType = type,
                Credits = credits,
            });
        }

        await AddTextCreditAsync("Authors", CreditGroupType.Authors, detail.Author, "Author", "author");
        await AddTextCreditAsync("Narrators", CreditGroupType.Narrators, detail.Narrator, "Narrator", "narrator");
        await AddTextCreditAsync("Directors", CreditGroupType.Directors, detail.Director, "Director", "director");
        await AddTextCreditAsync("Writers", CreditGroupType.Writers, detail.Writer, "Writer", "writer");

        if (cast.Count > 0)
        {
            groups.Add(new CreditGroupViewModel
            {
                Title = "Cast",
                GroupType = CreditGroupType.Cast,
                Credits = cast.Select((credit, index) => new EntityCreditViewModel
                {
                    EntityId = (credit.PersonId ?? Guid.Empty).ToString("D"),
                    EntityType = RelatedEntityType.Person,
                    DisplayName = credit.Name,
                    ImageUrl = credit.HeadshotUrl,
                    FallbackInitials = Initials(credit.Name),
                    PrimaryRole = "Actor",
                    CharacterName = credit.Characters.FirstOrDefault()?.CharacterName,
                    CharacterEntityId = credit.Characters.FirstOrDefault()?.FictionalEntityId.ToString("D"),
                    CharacterImageUrl = credit.Characters.FirstOrDefault()?.PortraitUrl,
                    SortOrder = index,
                    IsPrimary = index < 5,
                    IsCanonical = !string.IsNullOrWhiteSpace(credit.WikidataQid),
                }).ToList(),
            });
        }

        return groups;
    }

    private static IReadOnlyList<CharacterGroupViewModel> BuildCharacterGroupsFromCast(IReadOnlyList<MediaEngine.Api.Models.CastCreditDto> cast)
    {
        var characters = cast
            .SelectMany(c => c.Characters.Select(character => new EntityCreditViewModel
            {
                EntityId = character.FictionalEntityId.ToString("D"),
                EntityType = RelatedEntityType.Character,
                DisplayName = character.CharacterName ?? "Character",
                ImageUrl = character.PortraitUrl,
                FallbackInitials = Initials(character.CharacterName ?? "Character"),
                PrimaryRole = "Character",
                IsCanonical = !string.IsNullOrWhiteSpace(character.CharacterQid),
            }))
            .Where(c => !string.IsNullOrWhiteSpace(c.DisplayName))
            .DistinctBy(c => c.EntityId)
            .ToList();

        return characters.Count == 0
            ? []
            : [new CharacterGroupViewModel { Title = "Characters", GroupType = CharacterGroupType.MainCharacters, Characters = characters }];
    }

    private async Task<SeriesPlacementViewModel?> BuildSeriesPlacementAsync(Guid workId, LibraryItemDetail detail, DetailEntityType entityType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(detail.Series))
            return null;

        using var conn = _db.CreateConnection();
        var rawRows = await conn.QueryAsync(new CommandDefinition(
            """
            WITH current_lineage AS (
                SELECT COALESCE(current_grandparent.id, current_parent.id, current_work.id) AS RootWorkId
                FROM works current_work
                LEFT JOIN works current_parent ON current_parent.id = current_work.parent_work_id
                LEFT JOIN works current_grandparent ON current_grandparent.id = current_parent.parent_work_id
                WHERE current_work.id = @workId
            )
            SELECT CAST(w.id AS TEXT) AS WorkId,
                   CAST(COALESCE(
                       (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key IN ('title', 'episode_title') LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = w.id AND key = 'title' LIMIT 1),
                       'Untitled') AS TEXT) AS Title,
                   CAST(w.media_type AS TEXT) AS MediaType,
                   CAST(COALESCE(
                       (SELECT claim_value FROM metadata_claims WHERE entity_id = ma.id AND claim_key = 'series_position' AND provider_id = @wikidataProviderId ORDER BY confidence DESC, claimed_at DESC LIMIT 1),
                       (SELECT claim_value FROM metadata_claims WHERE entity_id = w.id AND claim_key = 'series_position' AND provider_id = @wikidataProviderId ORDER BY confidence DESC, claimed_at DESC LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key IN ('series_position', 'issue_number') LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = w.id AND key IN ('series_position', 'ordinal') LIMIT 1),
                       CASE WHEN w.ordinal IS NOT NULL THEN CAST(w.ordinal AS TEXT) END) AS TEXT) AS PositionLabel,
                   CAST(COALESCE(
                       (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key IN ('cover_url', 'cover') LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = w.id AND key IN ('cover_url', 'cover') LIMIT 1)) AS TEXT) AS ArtworkUrl
            FROM works w
            LEFT JOIN works parent ON parent.id = w.parent_work_id
            LEFT JOIN works grandparent ON grandparent.id = parent.parent_work_id
            LEFT JOIN editions e ON e.work_id = w.id
            LEFT JOIN media_assets ma ON ma.edition_id = e.id
            CROSS JOIN current_lineage current
            WHERE NOT EXISTS (SELECT 1 FROM works child WHERE child.parent_work_id = w.id)
              AND (
                    COALESCE(grandparent.id, parent.id, w.id) = current.RootWorkId
                 OR COALESCE(
                       (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key = 'series' LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = w.id AND key = 'series' LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = COALESCE(grandparent.id, parent.id, w.id) AND key = 'series' LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = COALESCE(grandparent.id, parent.id, w.id) AND key = 'title' LIMIT 1)
                    ) = @series
              )
              AND (
                    @mediaFilter = 'Other'
                 OR (@mediaFilter = 'Read' AND w.media_type IN ('Books', 'Book', 'Ebook', 'Comics', 'Comic'))
                 OR (@mediaFilter = 'Listen' AND w.media_type IN ('Audiobooks', 'Audiobook', 'Audio'))
                 OR (@mediaFilter = 'Watch' AND w.media_type IN ('Movies', 'Movie', 'TV', 'Television'))
                 OR (@mediaFilter = 'Music' AND w.media_type IN ('Music', 'MusicAlbum'))
              )
            GROUP BY w.id
            """,
            new
            {
                workId = workId.ToString("D"),
                series = detail.Series,
                mediaFilter = SeriesMediaFilter(entityType, detail.MediaType),
                wikidataProviderId = WellKnownProviders.Wikidata.ToString(),
            },
            cancellationToken: ct));
        var rows = rawRows.Select(row => new SeriesRow(
            StringValue(row.WorkId) ?? string.Empty,
            StringValue(row.Title) ?? "Untitled",
            StringValue(row.MediaType),
            StringValue(row.PositionLabel),
            StringValue(row.ArtworkUrl))).ToList();
        var seriesQid = ExtractQid(detail.CanonicalValues.FirstOrDefault(c =>
            string.Equals(c.Key, "series_qid", StringComparison.OrdinalIgnoreCase))?.Value);

        var items = rows.Select(row =>
        {
            var rowWorkId = Guid.TryParse(row.WorkId, out var parsed) ? parsed : Guid.Empty;
            var positionNumber = TryParseSeriesPosition(row.PositionLabel);
            return new SeriesItemViewModel
            {
                Id = row.WorkId,
                EntityType = entityType,
                Title = row.Title,
                ArtworkUrl = row.ArtworkUrl,
                PositionNumber = positionNumber,
                PositionLabel = positionNumber?.ToString(CultureInfo.InvariantCulture) ?? row.PositionLabel,
                IsCurrent = rowWorkId == workId,
                IsOwned = true,
                ProgressState = LibraryProgressState.Unknown,
            };
        }).ToList();
        items = await MergeSeriesMemberPlaceholdersAsync(items, seriesQid, entityType, ct);
        items = AddMissingSeriesPlaceholders(items, entityType);
        items = SortSeriesItems(items);

        if (items.Count == 0)
        {
            items.Add(new SeriesItemViewModel
            {
                Id = workId.ToString("D"),
                EntityType = entityType,
                Title = detail.Title,
                ArtworkUrl = detail.CoverUrl,
                PositionLabel = detail.SeriesPosition,
                PositionNumber = TryParseSeriesPosition(detail.SeriesPosition),
                IsCurrent = true,
                IsOwned = true,
            });
        }

        var currentIndex = Math.Max(0, items.FindIndex(i => i.IsCurrent));
        var current = items[currentIndex];
        return new SeriesPlacementViewModel
        {
            SeriesId = detail.Series,
            SeriesTitle = detail.Series,
            UniverseId = detail.UniverseSummary?.UniverseQid,
            UniverseTitle = detail.UniverseSummary?.UniverseName,
            PositionNumber = current.PositionNumber,
            TotalKnownItems = items.Count,
            PositionLabel = BuildSeriesPositionLabel(entityType, current.PositionNumber, items.Count, detail.Series),
            OrderingType = entityType is DetailEntityType.ComicIssue ? SeriesOrderingType.IssueNumber : SeriesOrderingType.LibraryOrder,
            PreviousItem = currentIndex > 0 ? items[currentIndex - 1] : null,
            CurrentItem = current,
            NextItem = currentIndex < items.Count - 1 ? items[currentIndex + 1] : null,
            OrderedItems = items,
        };
    }

    private async Task<IReadOnlyList<MediaGroupingViewModel>> BuildWorkMediaGroupsAsync(Guid workId, DetailEntityType entityType, CancellationToken ct)
    {
        if (entityType is not DetailEntityType.TvEpisode)
            return [];

        return [];
    }

    private static string SeriesMediaFilter(DetailEntityType entityType, string mediaType)
        => entityType switch
        {
            DetailEntityType.Book or DetailEntityType.ComicIssue or DetailEntityType.Work when mediaType.Contains("book", StringComparison.OrdinalIgnoreCase)
                || mediaType.Contains("comic", StringComparison.OrdinalIgnoreCase)
                || mediaType.Equals("Books", StringComparison.OrdinalIgnoreCase)
                || mediaType.Equals("Comics", StringComparison.OrdinalIgnoreCase) => "Read",
            DetailEntityType.Audiobook => "Listen",
            DetailEntityType.Movie or DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode => "Watch",
            DetailEntityType.MusicAlbum or DetailEntityType.MusicTrack => "Music",
            _ when mediaType.Contains("audio", StringComparison.OrdinalIgnoreCase) => "Listen",
            _ when mediaType.Contains("movie", StringComparison.OrdinalIgnoreCase) || mediaType.Equals("TV", StringComparison.OrdinalIgnoreCase) => "Watch",
            _ => "Other",
        };

    private async Task<List<SeriesItemViewModel>> MergeSeriesMemberPlaceholdersAsync(
        IReadOnlyList<SeriesItemViewModel> items,
        string? seriesQid,
        DetailEntityType entityType,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(seriesQid))
            return items.ToList();

        using var conn = _db.CreateConnection();
        var rawMembers = await conn.QueryAsync(new CommandDefinition(
            """
            SELECT work_qid AS WorkQid,
                   work_label AS WorkLabel,
                   position AS Position
            FROM series_members
            WHERE series_qid = @seriesQid
            ORDER BY CAST(position AS REAL), work_label;
            """,
            new { seriesQid },
            cancellationToken: ct));

        var merged = items.ToList();
        var ownedPositions = merged
            .Where(item => item.PositionNumber.HasValue)
            .Select(item => item.PositionNumber!.Value)
            .ToHashSet();

        foreach (var member in rawMembers)
        {
            var position = TryParseSeriesPosition(StringValue(member.Position));
            if (!position.HasValue || ownedPositions.Contains(position.Value))
                continue;

            merged.Add(new SeriesItemViewModel
            {
                Id = $"missing-{seriesQid}-{position.Value}",
                EntityType = entityType,
                Title = FirstNonBlank(StringValue(member.WorkLabel), $"Book {position.Value}"),
                PositionNumber = position,
                PositionLabel = position.Value.ToString(CultureInfo.InvariantCulture),
                IsOwned = false,
                ProgressState = LibraryProgressState.Unknown,
            });
        }

        return merged
            .OrderBy(item => item.PositionNumber ?? int.MaxValue)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<SeriesItemViewModel> AddMissingSeriesPlaceholders(
        IReadOnlyList<SeriesItemViewModel> items,
        DetailEntityType entityType)
    {
        var numbered = items
            .Where(item => item.PositionNumber.HasValue && item.PositionNumber.Value > 0)
            .GroupBy(item => item.PositionNumber!.Value)
            .ToDictionary(group => group.Key, group => group.First());
        var unnumbered = items
            .Where(item => !item.PositionNumber.HasValue || item.PositionNumber.Value <= 0)
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (numbered.Count == 0)
            return unnumbered;

        var max = numbered.Keys.Max();
        var filled = new List<SeriesItemViewModel>(max);
        for (var position = 1; position <= max; position++)
        {
            if (numbered.TryGetValue(position, out var existing))
            {
                filled.Add(existing);
                continue;
            }

            filled.Add(new SeriesItemViewModel
            {
                Id = $"missing-{position}",
                EntityType = entityType,
                Title = "Missing from library",
                PositionNumber = position,
                PositionLabel = position.ToString(),
                IsOwned = false,
                ProgressState = LibraryProgressState.Unknown,
            });
        }

        filled.AddRange(unnumbered);
        return filled;
    }

    private static List<SeriesItemViewModel> SortSeriesItems(IEnumerable<SeriesItemViewModel> items)
        => items
            .OrderBy(item => item.PositionNumber ?? int.MaxValue)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private async Task<IReadOnlyList<CollectionWorkSummary>> LoadCollectionWorksAsync(Guid collectionId, CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var rawRows = await conn.QueryAsync(new CommandDefinition(
            """
            SELECT CAST(w.id AS TEXT) AS Id,
                   CAST(ma.id AS TEXT) AS AssetId,
                   CAST(w.media_type AS TEXT) AS MediaType,
                   w.ordinal AS Ordinal,
                   CAST(COALESCE(NULLIF(episode_title.value, ''), NULLIF(episode_title_work.value, ''), NULLIF(title_asset.value, ''), NULLIF(title_work.value, ''), 'Untitled') AS TEXT) AS Title,
                   CAST(COALESCE(
                       NULLIF(episode_desc_work.value, ''),
                       NULLIF(episode_desc_asset.value, ''),
                       (SELECT NULLIF(CAST(claim_value AS TEXT), '') FROM metadata_claims WHERE entity_id = w.id AND claim_key IN ('episode_description', 'episode_overview') AND NULLIF(CAST(claim_value AS TEXT), '') IS NOT NULL ORDER BY confidence DESC, claimed_at DESC LIMIT 1),
                       (SELECT NULLIF(CAST(claim_value AS TEXT), '') FROM metadata_claims WHERE entity_id = ma.id AND claim_key IN ('episode_description', 'episode_overview') AND NULLIF(CAST(claim_value AS TEXT), '') IS NOT NULL ORDER BY confidence DESC, claimed_at DESC LIMIT 1),
                       NULLIF(desc_asset.value, ''),
                       NULLIF(overview_asset.value, ''),
                       NULLIF(desc_work.value, ''),
                       NULLIF(overview_work.value, '')) AS TEXT) AS Description,
                   CAST(COALESCE(NULLIF(season.value, ''), '') AS TEXT) AS Season,
                   CAST(COALESCE(NULLIF(episode.value, ''), '') AS TEXT) AS Episode,
                   CAST(COALESCE(NULLIF(track.value, ''), '') AS TEXT) AS TrackNumber,
                   CAST(COALESCE(NULLIF(runtime.value, ''), NULLIF(duration.value, '')) AS TEXT) AS Duration,
                   CAST(COALESCE(NULLIF(year_asset.value, ''), NULLIF(year_work.value, '')) AS TEXT) AS Year,
                   CAST(COALESCE(NULLIF(cover_asset.value, ''), NULLIF(poster_asset.value, ''), NULLIF(cover_work.value, ''), NULLIF(poster_work.value, ''), NULLIF(cover_root.value, ''), NULLIF(poster_root.value, '')) AS TEXT) AS ArtworkUrl,
                   CAST(COALESCE(NULLIF(still_asset.value, ''), NULLIF(still_work.value, ''), NULLIF(bg_asset.value, ''), NULLIF(bg_work.value, ''), NULLIF(hero_asset.value, ''), NULLIF(hero_work.value, ''), NULLIF(banner_asset.value, ''), NULLIF(banner_work.value, ''), NULLIF(bg_root.value, ''), NULLIF(hero_root.value, ''), NULLIF(banner_root.value, '')) AS TEXT) AS BackgroundUrl,
                   CAST(COALESCE(NULLIF(cover_state_asset.value, ''), NULLIF(cover_state_work.value, ''), NULLIF(cover_state_root.value, '')) AS TEXT) AS CoverState,
                   CAST(COALESCE(NULLIF(bg_state_asset.value, ''), NULLIF(bg_state_work.value, ''), NULLIF(hero_state_asset.value, ''), NULLIF(hero_state_work.value, ''), NULLIF(banner_state_asset.value, ''), NULLIF(banner_state_work.value, ''), NULLIF(bg_state_root.value, ''), NULLIF(hero_state_root.value, ''), NULLIF(banner_state_root.value, '')) AS TEXT) AS BackgroundState
            FROM works w
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            LEFT JOIN editions e ON e.work_id = w.id
            LEFT JOIN media_assets ma ON ma.edition_id = e.id
            LEFT JOIN canonical_values title_asset ON title_asset.entity_id = ma.id AND title_asset.key = 'title'
            LEFT JOIN canonical_values episode_title ON episode_title.entity_id = ma.id AND episode_title.key = 'episode_title'
            LEFT JOIN canonical_values episode_title_work ON episode_title_work.entity_id = w.id AND episode_title_work.key = 'episode_title'
            LEFT JOIN canonical_values title_work ON title_work.entity_id = w.id AND title_work.key = 'title'
            LEFT JOIN canonical_values desc_asset ON desc_asset.entity_id = ma.id AND desc_asset.key = 'description'
            LEFT JOIN canonical_values overview_asset ON overview_asset.entity_id = ma.id AND overview_asset.key = 'overview'
            LEFT JOIN canonical_values episode_desc_asset ON episode_desc_asset.entity_id = ma.id AND episode_desc_asset.key = 'episode_description'
            LEFT JOIN canonical_values desc_work ON desc_work.entity_id = w.id AND desc_work.key = 'description'
            LEFT JOIN canonical_values overview_work ON overview_work.entity_id = w.id AND overview_work.key = 'overview'
            LEFT JOIN canonical_values episode_desc_work ON episode_desc_work.entity_id = w.id AND episode_desc_work.key = 'episode_description'
            LEFT JOIN canonical_values season ON season.entity_id = ma.id AND season.key = 'season_number'
            LEFT JOIN canonical_values episode ON episode.entity_id = ma.id AND episode.key = 'episode_number'
            LEFT JOIN canonical_values track ON track.entity_id = ma.id AND track.key = 'track_number'
            LEFT JOIN canonical_values runtime ON runtime.entity_id = ma.id AND runtime.key = 'runtime'
            LEFT JOIN canonical_values duration ON duration.entity_id = ma.id AND duration.key = 'duration'
            LEFT JOIN canonical_values year_asset ON year_asset.entity_id = ma.id AND year_asset.key IN ('year', 'release_year')
            LEFT JOIN canonical_values year_work ON year_work.entity_id = w.id AND year_work.key IN ('year', 'release_year')
            LEFT JOIN canonical_values cover_asset ON cover_asset.entity_id = ma.id AND cover_asset.key IN ('cover_url', 'cover')
            LEFT JOIN canonical_values cover_work ON cover_work.entity_id = w.id AND cover_work.key IN ('cover_url', 'cover')
            LEFT JOIN canonical_values poster_asset ON poster_asset.entity_id = ma.id AND poster_asset.key IN ('poster_url', 'poster')
            LEFT JOIN canonical_values poster_work ON poster_work.entity_id = w.id AND poster_work.key IN ('poster_url', 'poster')
            LEFT JOIN canonical_values cover_root ON cover_root.entity_id = COALESCE(gp.id, p.id, w.id) AND cover_root.key IN ('cover_url', 'cover')
            LEFT JOIN canonical_values poster_root ON poster_root.entity_id = COALESCE(gp.id, p.id, w.id) AND poster_root.key IN ('poster_url', 'poster')
            LEFT JOIN canonical_values still_asset ON still_asset.entity_id = ma.id AND still_asset.key IN ('episode_still_url', 'episode_still')
            LEFT JOIN canonical_values still_work ON still_work.entity_id = w.id AND still_work.key IN ('episode_still_url', 'episode_still')
            LEFT JOIN canonical_values bg_asset ON bg_asset.entity_id = ma.id AND bg_asset.key IN ('background_url', 'background')
            LEFT JOIN canonical_values bg_work ON bg_work.entity_id = w.id AND bg_work.key IN ('background_url', 'background')
            LEFT JOIN canonical_values hero_asset ON hero_asset.entity_id = ma.id AND hero_asset.key IN ('hero_url', 'hero')
            LEFT JOIN canonical_values hero_work ON hero_work.entity_id = w.id AND hero_work.key IN ('hero_url', 'hero')
            LEFT JOIN canonical_values banner_asset ON banner_asset.entity_id = ma.id AND banner_asset.key IN ('banner_url', 'banner')
            LEFT JOIN canonical_values banner_work ON banner_work.entity_id = w.id AND banner_work.key IN ('banner_url', 'banner')
            LEFT JOIN canonical_values bg_root ON bg_root.entity_id = COALESCE(gp.id, p.id, w.id) AND bg_root.key IN ('background_url', 'background')
            LEFT JOIN canonical_values hero_root ON hero_root.entity_id = COALESCE(gp.id, p.id, w.id) AND hero_root.key IN ('hero_url', 'hero')
            LEFT JOIN canonical_values banner_root ON banner_root.entity_id = COALESCE(gp.id, p.id, w.id) AND banner_root.key IN ('banner_url', 'banner')
            LEFT JOIN canonical_values cover_state_asset ON cover_state_asset.entity_id = ma.id AND cover_state_asset.key = 'cover_state'
            LEFT JOIN canonical_values cover_state_work ON cover_state_work.entity_id = w.id AND cover_state_work.key = 'cover_state'
            LEFT JOIN canonical_values cover_state_root ON cover_state_root.entity_id = COALESCE(gp.id, p.id, w.id) AND cover_state_root.key = 'cover_state'
            LEFT JOIN canonical_values bg_state_asset ON bg_state_asset.entity_id = ma.id AND bg_state_asset.key = 'background_state'
            LEFT JOIN canonical_values bg_state_work ON bg_state_work.entity_id = w.id AND bg_state_work.key = 'background_state'
            LEFT JOIN canonical_values hero_state_asset ON hero_state_asset.entity_id = ma.id AND hero_state_asset.key = 'hero_state'
            LEFT JOIN canonical_values hero_state_work ON hero_state_work.entity_id = w.id AND hero_state_work.key = 'hero_state'
            LEFT JOIN canonical_values banner_state_asset ON banner_state_asset.entity_id = ma.id AND banner_state_asset.key = 'banner_state'
            LEFT JOIN canonical_values banner_state_work ON banner_state_work.entity_id = w.id AND banner_state_work.key = 'banner_state'
            LEFT JOIN canonical_values bg_state_root ON bg_state_root.entity_id = COALESCE(gp.id, p.id, w.id) AND bg_state_root.key = 'background_state'
            LEFT JOIN canonical_values hero_state_root ON hero_state_root.entity_id = COALESCE(gp.id, p.id, w.id) AND hero_state_root.key = 'hero_state'
            LEFT JOIN canonical_values banner_state_root ON banner_state_root.entity_id = COALESCE(gp.id, p.id, w.id) AND banner_state_root.key = 'banner_state'
            WHERE w.collection_id = @collectionId
            GROUP BY w.id
            ORDER BY CAST(NULLIF(Season, '') AS INTEGER), CAST(NULLIF(Episode, '') AS INTEGER), CAST(NULLIF(TrackNumber, '') AS INTEGER), COALESCE(w.ordinal, 9999), Title;
            """,
            new { collectionId = collectionId.ToString() },
            cancellationToken: ct));
        return rawRows.Select(row => new CollectionWorkSummary(
            StringValue(row.Id) ?? string.Empty,
            StringValue(row.MediaType) ?? string.Empty,
            IntValue(row.Ordinal),
            StringValue(row.Title) ?? "Untitled",
            StringValue(row.Description),
            StringValue(row.Season),
            StringValue(row.Episode),
            StringValue(row.TrackNumber),
            StringValue(row.Duration),
            StringValue(row.Year),
            ResolveCollectionArtworkUrl(StringValue(row.ArtworkUrl), StringValue(row.AssetId), "cover", StringValue(row.CoverState)),
            ResolveCollectionArtworkUrl(StringValue(row.BackgroundUrl), StringValue(row.AssetId), "background", StringValue(row.BackgroundState)))).ToList();
    }

    private async Task<Dictionary<string, string>> LoadCanonicalMapAsync(Guid entityId, CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<CanonicalPair>(new CommandDefinition(
            "SELECT key AS Key, value AS Value FROM canonical_values WHERE entity_id = @entityId;",
            new { entityId = entityId.ToString() },
            cancellationToken: ct));
        return rows.GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, string>> LoadCollectionRootCanonicalMapAsync(
        Guid collectionId,
        bool requireRootWithChildren,
        CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var rootId = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT COALESCE(gp.id, p.id, w.id)
            FROM works w
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            WHERE w.collection_id = @collectionId
              AND (
                    @requireRootWithChildren = 0
                 OR EXISTS (
                        SELECT 1
                        FROM works child
                        WHERE child.parent_work_id = COALESCE(gp.id, p.id, w.id)
                    )
              )
            ORDER BY COALESCE(w.ordinal, 9999), w.id
            LIMIT 1;
            """,
            new
            {
                collectionId = collectionId.ToString("D"),
                requireRootWithChildren = requireRootWithChildren ? 1 : 0,
            },
            cancellationToken: ct));

        return Guid.TryParse(rootId, out var rootGuid)
            ? await LoadCanonicalMapAsync(rootGuid, ct)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> MergeCanonicalMaps(
        IReadOnlyDictionary<string, string> primary,
        IReadOnlyDictionary<string, string> fallback)
    {
        var merged = new Dictionary<string, string>(fallback, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in primary)
            merged[key] = value;
        return merged;
    }

    private static string? ResolveLongDescription(
        string? detailDescription,
        IReadOnlyDictionary<string, string> canonicalValues,
        DetailEntityType entityType)
    {
        if (entityType == DetailEntityType.TvEpisode)
        {
            return FirstText(
                GetValue(canonicalValues, MetadataFieldConstants.EpisodeDescription),
                GetValue(canonicalValues, "episode_overview"),
                detailDescription,
                GetValue(canonicalValues, MetadataFieldConstants.Description),
                GetValue(canonicalValues, "overview"));
        }

        return FirstText(
            GetValue(canonicalValues, MetadataFieldConstants.Description),
            GetValue(canonicalValues, "overview"),
            GetValue(canonicalValues, "plot_summary"),
            detailDescription);
    }

    private Task<string?> BuildHeroSummaryAsync(
        string? tagline,
        string? description,
        string? wikidataQid,
        IReadOnlyDictionary<string, string> canonicalValues,
        CancellationToken ct)
    {
        // Current detail pages use the Wikipedia-backed description as display copy.
        // Retail taglines are still stored as claims but are not promoted into hero text.
        var canonicalSummary = FirstText(
            GetValue(canonicalValues, MetadataFieldConstants.ShortDescription),
            GetValue(canonicalValues, "wikidata_description"),
            GetValue(canonicalValues, "wikidata_summary"),
            GetValue(canonicalValues, "summary"));

        if (!string.IsNullOrWhiteSpace(canonicalSummary))
            return Task.FromResult(NormalizeHeroSummary(canonicalSummary));

        return Task.FromResult(BuildFallbackHeroSummary(description));
    }

    private async Task<WorkArtworkFallback> LoadWorkArtworkFallbackAsync(Guid workId, CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(
            """
            WITH ranked_assets AS (
                SELECT
                    w.id AS WorkId,
                    COALESCE(gp.id, p.id, w.id) AS RootWorkId,
                    ma.id AS AssetId,
                    ROW_NUMBER() OVER (
                        PARTITION BY w.id
                        ORDER BY CASE WHEN mc.claimed_at IS NULL THEN 1 ELSE 0 END, mc.claimed_at ASC, ma.id
                    ) AS AssetRank
                FROM works w
                INNER JOIN editions e ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                LEFT JOIN metadata_claims mc ON mc.entity_id = ma.id
                LEFT JOIN works p ON p.id = w.parent_work_id
                LEFT JOIN works gp ON gp.id = p.parent_work_id
                WHERE w.id = @workId
            )
            SELECT
                CAST(AssetId AS TEXT) AS AssetId,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('cover_url', 'cover', 'poster_url', 'poster') LIMIT 1) AS CoverUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('square_url', 'square') LIMIT 1) AS SquareUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('background_url', 'background') LIMIT 1) AS BackgroundUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('banner_url', 'banner') LIMIT 1) AS BannerUrl,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'cover_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'cover_state' LIMIT 1)) AS CoverState,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'square_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'square_state' LIMIT 1)) AS SquareState,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'background_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'background_state' LIMIT 1)) AS BackgroundState,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'banner_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'banner_state' LIMIT 1)) AS BannerState
            FROM ranked_assets
            WHERE AssetRank = 1
            LIMIT 1;
            """,
            new { workId = workId.ToString("D") },
            cancellationToken: ct));

        if (row is null)
            return new WorkArtworkFallback();

        var assetIdValue = StringValue(row.AssetId);
        if (!Guid.TryParse(assetIdValue, out Guid assetId))
            return new WorkArtworkFallback();

        return new WorkArtworkFallback
        {
            CoverUrl = DisplayArtworkUrlResolver.Resolve(StringValue(row.CoverUrl), assetId, "cover", StringValue(row.CoverState)),
            SquareUrl = DisplayArtworkUrlResolver.Resolve(StringValue(row.SquareUrl), assetId, "square", StringValue(row.SquareState)),
            BackgroundUrl = DisplayArtworkUrlResolver.Resolve(StringValue(row.BackgroundUrl), assetId, "background", StringValue(row.BackgroundState)),
            BannerUrl = DisplayArtworkUrlResolver.Resolve(StringValue(row.BannerUrl), assetId, "banner", StringValue(row.BannerState)),
        };
    }

    private static ArtworkSet BuildArtwork(
        DetailEntityType entityType,
        string? backdropUrl,
        string? bannerUrl,
        string? coverUrl,
        string? posterUrl,
        string? portraitUrl,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyList<string> relatedArtwork,
        int ownedFormatCount,
        string? artworkSource,
        string? logoUrl = null)
    {
        var primary = FirstNonBlank(GetValue(values, MetadataFieldConstants.ArtworkPrimaryHex), GetValue(values, "primary_color"), "#C9922E");
        var secondary = FirstNonBlank(GetValue(values, MetadataFieldConstants.ArtworkSecondaryHex), GetValue(values, "secondary_color"), "#271A3A");
        var accent = FirstNonBlank(GetValue(values, MetadataFieldConstants.ArtworkAccentHex), GetValue(values, "accent_color"), "#4F7DBA");
        var mode = ResolveArtworkPresentationMode(entityType, backdropUrl, bannerUrl, coverUrl, posterUrl, portraitUrl, relatedArtwork.Count, ownedFormatCount);
        var characterImageUrl = entityType == DetailEntityType.Character ? portraitUrl : null;
        var heroArtwork = HeroArtworkResolver.Resolve(entityType, backdropUrl, bannerUrl, coverUrl, posterUrl, portraitUrl, characterImageUrl, relatedArtwork);

        return new ArtworkSet
        {
            BackdropUrl = backdropUrl,
            BannerUrl = bannerUrl,
            PosterUrl = posterUrl,
            CoverUrl = coverUrl,
            LogoUrl = logoUrl ?? GetValue(values, "logo_url") ?? GetValue(values, "logo"),
            PortraitUrl = portraitUrl,
            CharacterImageUrl = characterImageUrl,
            RelatedArtworkUrls = relatedArtwork,
            DominantColors = [primary, secondary, accent],
            PrimaryColor = primary,
            SecondaryColor = secondary,
            AccentColor = accent,
            HeroArtwork = heroArtwork,
            PresentationMode = mode,
            Source = ResolveArtworkSource(artworkSource),
        };
    }

    private static IReadOnlyList<MetadataPill> BuildMetadataPills(LibraryItemDetail detail, DetailEntityType entityType)
    {
        var values = new List<MetadataPill>();
        AddPlain(values, FormatRating(detail.Rating), "rating");

        foreach (var genre in SplitMetadataValues(detail.Genre).Take(3))
        {
            values.Add(new MetadataPill
            {
                Label = genre,
                Kind = "genre",
                Route = $"/search?genre={Uri.EscapeDataString(genre)}",
                Tooltip = $"Browse {genre}",
            });
        }

        AddPlain(values, FormatEntityType(entityType), "type");
        AddPlain(values, detail.Year, "year");
        AddPlain(values, detail.Runtime, "duration");
        AddPlain(values, detail.Language, "audio");

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value.Label))
            .DistinctBy(value => value.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<DetailAction> BuildPrimaryActions(Guid id, DetailEntityType entityType, DetailPresentationContext context, IReadOnlyList<OwnedFormatViewModel> formats)
    {
        if (formats.Count > 1 && formats.Any(f => f.FormatType == MediaFormatType.Ebook) && formats.Any(f => f.FormatType == MediaFormatType.Audiobook))
        {
            return
            [
                new DetailAction { Key = "continue-reading", Label = "Continue Reading", Icon = "menu_book", Route = $"/book/{id}", IsPrimary = true },
                new DetailAction { Key = "continue-listening", Label = "Continue Listening", Icon = "headphones", Route = $"/listen/audiobook/{id}", IsPrimary = true },
            ];
        }

        return entityType switch
        {
            DetailEntityType.Movie => [new DetailAction { Key = "play", Label = "Play", Icon = "play_arrow", Route = $"/watch/player/resolve?workId={id}", IsPrimary = true }],
            DetailEntityType.TvShow => [new DetailAction { Key = "watch-latest", Label = "Watch Latest", Icon = "play_arrow", IsPrimary = true }],
            DetailEntityType.Book or DetailEntityType.ComicIssue => [new DetailAction { Key = "read", Label = "Read", Icon = "menu_book", Route = $"/book/{id}", IsPrimary = true }],
            DetailEntityType.Audiobook => [new DetailAction { Key = "listen", Label = "Listen", Icon = "headphones", Route = $"/listen/audiobook/{id}", IsPrimary = true }],
            DetailEntityType.MusicAlbum => [new DetailAction { Key = "play-album", Label = "Play Album", Icon = "play_arrow", IsPrimary = true }],
            DetailEntityType.MusicArtist => [new DetailAction { Key = "play-artist", Label = "Play Artist", Icon = "play_arrow", IsPrimary = true }],
            _ => [new DetailAction { Key = "open", Label = "Open", Icon = "open_in_new", IsPrimary = true }],
        };
    }

    private static IReadOnlyList<DetailAction> BuildSecondaryActions(Guid id, DetailEntityType entityType, IReadOnlyList<OwnedFormatViewModel>? formats = null)
    {
        var actions = new List<DetailAction>();

        if (entityType is DetailEntityType.Movie or DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode)
        {
            actions.Add(new DetailAction
            {
                Key = "watch-party",
                Label = "Watch Party",
                Icon = "groups",
                Tooltip = "Watch Party is coming soon",
                IsDisabled = true,
                IsStub = true,
                DisplayStyle = "icon",
            });
        }

        actions.Add(new DetailAction
        {
            Key = "add-to-collection",
            Label = "Add to collection",
            Icon = "add",
            Tooltip = "Add to collection",
            DisplayStyle = "icon",
        });

        actions.Add(new DetailAction
        {
            Key = "reaction-menu",
            Label = "Rate",
            Icon = "thumb_up",
            Tooltip = "Rate this item",
            DisplayStyle = "hover-menu",
            Children =
            [
                new DetailAction { Key = "like", Label = "Thumbs up", Icon = "thumb_up", Tooltip = "Thumbs up" },
                new DetailAction { Key = "dislike", Label = "Thumbs down", Icon = "thumb_down", Tooltip = "Thumbs down" },
            ],
        });

        if (formats?.Any(f => f.FormatType == MediaFormatType.Ebook) == true &&
            formats.Any(f => f.FormatType == MediaFormatType.Audiobook))
        {
            actions.Add(new DetailAction
            {
                Key = "format-tools",
                Label = "Read/listen tools",
                Icon = "headphones",
                Tooltip = "Read/listen tools",
                DisplayStyle = "hover-menu",
                Children =
                [
                    new DetailAction { Key = "sync-progress", Label = "Sync Progress", Icon = "sync", Tooltip = "Sync Progress (coming soon)", IsDisabled = true, IsStub = true },
                    new DetailAction { Key = "read-along", Label = "Read Along", Icon = "auto_stories", Tooltip = "Read Along (coming soon)", IsDisabled = true, IsStub = true },
                    new DetailAction { Key = "keep-separate", Label = "Keep Separate", Icon = "link", Tooltip = "Keep reading and listening progress separate", IsDisabled = true },
                    new DetailAction { Key = "format-settings", Label = "Format Settings", Icon = "settings", Tooltip = "Format Settings (coming soon)", IsDisabled = true, IsStub = true },
                ],
            });
        }

        return actions;
    }

    private static IReadOnlyList<DetailAction> BuildOverflowActions(Guid id, DetailEntityType entityType, bool isAdminView)
    {
        var actions = new List<DetailAction>
        {
            new() { Key = "details", Label = "Details", Icon = "info" },
            new() { Key = "sync-settings", Label = "Sync Settings", Icon = "sync", Tooltip = "Sync settings are coming soon", IsDisabled = true, IsStub = true },
            new() { Key = "manage-artwork", Label = "Manage Artwork", Icon = "image", IsAdminOnly = true },
            new() { Key = "refresh", Label = "Refresh Metadata", Icon = "sync", IsAdminOnly = true },
            new() { Key = "file-info", Label = "View File Info", Icon = "info", IsAdminOnly = true },
        };

        if (isAdminView)
        {
            actions.Add(new DetailAction { Key = "edit", Label = "Edit Details", Icon = "edit", IsAdminOnly = true });
            actions.Add(new DetailAction { Key = "delete", Label = "Delete from Library", Icon = "delete", IsAdminOnly = true, IsDestructive = true });
        }

        return actions.Where(a => !a.IsAdminOnly || isAdminView).ToList();
    }

    private static IReadOnlyList<DetailTab> BuildTabs(DetailEntityType entityType, DetailPresentationContext context, bool isAdminView, bool hasSeries = false)
    {
        string[] keys = entityType switch
        {
            DetailEntityType.TvShow => ["episodes", "overview", "people", "universe", "details"],
            DetailEntityType.TvSeason => ["episodes", "overview", "people", "details"],
            DetailEntityType.Movie when hasSeries => ["series", "overview", "people", "universe", "related", "details"],
            DetailEntityType.Movie => ["overview", "people", "universe", "related", "details"],
            DetailEntityType.Book or DetailEntityType.Audiobook when hasSeries => ["series", "overview", "chapters", "contributors", "characters", "universe", "editions", "details"],
            DetailEntityType.Book or DetailEntityType.Audiobook => ["overview", "chapters", "contributors", "characters", "series", "universe", "editions", "details"],
            DetailEntityType.Work when hasSeries => ["series", "overview", "formats", "chapters", "contributors", "characters", "universe", "editions", "details"],
            DetailEntityType.Work => ["overview", "formats", "chapters", "contributors", "characters", "series", "universe", "editions", "details"],
            DetailEntityType.ComicIssue when hasSeries => ["series", "overview", "contributors", "characters", "universe", "editions", "details"],
            DetailEntityType.ComicIssue => ["overview", "contributors", "characters", "series", "universe", "editions", "details"],
            DetailEntityType.MusicAlbum => ["tracks", "credits", "related", "details"],
            DetailEntityType.MusicArtist when context == DetailPresentationContext.Listen => ["overview", "albums", "tracks", "appears-on", "credits", "related", "details"],
            DetailEntityType.Person when context == DetailPresentationContext.Listen => ["overview", "albums", "tracks", "appears-on", "credits", "movies-tv", "details"],
            DetailEntityType.Person when context == DetailPresentationContext.Watch => ["overview", "movies-tv", "roles", "characters", "music", "collaborators", "details"],
            DetailEntityType.Person => ["overview", "works", "roles", "music", "movies-tv", "characters", "universes", "details"],
            DetailEntityType.Character => ["overview", "appearances", "portrayals", "relationships", "universe", "details"],
            DetailEntityType.Universe => ["overview", "timeline", "media", "characters", "people", "relationships", "details"],
            _ => ["overview", "people", "characters", "series", "universe", "related", "details"],
        };

        var tabs = keys.Select(key => new DetailTab { Key = key, Label = ToTabLabel(key) }).ToList();
        if (isAdminView)
            tabs.Add(new DetailTab { Key = "registry", Label = "Registry", IsAdminOnly = true });
        return tabs;
    }

    private static void AddPlain(List<MetadataPill> values, string? label, string kind)
    {
        if (!string.IsNullOrWhiteSpace(label))
            values.Add(new MetadataPill { Label = label, Kind = kind });
    }

    private static string? FormatRating(string? rating)
    {
        if (string.IsNullOrWhiteSpace(rating))
            return null;

        var trimmed = rating.Trim();
        return double.TryParse(trimmed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
            : trimmed;
    }

    private static IEnumerable<string> SplitMetadataValues(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        foreach (var part in value.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(part))
                yield return part;
        }
    }

    private static IReadOnlyList<DetailAction> BuildFormatActions(Guid workId, MediaFormatType format)
        => format switch
        {
            MediaFormatType.Ebook => [new DetailAction { Key = "read", Label = "Read", Icon = "menu_book", Route = $"/book/{workId}" }],
            MediaFormatType.Audiobook => [new DetailAction { Key = "listen", Label = "Listen", Icon = "headphones", Route = $"/listen/audiobook/{workId}" }],
            MediaFormatType.Movie => [new DetailAction { Key = "play", Label = "Play", Icon = "play_arrow" }],
            _ => [new DetailAction { Key = "open", Label = "Open", Icon = "open_in_new" }],
        };

    private static ReadingListeningSyncCapabilityViewModel? BuildSyncCapability(Guid workId, IReadOnlyList<OwnedFormatViewModel> formats, MultiFormatState state)
    {
        if (state == MultiFormatState.SingleFormat)
            return null;

        var ebook = formats.FirstOrDefault(f => f.FormatType == MediaFormatType.Ebook);
        var audio = formats.FirstOrDefault(f => f.FormatType == MediaFormatType.Audiobook);
        if (ebook is null || audio is null)
            return new ReadingListeningSyncCapabilityViewModel
            {
                State = SyncCapabilityState.NotApplicable,
                Reason = "Read + Listen Sync only applies when both ebook and audiobook formats are owned.",
            };

        // Read/listen alignment plugs in here. Until alignment confidence exists, multi-format works default to separate progress.
        return new ReadingListeningSyncCapabilityViewModel
        {
            State = SyncCapabilityState.NeedsReview,
            Reason = "Sync needs review. We could not confidently align this ebook and audiobook yet.",
            TextEditionId = ebook.Id,
            AudioEditionId = audio.Id,
            PreviewAction = new DetailAction { Key = "preview-sync", Label = "Preview Sync", Icon = "compare_arrows", Tooltip = "Preview Sync is coming soon", IsDisabled = true, IsStub = true },
            EnableAction = new DetailAction { Key = "enable-sync", Label = "Enable Sync", Icon = "link", Tooltip = "Sync Progress is coming soon", IsDisabled = true, IsStub = true },
        };
    }

    private static DetailEntityType InferWorkEntityType(string mediaType, LibraryItemDetail detail)
    {
        if (!string.IsNullOrWhiteSpace(detail.EpisodeNumber) || mediaType.Equals("TV", StringComparison.OrdinalIgnoreCase))
            return DetailEntityType.TvEpisode;
        if (mediaType.Contains("movie", StringComparison.OrdinalIgnoreCase))
            return DetailEntityType.Movie;
        if (mediaType.Contains("audio", StringComparison.OrdinalIgnoreCase))
            return DetailEntityType.Audiobook;
        if (mediaType.Contains("comic", StringComparison.OrdinalIgnoreCase) || mediaType.Equals("Cbz", StringComparison.OrdinalIgnoreCase))
            return DetailEntityType.ComicIssue;
        if (mediaType.Contains("music", StringComparison.OrdinalIgnoreCase))
            return DetailEntityType.MusicTrack;
        return DetailEntityType.Book;
    }

    private static DetailEntityType InferCollectionEntityType(IReadOnlyList<CollectionWorkSummary> works)
    {
        var mediaTypes = works.Select(w => w.MediaType).ToList();
        if (mediaTypes.Any(m => m.Contains("TV", StringComparison.OrdinalIgnoreCase)) || works.Any(w => !string.IsNullOrWhiteSpace(w.Season)))
            return DetailEntityType.TvShow;
        if (mediaTypes.Any(m => m.Contains("movie", StringComparison.OrdinalIgnoreCase)))
            return DetailEntityType.MovieSeries;
        if (mediaTypes.Any(m => m.Contains("music", StringComparison.OrdinalIgnoreCase)))
            return DetailEntityType.MusicAlbum;
        if (mediaTypes.Any(m => m.Contains("comic", StringComparison.OrdinalIgnoreCase)))
            return DetailEntityType.ComicSeries;
        return DetailEntityType.Collection;
    }

    private static MediaFormatType ToFormatType(string mediaType, string? formatLabel)
    {
        var value = $"{mediaType} {formatLabel}".ToLowerInvariant();
        if (value.Contains("audio")) return MediaFormatType.Audiobook;
        if (value.Contains("epub") || value.Contains("ebook") || value.Contains("book")) return MediaFormatType.Ebook;
        if (value.Contains("comic") || value.Contains("cbz")) return MediaFormatType.ComicIssue;
        if (value.Contains("movie") || value.Contains("video")) return MediaFormatType.Movie;
        if (value.Contains("music") || value.Contains("album")) return MediaFormatType.MusicAlbum;
        if (value.Contains("tv")) return MediaFormatType.TvSeries;
        return MediaFormatType.Ebook;
    }

    private static string ToFormatDisplay(string mediaType, string? formatLabel)
    {
        if (!string.IsNullOrWhiteSpace(formatLabel))
            return formatLabel;
        return ToFormatType(mediaType, formatLabel) switch
        {
            MediaFormatType.Audiobook => "Audiobook",
            MediaFormatType.Ebook => "Ebook",
            MediaFormatType.ComicIssue => "Comic Issue",
            MediaFormatType.Movie => "Movie",
            MediaFormatType.MusicAlbum => "Music Album",
            MediaFormatType.TvSeries => "TV",
            _ => mediaType,
        };
    }

    private static string? BuildSubtitle(LibraryItemDetail detail, DetailEntityType entityType, MultiFormatState state)
    {
        if (state != MultiFormatState.SingleFormat)
            return "Book + Audiobook • Separate Progress";

        return entityType switch
        {
            DetailEntityType.Book => detail.Author,
            DetailEntityType.Audiobook => FirstNonBlank(detail.Narrator, detail.Author),
            DetailEntityType.Movie => FirstNonBlank(detail.Year, "Movie"),
            DetailEntityType.TvEpisode => string.Join(" • ", new[] { detail.ShowName, FormatSeasonEpisode(detail.SeasonNumber, detail.EpisodeNumber) }.Where(s => !string.IsNullOrWhiteSpace(s))),
            _ => FormatEntityType(entityType),
        };
    }

    private static IReadOnlyList<RelationshipGroup> BuildRelationshipStrip(LibraryItemDetail detail, SeriesPlacementViewModel? series)
    {
        var groups = new List<RelationshipGroup>();
        if (series is not null)
        {
            groups.Add(new RelationshipGroup
            {
                Title = "Series",
                Items = [new RelatedEntityChip { Id = series.SeriesId, EntityType = RelatedEntityType.Series, Label = series.SeriesTitle }],
            });
        }

        if (!string.IsNullOrWhiteSpace(detail.UniverseSummary?.UniverseName))
        {
            groups.Add(new RelationshipGroup
            {
                Title = "Universe",
                Items = [new RelatedEntityChip { Id = detail.UniverseSummary.UniverseQid ?? detail.UniverseSummary.UniverseName!, EntityType = RelatedEntityType.Universe, Label = detail.UniverseSummary.UniverseName! }],
            });
        }

        return groups;
    }

    private static HeroBrandViewModel? BuildHeroBrand(DetailEntityType entityType, string? label, string? imageUrl)
    {
        if (entityType is not (DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode))
            return null;

        if (string.IsNullOrWhiteSpace(label) && string.IsNullOrWhiteSpace(imageUrl))
            return null;

        return new HeroBrandViewModel
        {
            Label = string.IsNullOrWhiteSpace(label) ? null : label,
            ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl,
        };
    }

    private static IReadOnlyList<MediaGroupingViewModel> BuildCollectionMediaGroups(DetailEntityType entityType, IReadOnlyList<CollectionWorkSummary> works)
    {
        if (entityType == DetailEntityType.TvShow)
        {
            return works.GroupBy(w => string.IsNullOrWhiteSpace(w.Season) ? "Season 1" : $"Season {w.Season}")
                .OrderBy(g => TryParseInt(g.Key.Replace("Season ", "")) ?? int.MaxValue)
                .Select(g => new MediaGroupingViewModel
                {
                    Key = g.Key.ToLowerInvariant().Replace(" ", "-"),
                    Title = g.Key,
                    Items = g.Select(ToMediaItem).ToList(),
                }).ToList();
        }

        return
        [
            new MediaGroupingViewModel
            {
                Key = entityType == DetailEntityType.MusicAlbum ? "tracks" : "items",
                Title = entityType == DetailEntityType.MusicAlbum ? "Tracks" : "Items",
                Items = works.Select(ToMediaItem).ToList(),
            }
        ];
    }

    private static MediaGroupingItemViewModel ToMediaItem(CollectionWorkSummary work)
        => new()
        {
            Id = work.Id,
            EntityType = InferMediaItemEntityType(work),
            Title = work.Title,
            Subtitle = FirstNonBlank(FormatSeasonEpisode(work.Season, work.Episode), work.Year, work.Duration),
            Description = work.Description,
            ArtworkUrl = FirstNonBlank(work.BackgroundUrl, work.ArtworkUrl),
            Metadata = BuildEpisodeMetadata(work.Duration, work.Year),
            Actions = [new DetailAction { Key = "open", Label = "Open", Icon = "open_in_new", Route = BuildWorkRoute(work) }],
            IsOwned = true,
        };

    private static IReadOnlyList<MetadataPill> BuildEpisodeMetadata(string? duration, string? year)
    {
        var values = new List<MetadataPill>();
        if (!string.IsNullOrWhiteSpace(duration))
            values.Add(new MetadataPill { Label = duration, Kind = "duration" });
        if (!string.IsNullOrWhiteSpace(year))
            values.Add(new MetadataPill { Label = year, Kind = "year" });
        return values;
    }

    private static DetailEntityType InferMediaItemEntityType(CollectionWorkSummary work)
    {
        if (!string.IsNullOrWhiteSpace(work.Episode) || work.MediaType.Contains("TV", StringComparison.OrdinalIgnoreCase)) return DetailEntityType.TvEpisode;
        if (work.MediaType.Contains("movie", StringComparison.OrdinalIgnoreCase)) return DetailEntityType.Movie;
        if (work.MediaType.Contains("music", StringComparison.OrdinalIgnoreCase)) return DetailEntityType.MusicTrack;
        if (work.MediaType.Contains("audio", StringComparison.OrdinalIgnoreCase)) return DetailEntityType.Audiobook;
        if (work.MediaType.Contains("comic", StringComparison.OrdinalIgnoreCase)) return DetailEntityType.ComicIssue;
        return DetailEntityType.Book;
    }

    private static string BuildWorkRoute(CollectionWorkSummary work) => InferMediaItemEntityType(work) switch
    {
        DetailEntityType.Movie => $"/watch/movie/{work.Id}",
        DetailEntityType.TvEpisode => $"/details/tvepisode/{work.Id}?context=watch",
        DetailEntityType.Audiobook => $"/listen/audiobook/{work.Id}",
        _ => $"/book/{work.Id}",
    };

    private static IReadOnlyList<MetadataPill> BuildCollectionMetadata(DetailEntityType entityType, IReadOnlyList<CollectionWorkSummary> works)
        => [new MetadataPill { Label = FormatEntityType(entityType) }, new MetadataPill { Label = $"{works.Count} item{(works.Count == 1 ? "" : "s")}" }];

    private static IReadOnlyList<DetailAction> BuildCollectionActions(Guid id, DetailEntityType entityType, DetailPresentationContext context)
        => entityType switch
        {
            DetailEntityType.TvShow => [new DetailAction { Key = "watch-latest", Label = "Watch Latest", Icon = "play_arrow", IsPrimary = true }],
            DetailEntityType.MusicAlbum => [new DetailAction { Key = "play-album", Label = "Play Album", Icon = "play_arrow", IsPrimary = true }, new DetailAction { Key = "shuffle", Label = "Shuffle", Icon = "shuffle" }],
            _ => [new DetailAction { Key = "open", Label = "Open", Icon = "open_in_new", IsPrimary = true }],
        };

    private async Task<IReadOnlyList<CreditGroupViewModel>> BuildCollectionCreditsAsync(Guid collectionId, IReadOnlyList<CollectionWorkSummary> works, DetailEntityType entityType, CancellationToken ct)
    {
        if (entityType != DetailEntityType.TvShow)
            return [];

        var root = works.FirstOrDefault()?.Id;
        if (!Guid.TryParse(root, out var rootWorkId))
            return [];

        var cast = await CastCreditQueries.BuildForWorkAsync(rootWorkId, _canonicalArrays, _persons, _db, ct);
        return cast.Count == 0
            ? []
            : [new CreditGroupViewModel
            {
                Title = "Cast",
                GroupType = CreditGroupType.Cast,
                Credits = cast.Select((credit, index) => new EntityCreditViewModel
                {
                    EntityId = (credit.PersonId ?? Guid.Empty).ToString("D"),
                    EntityType = RelatedEntityType.Person,
                    DisplayName = credit.Name,
                    ImageUrl = credit.HeadshotUrl,
                    FallbackInitials = Initials(credit.Name),
                    PrimaryRole = "Actor",
                    CharacterName = credit.Characters.FirstOrDefault()?.CharacterName,
                    CharacterEntityId = credit.Characters.FirstOrDefault()?.FictionalEntityId.ToString("D"),
                    CharacterImageUrl = credit.Characters.FirstOrDefault()?.PortraitUrl,
                    SortOrder = index,
                }).ToList(),
            }];
    }

    private async Task<IReadOnlyList<CharacterGroupViewModel>> BuildCollectionCharactersAsync(Guid collectionId, string? qid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(qid))
            return [];

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<CollectionCharacterRow>(new CommandDefinition(
            """
            SELECT fe.id AS Id,
                   fe.label AS Label,
                   fe.wikidata_qid AS WikidataQid,
                   fe.fictional_universe_qid AS UniverseQid,
                   fe.fictional_universe_label AS UniverseLabel,
                   fe.image_url AS ImageUrl,
                   fe.entity_sub_type AS EntitySubType,
                   cp.id AS PortraitId,
                   cp.image_url AS PortraitImageUrl,
                   cp.local_image_path AS PortraitLocalImagePath,
                   CASE WHEN cp.is_default = 1 THEN 1 ELSE 0 END AS PortraitIsDefault
            FROM fictional_entities fe
            LEFT JOIN character_portraits cp
                ON cp.fictional_entity_id = fe.id
               AND cp.id = (
                   SELECT cp2.id
                   FROM character_portraits cp2
                   WHERE cp2.fictional_entity_id = fe.id
                   ORDER BY cp2.is_default DESC, cp2.updated_at DESC, cp2.created_at DESC
                   LIMIT 1
               )
            WHERE fe.fictional_universe_qid = @qid
              AND fe.entity_sub_type = 'Character'
            ORDER BY fe.label
            LIMIT 24;
            """,
            new { qid },
            cancellationToken: ct));

        var characters = rows.Select(row => new EntityCreditViewModel
        {
            EntityId = row.Id.ToString("D"),
            EntityType = RelatedEntityType.Character,
            DisplayName = row.Label,
            ImageUrl = ApiImageUrls.BuildCharacterPortraitUrl(row.PortraitId, row.PortraitLocalImagePath, row.PortraitImageUrl)
                ?? row.ImageUrl,
            FallbackInitials = Initials(row.Label),
            PrimaryRole = "Character",
            IsCanonical = !string.IsNullOrWhiteSpace(row.WikidataQid),
        }).ToList();

        return characters.Count == 0
            ? []
            : [new CharacterGroupViewModel { Title = "Characters", GroupType = CharacterGroupType.MainCharacters, Characters = characters }];
    }

    private async Task<IReadOnlyList<CreditGroupViewModel>> BuildUniverseCastGroupsAsync(string? qid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(qid))
            return [];

        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync<UniversePerformerRow>(new CommandDefinition(
            """
            SELECT p.id AS PersonId,
                   p.name AS PersonName,
                   p.wikidata_qid AS PersonQid,
                   p.headshot_url AS HeadshotUrl,
                   p.local_headshot_path AS LocalHeadshotPath,
                   fe.id AS CharacterId,
                   fe.label AS CharacterName,
                   cp.id AS PortraitId,
                   cp.image_url AS PortraitImageUrl,
                   cp.local_image_path AS PortraitLocalImagePath,
                   CASE WHEN cp.is_default = 1 THEN 1 ELSE 0 END AS PortraitIsDefault
            FROM fictional_entities fe
            INNER JOIN character_performer_links cpl
                ON cpl.fictional_entity_id = fe.id
            INNER JOIN persons p
                ON p.id = cpl.person_id
            LEFT JOIN character_portraits cp
                ON cp.fictional_entity_id = fe.id
               AND cp.person_id = p.id
            WHERE fe.fictional_universe_qid = @qid
            ORDER BY p.name, fe.label, cp.is_default DESC;
            """,
            new { qid },
            cancellationToken: ct))).ToList();

        var credits = rows
            .Where(row => row.PersonId.HasValue && !string.IsNullOrWhiteSpace(row.PersonName))
            .GroupBy(row => new
            {
                row.PersonId,
                row.PersonName,
                row.PersonQid,
                row.HeadshotUrl,
                row.LocalHeadshotPath,
            })
            .Select((group, index) =>
            {
                var preferredCharacter = group
                    .OrderByDescending(row => row.PortraitIsDefault)
                    .ThenByDescending(row => !string.IsNullOrWhiteSpace(row.PortraitImageUrl))
                    .FirstOrDefault();

                return new EntityCreditViewModel
                {
                    EntityId = group.Key.PersonId!.Value.ToString("D"),
                    EntityType = RelatedEntityType.Person,
                    DisplayName = group.Key.PersonName ?? "Unknown",
                    ImageUrl = ApiImageUrls.BuildPersonHeadshotUrl(group.Key.PersonId.Value, group.Key.LocalHeadshotPath, group.Key.HeadshotUrl),
                    FallbackInitials = Initials(group.Key.PersonName ?? "Unknown"),
                    PrimaryRole = "Actor",
                    CharacterName = preferredCharacter?.CharacterName,
                    CharacterEntityId = preferredCharacter?.CharacterId.ToString("D"),
                    CharacterImageUrl = preferredCharacter is null
                        ? null
                        : ApiImageUrls.BuildCharacterPortraitUrl(
                            preferredCharacter.PortraitId,
                            preferredCharacter.PortraitLocalImagePath,
                            preferredCharacter.PortraitImageUrl),
                    SortOrder = index,
                    IsCanonical = !string.IsNullOrWhiteSpace(group.Key.PersonQid),
                };
            })
            .Take(24)
            .ToList();

        return credits.Count == 0
            ? []
            : [new CreditGroupViewModel { Title = "Cast", GroupType = CreditGroupType.Cast, Credits = credits }];
    }

    private async Task<IReadOnlyList<RelationshipGroup>> BuildUniverseRelationshipGroupsAsync(string? qid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(qid))
            return [];

        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync<UniverseRelationshipRow>(new CommandDefinition(
            """
            SELECT er.relationship_type AS RelationshipType,
                   er.subject_qid AS SubjectQid,
                   er.object_qid AS ObjectQid,
                   COALESCE(subject.label, er.subject_qid) AS SubjectLabel,
                   COALESCE(object.label, er.object_qid) AS ObjectLabel,
                   subject.entity_sub_type AS SubjectType,
                   object.entity_sub_type AS ObjectType
            FROM entity_relationships er
            INNER JOIN fictional_entities subject
                ON subject.wikidata_qid = er.subject_qid
               AND subject.fictional_universe_qid = @qid
            INNER JOIN fictional_entities object
                ON object.wikidata_qid = er.object_qid
               AND object.fictional_universe_qid = @qid
            ORDER BY er.relationship_type, SubjectLabel, ObjectLabel
            LIMIT 60;
            """,
            new { qid },
            cancellationToken: ct))).ToList();

        return rows
            .GroupBy(row => row.RelationshipType, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RelationshipGroup
            {
                Title = ToRelationshipGroupTitle(group.Key),
                Items = group.Take(12).Select(row => new RelatedEntityChip
                {
                    Id = row.SubjectQid,
                    EntityType = RelatedEntityType.Character,
                    Label = $"{row.SubjectLabel} {FormatRelationshipLabel(row.RelationshipType)} {row.ObjectLabel}",
                }).ToList(),
            })
            .ToList();
    }

    private static IReadOnlyList<RelationshipGroup> BuildCollectionRelationships(CollectionDetailRow row, DetailEntityType entityType)
        => string.IsNullOrWhiteSpace(row.WikidataQid)
            ? []
            : [new RelationshipGroup { Title = "Canonical Identity", Items = [new RelatedEntityChip { Id = row.WikidataQid!, EntityType = RelatedEntityType.Universe, Label = row.WikidataQid! }] }];

    private static IReadOnlyList<EntityCreditViewModel> BuildPreviewContributors(
        DetailEntityType entityType,
        IReadOnlyList<CreditGroupViewModel> groups)
    {
        CreditGroupType[] preferredGroupTypes = entityType switch
        {
            DetailEntityType.Movie or DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode or DetailEntityType.Universe => [CreditGroupType.Cast],
            DetailEntityType.Book or DetailEntityType.Audiobook or DetailEntityType.Work => [CreditGroupType.Authors, CreditGroupType.Narrators],
            DetailEntityType.ComicIssue or DetailEntityType.ComicSeries => [CreditGroupType.Writers, CreditGroupType.Illustrators, CreditGroupType.CreativeTeam],
            _ => Array.Empty<CreditGroupType>(),
        };

        var preferredCredits = preferredGroupTypes.Length == 0
            ? []
            : groups
                .Where(group => preferredGroupTypes.Contains(group.GroupType))
                .SelectMany(group => group.Credits)
                .OrderBy(credit => credit.SortOrder)
                .Take(12)
                .ToList();

        return preferredCredits.Count > 0
            ? preferredCredits
            : groups.SelectMany(g => g.Credits).OrderBy(c => c.SortOrder).Take(12).ToList();
    }

    private static string ToRelationshipGroupTitle(string relationshipType)
        => string.Join(' ', relationshipType.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    private static string FormatRelationshipLabel(string relationshipType) => relationshipType switch
    {
        "father" => "is father of",
        "mother" => "is mother of",
        "spouse" => "is spouse of",
        "sibling" => "is sibling of",
        "child" => "is child of",
        "opponent" => "opposes",
        "student_of" => "is student of",
        "member_of" => "is member of",
        "residence" => "resides in",
        "located_in" => "is located in",
        "part_of" => "is part of",
        "head_of" => "leads",
        "parent_organization" => "is parent organization of",
        "has_parts" => "has part",
        "creator" => "created",
        "performer" => "performed by",
        "same_as" => "is same as",
        "significant_person" => "is significant to",
        "affiliation" => "is affiliated with",
        "based_on" => "is based on",
        "derivative_work" => "is derivative of",
        "inspired_by" => "is inspired by",
        _ => relationshipType.Replace('_', ' '),
    };

    private static string BuildCollectionSubtitle(DetailEntityType entityType, IReadOnlyList<CollectionWorkSummary> works)
    {
        var types = works.Select(w => FormatEntityType(InferMediaItemEntityType(w))).Distinct(StringComparer.OrdinalIgnoreCase).Take(3);
        return $"{FormatEntityType(entityType)} • {works.Count} item{(works.Count == 1 ? "" : "s")} • {string.Join(", ", types)}";
    }

    private static IReadOnlyList<CreditGroupViewModel> BuildPersonCreditGroups(IReadOnlyList<MediaEngine.Api.Models.PersonLibraryCreditDto> credits, DetailPresentationContext context)
        => credits
            .GroupBy(c => string.IsNullOrWhiteSpace(c.Role) ? "Credits" : c.Role)
            .OrderBy(g => PersonRolePriority(g.Key, context))
            .Select(g => new CreditGroupViewModel
            {
                Title = g.Key,
                GroupType = CreditGroupType.RelatedPeople,
                Credits = g.Select((credit, index) => new EntityCreditViewModel
                {
                    EntityId = credit.WorkId.ToString("D"),
                    EntityType = RelatedEntityType.Series,
                    DisplayName = credit.Title,
                    ImageUrl = credit.CoverUrl,
                    FallbackInitials = Initials(credit.Title),
                    PrimaryRole = credit.Role,
                    SecondaryRole = credit.MediaType,
                    CharacterName = credit.Characters.FirstOrDefault()?.CharacterName,
                    SortOrder = index,
                }).ToList(),
            }).ToList();

    private static IReadOnlyList<CharacterGroupViewModel> BuildPersonCharacterGroups(IReadOnlyList<MediaEngine.Api.Models.PersonCharacterRoleDto> roles)
    {
        var characters = roles.Select(role => new EntityCreditViewModel
        {
            EntityId = role.FictionalEntityId.ToString("D"),
            EntityType = RelatedEntityType.Character,
            DisplayName = role.CharacterName ?? "Character",
            ImageUrl = role.PortraitUrl,
            FallbackInitials = Initials(role.CharacterName ?? "Character"),
            PrimaryRole = "Character",
            SecondaryRole = role.WorkTitle,
        }).ToList();

        return characters.Count == 0
            ? []
            : [new CharacterGroupViewModel { Title = "Characters", GroupType = CharacterGroupType.MainCharacters, Characters = characters }];
    }

    private static IReadOnlyList<MediaGroupingViewModel> BuildPersonMediaGroups(IReadOnlyList<MediaEngine.Api.Models.PersonLibraryCreditDto> credits, DetailPresentationContext context)
        => credits
            .GroupBy(c => PersonMediaGroupKey(c.MediaType, context))
            .OrderBy(g => PersonMediaGroupPriority(g.Key, context))
            .Select(g => new MediaGroupingViewModel
            {
                Key = g.Key.ToLowerInvariant().Replace(" ", "-").Replace("&", "and"),
                Title = g.Key,
                Items = g.Select(c => new MediaGroupingItemViewModel
                {
                    Id = c.WorkId.ToString("D"),
                    EntityType = MapMediaTypeToEntityType(c.MediaType),
                    Title = c.Title,
                    Subtitle = string.Join(" • ", new[] { c.Role, c.Year }.Where(v => !string.IsNullOrWhiteSpace(v))),
                    ArtworkUrl = c.CoverUrl,
                    Actions = [new DetailAction { Key = "open", Label = "Open", Icon = "open_in_new", Route = BuildCreditRoute(c) }],
                    IsOwned = true,
                }).ToList(),
            }).ToList();

    private static IReadOnlyList<MetadataPill> BuildPersonMetadata(IReadOnlyList<string> roles, int creditCount)
        => roles.Take(4).Select(role => new MetadataPill { Label = role }).Append(new MetadataPill { Label = $"{creditCount} library credits" }).ToList();

    private static IReadOnlyList<DetailAction> BuildPersonActions(Guid personId, DetailEntityType entityType, DetailPresentationContext context)
        => context == DetailPresentationContext.Listen || entityType == DetailEntityType.MusicArtist
            ? [new DetailAction { Key = "play", Label = "Play Artist", Icon = "play_arrow", IsPrimary = true }, new DetailAction { Key = "shuffle", Label = "Shuffle", Icon = "shuffle" }]
            : [new DetailAction { Key = "view-works", Label = "View Works", Icon = "collections", IsPrimary = true }];

    private static string? PreferredAssetUrl(IReadOnlyList<MediaEngine.Domain.Entities.EntityAsset> assets, string assetType)
        => assets.FirstOrDefault(a => a.AssetTypeValue.Equals(assetType, StringComparison.OrdinalIgnoreCase) && a.IsPreferred)?.ImageUrl
           ?? assets.FirstOrDefault(a => a.AssetTypeValue.Equals(assetType, StringComparison.OrdinalIgnoreCase))?.ImageUrl;

    private static ArtworkSource ResolveArtworkSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return ArtworkSource.Generated;
        if (source.Contains("user", StringComparison.OrdinalIgnoreCase) || source.Contains("manual", StringComparison.OrdinalIgnoreCase))
            return ArtworkSource.User;
        return ArtworkSource.Provider;
    }

    private static CanonicalIdentityStatus ResolveIdentityStatus(string? qid, string? status, double? confidence)
    {
        if (!string.IsNullOrWhiteSpace(qid))
            return CanonicalIdentityStatus.WikidataLinked;
        if (status?.Contains("review", StringComparison.OrdinalIgnoreCase) == true || confidence is < 0.7)
            return CanonicalIdentityStatus.NeedsReview;
        return CanonicalIdentityStatus.ProviderMatched;
    }

    private static string? BuildSeriesPositionLabel(DetailEntityType type, int? position, int total, string seriesTitle)
    {
        if (!position.HasValue)
            return null;

        var prefix = type switch
        {
            DetailEntityType.Movie => "Movie",
            DetailEntityType.Audiobook => "Audiobook",
            DetailEntityType.ComicIssue => "Issue",
            _ => "Book",
        };
        return $"{prefix} {position} of {total} in {seriesTitle}";
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

    private static DetailEntityType MapMediaTypeToEntityType(string? mediaType)
    {
        if (mediaType?.Contains("movie", StringComparison.OrdinalIgnoreCase) == true) return DetailEntityType.Movie;
        if (mediaType?.Contains("tv", StringComparison.OrdinalIgnoreCase) == true) return DetailEntityType.TvEpisode;
        if (mediaType?.Contains("music", StringComparison.OrdinalIgnoreCase) == true) return DetailEntityType.MusicTrack;
        if (mediaType?.Contains("audio", StringComparison.OrdinalIgnoreCase) == true) return DetailEntityType.Audiobook;
        if (mediaType?.Contains("comic", StringComparison.OrdinalIgnoreCase) == true) return DetailEntityType.ComicIssue;
        return DetailEntityType.Book;
    }

    private static string? BuildCreditRoute(MediaEngine.Api.Models.PersonLibraryCreditDto credit)
        => MapMediaTypeToEntityType(credit.MediaType) switch
        {
            DetailEntityType.Movie => $"/watch/movie/{credit.WorkId}",
            DetailEntityType.TvEpisode when credit.CollectionId.HasValue => $"/watch/tv/show/{credit.CollectionId.Value}/episode/{credit.WorkId}",
            DetailEntityType.Audiobook => $"/listen/audiobook/{credit.WorkId}",
            _ => $"/book/{credit.WorkId}",
        };

    private static string PersonMediaGroupKey(string? mediaType, DetailPresentationContext context)
    {
        if (context == DetailPresentationContext.Listen && mediaType?.Contains("music", StringComparison.OrdinalIgnoreCase) == true)
            return "Music";
        if (context == DetailPresentationContext.Watch && (mediaType?.Contains("movie", StringComparison.OrdinalIgnoreCase) == true || mediaType?.Contains("tv", StringComparison.OrdinalIgnoreCase) == true))
            return "Movies & TV";
        if (mediaType?.Contains("audio", StringComparison.OrdinalIgnoreCase) == true) return "Audiobooks";
        if (mediaType?.Contains("book", StringComparison.OrdinalIgnoreCase) == true) return "Books";
        if (mediaType?.Contains("music", StringComparison.OrdinalIgnoreCase) == true) return "Music";
        return FirstNonBlank(mediaType, "Works");
    }

    private static int PersonMediaGroupPriority(string key, DetailPresentationContext context)
        => (context, key) switch
        {
            (DetailPresentationContext.Listen, "Music") => 0,
            (DetailPresentationContext.Watch, "Movies & TV") => 0,
            (DetailPresentationContext.Read, "Books") => 0,
            (DetailPresentationContext.Read, "Audiobooks") => 1,
            _ => 5,
        };

    private static int PersonRolePriority(string role, DetailPresentationContext context)
        => role.ToLowerInvariant() switch
        {
            "primary artist" or "artist" or "performer" when context == DetailPresentationContext.Listen => 0,
            "actor" when context == DetailPresentationContext.Watch => 0,
            "author" when context == DetailPresentationContext.Read => 0,
            "narrator" when context == DetailPresentationContext.Read => 1,
            _ => 5,
        };

    private static string FormatEntityType(DetailEntityType entityType) => entityType switch
    {
        DetailEntityType.TvShow => "TV Show",
        DetailEntityType.TvSeason => "TV Season",
        DetailEntityType.TvEpisode => "TV Episode",
        DetailEntityType.MovieSeries => "Movie Series",
        DetailEntityType.BookSeries => "Book Series",
        DetailEntityType.ComicIssue => "Comic Issue",
        DetailEntityType.ComicSeries => "Comic Series",
        DetailEntityType.MusicAlbum => "Album",
        DetailEntityType.MusicArtist => "Artist",
        _ => entityType.ToString(),
    };

    private static string ToTabLabel(string key) => key switch
    {
        "people" => "Cast",
        "movies-tv" => "Movies & TV",
        "appears-on" => "Appears On",
        _ => string.Join(" ", key.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..])),
    };

    private static string? GetValue(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string? FirstText(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? NormalizeHeroSummary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = string.Join(
            ' ',
            value.Replace("\r", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return normalized.Length <= 260 ? normalized : normalized[..260].TrimEnd() + "...";
    }

    private static string? BuildFallbackHeroSummary(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var firstParagraph = description.Replace("\r", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return NormalizeHeroSummary(firstParagraph ?? description);
    }

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static IReadOnlyList<MetadataPill> MaybePill(string? value)
        => string.IsNullOrWhiteSpace(value) ? [] : [new MetadataPill { Label = value }];

    private static IReadOnlyList<string> SplitNames(string value)
        => value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Initials(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant(),
        };
    }

    private static int? TryParseInt(string? value)
        => TryParseSeriesPosition(value);

    private static int? TryParseSeriesPosition(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
            return parsedInt;

        var numericText = new string(trimmed
            .SkipWhile(c => !char.IsDigit(c))
            .TakeWhile(c => char.IsDigit(c) || c is '.' or ',')
            .Select(c => c == ',' ? '.' : c)
            .ToArray());

        if (double.TryParse(numericText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedDouble))
            return (int)Math.Round(parsedDouble, MidpointRounding.AwayFromZero);

        return null;
    }

    private static string? ExtractQid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var qid = value.Trim();
        if (qid.Contains("::", StringComparison.Ordinal))
            qid = qid.Split("::", 2, StringSplitOptions.TrimEntries)[0];
        if (qid.Contains("|||", StringComparison.Ordinal))
            qid = qid.Split("|||", 2, StringSplitOptions.TrimEntries)[0];
        if (qid.Contains('/', StringComparison.Ordinal))
            qid = qid[(qid.LastIndexOf('/') + 1)..];

        return string.IsNullOrWhiteSpace(qid) ? null : qid;
    }

    private static string? StringValue(object? value)
    {
        if (value is null or DBNull)
            return null;

        if (value is byte[] bytes)
            return Encoding.UTF8.GetString(bytes);

        var text = Convert.ToString(value);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? ResolveCollectionArtworkUrl(string? value, string? assetIdValue, string kind, string? state)
    {
        if (!Guid.TryParse(assetIdValue, out var assetId))
            return string.IsNullOrWhiteSpace(value) ? null : value;

        // Collection and TV-show detail pages are composed from representative
        // child works. Their downloaded artwork is stored on the child asset, so
        // route the same local image stream URLs used by work/movie detail pages.
        return DisplayArtworkUrlResolver.Resolve(value, assetId, kind, state);
    }

    private static int? IntValue(object? value)
    {
        if (value is null or DBNull)
            return null;

        return value switch
        {
            int i => i,
            long l => checked((int)l),
            _ => int.TryParse(Convert.ToString(value), out var parsed) ? parsed : null,
        };
    }

    private sealed record WorkContributorResult(IReadOnlyList<MediaEngine.Api.Models.CastCreditDto> CastCredits);
    private sealed record CanonicalPair(string Key, string Value);
    private sealed record WorkArtworkFallback
    {
        public string? CoverUrl { get; init; }
        public string? SquareUrl { get; init; }
        public string? BackgroundUrl { get; init; }
        public string? BannerUrl { get; init; }
    }
    private sealed record OwnedFormatRow(Guid EditionId, string? FormatLabel, Guid AssetId, string FilePathRoot, string? AssetCoverUrl, string? EditionCoverUrl, string? Runtime, string? PageCount, string? Narrator);
    private sealed record CollectionDetailRow(Guid Id, string? DisplayName, string? WikidataQid, string? Description, string? Tagline, string? CoverUrl, string? BackgroundUrl, string? BannerUrl, string? LogoUrl, string? HeroBrandLabel, string? HeroBrandImageUrl);
    private sealed record SeriesRow(string WorkId, string Title, string? MediaType, string? PositionLabel, string? ArtworkUrl);
    private sealed record CollectionWorkSummary(string Id, string MediaType, int? Ordinal, string Title, string? Description, string? Season, string? Episode, string? TrackNumber, string? Duration, string? Year, string? ArtworkUrl, string? BackgroundUrl);
    private sealed record CharacterDetailRow(Guid Id, string Label, string? WikidataQid, string? UniverseQid, string? UniverseLabel, string? ImageUrl, string? EntitySubType);
    private sealed class CollectionCharacterRow
    {
        public Guid Id { get; init; }
        public string Label { get; init; } = "";
        public string? WikidataQid { get; init; }
        public string? UniverseQid { get; init; }
        public string? UniverseLabel { get; init; }
        public string? ImageUrl { get; init; }
        public string? EntitySubType { get; init; }
        public Guid? PortraitId { get; init; }
        public string? PortraitImageUrl { get; init; }
        public string? PortraitLocalImagePath { get; init; }
        public bool PortraitIsDefault { get; init; }
    }

    private sealed record CharacterPortraitRow(Guid Id, string? ImageUrl, string? LocalImagePath, bool IsDefault);
    private sealed class UniversePerformerRow
    {
        public Guid? PersonId { get; init; }
        public string? PersonName { get; init; }
        public string? PersonQid { get; init; }
        public string? HeadshotUrl { get; init; }
        public string? LocalHeadshotPath { get; init; }
        public Guid CharacterId { get; init; }
        public string? CharacterName { get; init; }
        public Guid? PortraitId { get; init; }
        public string? PortraitImageUrl { get; init; }
        public string? PortraitLocalImagePath { get; init; }
        public bool PortraitIsDefault { get; init; }
    }

    private sealed class UniverseRelationshipRow
    {
        public string RelationshipType { get; init; } = "";
        public string SubjectQid { get; init; } = "";
        public string ObjectQid { get; init; } = "";
        public string SubjectLabel { get; init; } = "";
        public string ObjectLabel { get; init; } = "";
        public string? SubjectType { get; init; }
        public string? ObjectType { get; init; }
    }
}
