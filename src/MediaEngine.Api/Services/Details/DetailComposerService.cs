using Dapper;
using MediaEngine.Api.Endpoints;
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
        var multiFormatState = ownedFormats.Count > 1
            ? MultiFormatState.MultipleFormatsSeparateProgress
            : MultiFormatState.SingleFormat;

        var artwork = BuildArtwork(
            entityType,
            detail.BackgroundUrl ?? detail.HeroUrl,
            detail.BannerUrl,
            detail.CoverUrl,
            detail.CoverUrl,
            null,
            values,
            ownedFormats.Select(f => f.CoverUrl).Where(url => !string.IsNullOrWhiteSpace(url)).Cast<string>().ToList(),
            ownedFormats.Count,
            detail.ArtworkSource);

        var contributors = await BuildWorkContributorsAsync(workId, detail, entityType, ct);
        var characters = BuildCharacterGroupsFromCast(contributors.CastCredits);
        var contributorGroups = BuildContributorGroups(detail, entityType, contributors.CastCredits);
        var seriesPlacement = await BuildSeriesPlacementAsync(workId, detail, entityType, ct);
        var mediaGroups = await BuildWorkMediaGroupsAsync(workId, entityType, ct);

        return new DetailPageViewModel
        {
            Id = workId.ToString("D"),
            EntityType = entityType,
            PresentationContext = context,
            Title = FirstNonBlank(detail.Title, detail.EpisodeTitle, detail.FileName, "Untitled"),
            Subtitle = BuildSubtitle(detail, entityType, multiFormatState),
            Tagline = detail.Tagline,
            Description = detail.Description,
            Artwork = artwork,
            OwnedFormats = ownedFormats,
            MultiFormatState = multiFormatState,
            SyncCapability = BuildSyncCapability(workId, ownedFormats, multiFormatState),
            SeriesPlacement = seriesPlacement,
            Metadata = BuildMetadataPills(detail, entityType),
            PrimaryActions = BuildPrimaryActions(workId, entityType, context, ownedFormats),
            SecondaryActions = BuildSecondaryActions(workId, entityType),
            OverflowActions = BuildOverflowActions(workId, entityType, isAdminView),
            ContributorGroups = contributorGroups,
            PreviewContributors = contributorGroups.SelectMany(g => g.Credits).OrderBy(c => c.SortOrder).Take(12).ToList(),
            CharacterGroups = characters,
            PreviewCharacters = characters.SelectMany(g => g.Characters).Take(12).ToList(),
            RelationshipStrip = BuildRelationshipStrip(detail, seriesPlacement),
            Tabs = BuildTabs(entityType, context, isAdminView),
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
        var row = await conn.QueryFirstOrDefaultAsync<CollectionDetailRow>(new CommandDefinition(
            """
            SELECT c.id AS Id,
                   c.display_name AS DisplayName,
                   c.wikidata_qid AS WikidataQid,
                   (SELECT value FROM canonical_values WHERE entity_id = c.id AND key IN ('description', 'overview') LIMIT 1) AS Description,
                   (SELECT value FROM canonical_values WHERE entity_id = c.id AND key = 'tagline' LIMIT 1) AS Tagline,
                   (SELECT value FROM canonical_values WHERE entity_id = c.id AND key IN ('cover_url', 'cover') LIMIT 1) AS CoverUrl,
                   (SELECT value FROM canonical_values WHERE entity_id = c.id AND key IN ('background_url', 'background') LIMIT 1) AS BackgroundUrl,
                   (SELECT value FROM canonical_values WHERE entity_id = c.id AND key IN ('banner_url', 'banner') LIMIT 1) AS BannerUrl,
                   (SELECT value FROM canonical_values WHERE entity_id = c.id AND key IN ('logo_url', 'logo') LIMIT 1) AS LogoUrl
            FROM collections c
            WHERE c.id = @collectionId
            LIMIT 1;
            """,
            new { collectionId = collectionId.ToString() },
            cancellationToken: ct));

        if (row is null)
            return null;

        var works = await LoadCollectionWorksAsync(collectionId, ct);
        if (entityType == DetailEntityType.Collection)
            entityType = InferCollectionEntityType(works);

        var relatedArt = works.Select(w => w.ArtworkUrl).Where(url => !string.IsNullOrWhiteSpace(url)).Cast<string>().Take(8).ToList();
        var values = await LoadCanonicalMapAsync(collectionId, ct);
        var artwork = BuildArtwork(entityType, row.BackgroundUrl, row.BannerUrl, row.CoverUrl, row.CoverUrl, null, values, relatedArt, 0, null, row.LogoUrl);

        return new DetailPageViewModel
        {
            Id = collectionId.ToString("D"),
            EntityType = entityType,
            PresentationContext = context,
            Title = FirstNonBlank(row.DisplayName, "Collection"),
            Subtitle = BuildCollectionSubtitle(entityType, works),
            Tagline = row.Tagline,
            Description = row.Description,
            Artwork = artwork,
            Metadata = BuildCollectionMetadata(entityType, works),
            PrimaryActions = BuildCollectionActions(collectionId, entityType, context),
            SecondaryActions = [new DetailAction { Key = "collection", Label = "Add to Collection", Icon = "playlist_add" }],
            OverflowActions = BuildOverflowActions(collectionId, entityType, isAdminView),
            ContributorGroups = await BuildCollectionCreditsAsync(collectionId, works, entityType, ct),
            CharacterGroups = await BuildCollectionCharactersAsync(collectionId, row.WikidataQid, ct),
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
                   (SELECT value FROM canonical_values WHERE entity_id = collections.id AND key IN ('background_url', 'background') LIMIT 1) AS BackgroundUrl,
                   (SELECT value FROM canonical_values WHERE entity_id = collections.id AND key IN ('banner_url', 'banner') LIMIT 1) AS BannerUrl,
                   (SELECT value FROM canonical_values WHERE entity_id = collections.id AND key IN ('cover_url', 'cover') LIMIT 1) AS CoverUrl
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

    private static IReadOnlyList<CreditGroupViewModel> BuildContributorGroups(
        LibraryItemDetail detail,
        DetailEntityType entityType,
        IReadOnlyList<MediaEngine.Api.Models.CastCreditDto> cast)
    {
        var groups = new List<CreditGroupViewModel>();
        void AddTextCredit(string title, CreditGroupType type, string? value, string role)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            groups.Add(new CreditGroupViewModel
            {
                Title = title,
                GroupType = type,
                Credits = SplitNames(value).Select((name, index) => new EntityCreditViewModel
                {
                    EntityId = name,
                    EntityType = RelatedEntityType.Person,
                    DisplayName = name,
                    FallbackInitials = Initials(name),
                    PrimaryRole = role,
                    SortOrder = index,
                    IsPrimary = index == 0,
                }).ToList(),
            });
        }

        AddTextCredit("Authors", CreditGroupType.Authors, detail.Author, "Author");
        AddTextCredit("Narrators", CreditGroupType.Narrators, detail.Narrator, "Narrator");
        AddTextCredit("Directors", CreditGroupType.Directors, detail.Director, "Director");
        AddTextCredit("Writers", CreditGroupType.Writers, detail.Writer, "Writer");

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
        var rows = (await conn.QueryAsync<SeriesRow>(new CommandDefinition(
            """
            SELECT w.id AS WorkId,
                   COALESCE(title_asset.value, title_work.value, 'Untitled') AS Title,
                   COALESCE(pos_asset.value, pos_work.value) AS PositionLabel,
                   COALESCE(cover_asset.value, cover_work.value) AS ArtworkUrl
            FROM works w
            LEFT JOIN editions e ON e.work_id = w.id
            LEFT JOIN media_assets ma ON ma.edition_id = e.id
            LEFT JOIN canonical_values series_asset ON series_asset.entity_id = ma.id AND series_asset.key = 'series'
            LEFT JOIN canonical_values series_work ON series_work.entity_id = w.id AND series_work.key = 'series'
            LEFT JOIN canonical_values title_asset ON title_asset.entity_id = ma.id AND title_asset.key IN ('title', 'episode_title')
            LEFT JOIN canonical_values title_work ON title_work.entity_id = w.id AND title_work.key = 'title'
            LEFT JOIN canonical_values pos_asset ON pos_asset.entity_id = ma.id AND pos_asset.key IN ('series_position', 'issue_number')
            LEFT JOIN canonical_values pos_work ON pos_work.entity_id = w.id AND pos_work.key IN ('series_position', 'ordinal')
            LEFT JOIN canonical_values cover_asset ON cover_asset.entity_id = ma.id AND cover_asset.key IN ('cover_url', 'cover')
            LEFT JOIN canonical_values cover_work ON cover_work.entity_id = w.id AND cover_work.key IN ('cover_url', 'cover')
            WHERE COALESCE(series_asset.value, series_work.value) = @series
            GROUP BY w.id
            ORDER BY CAST(COALESCE(pos_asset.value, pos_work.value, w.ordinal, 9999) AS REAL), Title;
            """,
            new { series = detail.Series },
            cancellationToken: ct))).ToList();

        var items = rows.Select((row, index) => new SeriesItemViewModel
        {
            Id = row.WorkId.ToString("D"),
            EntityType = entityType,
            Title = row.Title,
            ArtworkUrl = row.ArtworkUrl,
            PositionNumber = TryParseInt(row.PositionLabel) ?? index + 1,
            PositionLabel = row.PositionLabel,
            IsCurrent = row.WorkId == workId,
            IsOwned = true,
            ProgressState = LibraryProgressState.Unknown,
        }).ToList();

        if (items.Count == 0)
        {
            items.Add(new SeriesItemViewModel
            {
                Id = workId.ToString("D"),
                EntityType = entityType,
                Title = detail.Title,
                ArtworkUrl = detail.CoverUrl,
                PositionLabel = detail.SeriesPosition,
                PositionNumber = TryParseInt(detail.SeriesPosition),
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

    private async Task<IReadOnlyList<CollectionWorkSummary>> LoadCollectionWorksAsync(Guid collectionId, CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<CollectionWorkSummary>(new CommandDefinition(
            """
            SELECT w.id AS Id,
                   w.media_type AS MediaType,
                   w.ordinal AS Ordinal,
                   COALESCE(title_asset.value, episode_title.value, title_work.value, 'Untitled') AS Title,
                   COALESCE(desc_asset.value, desc_work.value) AS Description,
                   COALESCE(season.value, '') AS Season,
                   COALESCE(episode.value, '') AS Episode,
                   COALESCE(track.value, '') AS TrackNumber,
                   COALESCE(runtime.value, duration.value) AS Duration,
                   COALESCE(year_asset.value, year_work.value) AS Year,
                   COALESCE(cover_asset.value, cover_work.value) AS ArtworkUrl,
                   COALESCE(bg_asset.value, bg_work.value, banner_asset.value, banner_work.value) AS BackgroundUrl
            FROM works w
            LEFT JOIN editions e ON e.work_id = w.id
            LEFT JOIN media_assets ma ON ma.edition_id = e.id
            LEFT JOIN canonical_values title_asset ON title_asset.entity_id = ma.id AND title_asset.key = 'title'
            LEFT JOIN canonical_values episode_title ON episode_title.entity_id = ma.id AND episode_title.key = 'episode_title'
            LEFT JOIN canonical_values title_work ON title_work.entity_id = w.id AND title_work.key = 'title'
            LEFT JOIN canonical_values desc_asset ON desc_asset.entity_id = ma.id AND desc_asset.key = 'description'
            LEFT JOIN canonical_values desc_work ON desc_work.entity_id = w.id AND desc_work.key = 'description'
            LEFT JOIN canonical_values season ON season.entity_id = ma.id AND season.key = 'season_number'
            LEFT JOIN canonical_values episode ON episode.entity_id = ma.id AND episode.key = 'episode_number'
            LEFT JOIN canonical_values track ON track.entity_id = ma.id AND track.key = 'track_number'
            LEFT JOIN canonical_values runtime ON runtime.entity_id = ma.id AND runtime.key = 'runtime'
            LEFT JOIN canonical_values duration ON duration.entity_id = ma.id AND duration.key = 'duration'
            LEFT JOIN canonical_values year_asset ON year_asset.entity_id = ma.id AND year_asset.key IN ('year', 'release_year')
            LEFT JOIN canonical_values year_work ON year_work.entity_id = w.id AND year_work.key IN ('year', 'release_year')
            LEFT JOIN canonical_values cover_asset ON cover_asset.entity_id = ma.id AND cover_asset.key IN ('cover_url', 'cover')
            LEFT JOIN canonical_values cover_work ON cover_work.entity_id = w.id AND cover_work.key IN ('cover_url', 'cover')
            LEFT JOIN canonical_values bg_asset ON bg_asset.entity_id = ma.id AND bg_asset.key IN ('background_url', 'background')
            LEFT JOIN canonical_values bg_work ON bg_work.entity_id = w.id AND bg_work.key IN ('background_url', 'background')
            LEFT JOIN canonical_values banner_asset ON banner_asset.entity_id = ma.id AND banner_asset.key IN ('banner_url', 'banner')
            LEFT JOIN canonical_values banner_work ON banner_work.entity_id = w.id AND banner_work.key IN ('banner_url', 'banner')
            WHERE w.collection_id = @collectionId
            GROUP BY w.id
            ORDER BY CAST(NULLIF(Season, '') AS INTEGER), CAST(NULLIF(Episode, '') AS INTEGER), CAST(NULLIF(TrackNumber, '') AS INTEGER), COALESCE(w.ordinal, 9999), Title;
            """,
            new { collectionId = collectionId.ToString() },
            cancellationToken: ct));
        return rows.ToList();
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

        return new ArtworkSet
        {
            BackdropUrl = backdropUrl,
            BannerUrl = bannerUrl,
            PosterUrl = posterUrl,
            CoverUrl = coverUrl,
            LogoUrl = logoUrl ?? GetValue(values, "logo_url") ?? GetValue(values, "logo"),
            PortraitUrl = portraitUrl,
            CharacterImageUrl = entityType == DetailEntityType.Character ? portraitUrl : null,
            RelatedArtworkUrls = relatedArtwork,
            DominantColors = [primary, secondary, accent],
            PrimaryColor = primary,
            SecondaryColor = secondary,
            AccentColor = accent,
            PresentationMode = mode,
            Source = ResolveArtworkSource(artworkSource),
        };
    }

    private static IReadOnlyList<MetadataPill> BuildMetadataPills(LibraryItemDetail detail, DetailEntityType entityType)
    {
        var values = new[] { FormatEntityType(entityType), detail.Year, detail.Runtime, detail.Genre, detail.Rating, detail.Language }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new MetadataPill { Label = value! })
            .ToList();
        return values;
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

    private static IReadOnlyList<DetailAction> BuildSecondaryActions(Guid id, DetailEntityType entityType)
        =>
        [
            new DetailAction { Key = "add", Label = "Add to Collection", Icon = "playlist_add" },
            new DetailAction { Key = "mark", Label = entityType is DetailEntityType.Book or DetailEntityType.Audiobook or DetailEntityType.ComicIssue ? "Mark Finished" : "Mark Watched", Icon = "check_circle" },
        ];

    private static IReadOnlyList<DetailAction> BuildOverflowActions(Guid id, DetailEntityType entityType, bool isAdminView)
    {
        var actions = new List<DetailAction>
        {
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

    private static IReadOnlyList<DetailTab> BuildTabs(DetailEntityType entityType, DetailPresentationContext context, bool isAdminView)
    {
        string[] keys = entityType switch
        {
            DetailEntityType.TvShow => ["episodes", "overview", "people", "universe", "details"],
            DetailEntityType.TvSeason => ["episodes", "overview", "people", "details"],
            DetailEntityType.Book or DetailEntityType.Audiobook => ["overview", "chapters", "contributors", "characters", "series", "universe", "editions", "details"],
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
            PreviewAction = new DetailAction { Key = "preview-sync", Label = "Preview", Icon = "compare_arrows" },
            EnableAction = new DetailAction { Key = "enable-sync", Label = "Enable Sync", Icon = "link" },
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
            Id = work.Id.ToString("D"),
            EntityType = InferMediaItemEntityType(work),
            Title = work.Title,
            Subtitle = FirstNonBlank(FormatSeasonEpisode(work.Season, work.Episode), work.Year, work.Duration),
            Description = work.Description,
            ArtworkUrl = FirstNonBlank(work.BackgroundUrl, work.ArtworkUrl),
            Metadata = new[] { work.Duration, work.Year }.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => new MetadataPill { Label = v! }).ToList(),
            Actions = [new DetailAction { Key = "open", Label = "Open", Icon = "open_in_new", Route = BuildWorkRoute(work) }],
            IsOwned = true,
        };

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
        if (root is null)
            return [];

        var cast = await CastCreditQueries.BuildForWorkAsync(root.Value, _canonicalArrays, _persons, _db, ct);
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
        var rows = await conn.QueryAsync<CharacterDetailRow>(new CommandDefinition(
            """
            SELECT id AS Id,
                   label AS Label,
                   wikidata_qid AS WikidataQid,
                   fictional_universe_qid AS UniverseQid,
                   fictional_universe_label AS UniverseLabel,
                   image_url AS ImageUrl,
                   entity_sub_type AS EntitySubType
            FROM fictional_entities
            WHERE fictional_universe_qid = @qid
              AND entity_sub_type = 'Character'
            ORDER BY label
            LIMIT 24;
            """,
            new { qid },
            cancellationToken: ct));

        var characters = rows.Select(row => new EntityCreditViewModel
        {
            EntityId = row.Id.ToString("D"),
            EntityType = RelatedEntityType.Character,
            DisplayName = row.Label,
            ImageUrl = row.ImageUrl,
            FallbackInitials = Initials(row.Label),
            PrimaryRole = "Character",
            IsCanonical = !string.IsNullOrWhiteSpace(row.WikidataQid),
        }).ToList();

        return characters.Count == 0
            ? []
            : [new CharacterGroupViewModel { Title = "Characters", GroupType = CharacterGroupType.MainCharacters, Characters = characters }];
    }

    private static IReadOnlyList<RelationshipGroup> BuildCollectionRelationships(CollectionDetailRow row, DetailEntityType entityType)
        => string.IsNullOrWhiteSpace(row.WikidataQid)
            ? []
            : [new RelationshipGroup { Title = "Canonical Identity", Items = [new RelatedEntityChip { Id = row.WikidataQid!, EntityType = RelatedEntityType.Universe, Label = row.WikidataQid! }] }];

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
        "people" => "Cast & Characters",
        "movies-tv" => "Movies & TV",
        "appears-on" => "Appears On",
        _ => string.Join(" ", key.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..])),
    };

    private static string? GetValue(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

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
        => int.TryParse(value, out var parsed) ? parsed : null;

    private sealed record WorkContributorResult(IReadOnlyList<MediaEngine.Api.Models.CastCreditDto> CastCredits);
    private sealed record CanonicalPair(string Key, string Value);
    private sealed record OwnedFormatRow(Guid EditionId, string? FormatLabel, Guid AssetId, string FilePathRoot, string? AssetCoverUrl, string? EditionCoverUrl, string? Runtime, string? PageCount, string? Narrator);
    private sealed record CollectionDetailRow(Guid Id, string? DisplayName, string? WikidataQid, string? Description, string? Tagline, string? CoverUrl, string? BackgroundUrl, string? BannerUrl, string? LogoUrl);
    private sealed record SeriesRow(Guid WorkId, string Title, string? PositionLabel, string? ArtworkUrl);
    private sealed record CollectionWorkSummary(Guid Id, string MediaType, int? Ordinal, string Title, string? Description, string? Season, string? Episode, string? TrackNumber, string? Duration, string? Year, string? ArtworkUrl, string? BackgroundUrl);
    private sealed record CharacterDetailRow(Guid Id, string Label, string? WikidataQid, string? UniverseQid, string? UniverseLabel, string? ImageUrl, string? EntitySubType);
    private sealed record CharacterPortraitRow(Guid Id, string? ImageUrl, string? LocalImagePath, bool IsDefault);
}
