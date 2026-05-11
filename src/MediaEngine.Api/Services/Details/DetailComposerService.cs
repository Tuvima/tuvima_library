using Dapper;
using System.Globalization;
using System.Text;
using MediaEngine.Api.Endpoints;
using MediaEngine.Api.Services.Display;
using MediaEngine.Contracts.Details;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Details;

public sealed class DetailComposerService
{
    private static readonly Guid DefaultOwnerUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly IDatabaseConnection _db;
    private readonly ILibraryItemRepository _libraryItems;
    private readonly IPersonRepository _persons;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly ICanonicalValueArrayRepository _canonicalArrays;
    private readonly ISeriesManifestRepository _seriesManifests;

    public DetailComposerService(
        IDatabaseConnection db,
        ILibraryItemRepository libraryItems,
        IPersonRepository persons,
        IEntityAssetRepository entityAssets,
        ICanonicalValueArrayRepository canonicalArrays,
        ISeriesManifestRepository seriesManifests)
    {
        _db = db;
        _libraryItems = libraryItems;
        _persons = persons;
        _entityAssets = entityAssets;
        _canonicalArrays = canonicalArrays;
        _seriesManifests = seriesManifests;
    }

    public async Task<DetailPageViewModel?> BuildAsync(
        DetailEntityType entityType,
        Guid id,
        DetailPresentationContext context,
        CancellationToken ct = default,
        string? selectedSeriesId = null)
    {
        var isAdminView = context is DetailPresentationContext.Admin;

        return entityType switch
        {
            DetailEntityType.Person or DetailEntityType.MusicArtist => await BuildPersonAsync(id, entityType, context, isAdminView, ct),
            DetailEntityType.Collection or DetailEntityType.TvShow or DetailEntityType.MovieSeries or DetailEntityType.BookSeries
                or DetailEntityType.ComicSeries or DetailEntityType.MusicAlbum => await BuildCollectionAsync(id, entityType, context, isAdminView, ct),
            DetailEntityType.Character => await BuildCharacterAsync(id, context, isAdminView, ct),
            DetailEntityType.Universe => await BuildUniverseAsync(id, context, isAdminView, ct),
            _ => await BuildWorkAsync(id, entityType, context, isAdminView, selectedSeriesId, ct),
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
        string? selectedSeriesId,
        CancellationToken ct)
    {
        var detail = await _libraryItems.GetDetailAsync(workId, ct);
        if (detail is null)
            return null;

        var values = detail.CanonicalValues.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
        var entityType = requestedType == DetailEntityType.Work ? InferWorkEntityType(detail.MediaType, detail) : requestedType;
        var ownedFormats = await LoadOwnedFormatsAsync(workId, detail, ct);
        var heroProgress = BuildHeroProgress(entityType, detail.Runtime, ownedFormats);
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
            ownedCoverUrls.FirstOrDefault(),
            detail.CoverUrl,
            GetValue(values, "cover_url"),
            GetValue(values, "cover"),
            GetValue(values, "poster_url"),
            GetValue(values, "poster"),
            artworkFallback.CoverUrl,
            artworkFallback.SquareUrl);
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
        var seriesPlacement = await BuildSeriesPlacementAsync(workId, detail, entityType, selectedSeriesId, ct);
        var mediaGroups = await BuildWorkMediaGroupsAsync(workId, entityType, ct);
        var longDescription = ResolveLongDescription(detail.Description, values, entityType);
        var heroSummary = await BuildHeroSummaryAsync(detail.Tagline, longDescription, detail.WikidataQid, values, entityType, ct);

        return new DetailPageViewModel
        {
            Id = workId.ToString("D"),
            EntityType = entityType,
            PresentationContext = context,
            Title = entityType == DetailEntityType.TvEpisode
                ? FirstNonBlank(detail.EpisodeTitle, GetValue(values, MetadataFieldConstants.EpisodeTitle), detail.Title, detail.FileName, "Untitled")
                : FirstNonBlank(detail.Title, detail.EpisodeTitle, detail.FileName, "Untitled"),
            Subtitle = BuildSubtitle(detail, entityType, values, multiFormatState),
            Tagline = heroSummary,
            Description = longDescription,
            DescriptionAttribution = BuildWikipediaDescriptionAttribution(longDescription, GetValue(values, "wikipedia_url")),
            Artwork = artwork,
            HeroBrand = BuildHeroBrand(
                entityType,
                FirstNonBlank(GetValue(values, "network"), GetValue(values, "studio"), GetValue(values, "broadcaster")),
                FirstNonBlank(GetValue(values, "network_logo_url"), GetValue(values, "network_logo"), GetValue(values, "studio_logo_url"), GetValue(values, "broadcaster_logo_url"))),
            Progress = heroProgress,
            OwnedFormats = ownedFormats,
            MultiFormatState = multiFormatState,
            SyncCapability = BuildSyncCapability(workId, ownedFormats, multiFormatState),
            SeriesPlacement = seriesPlacement,
            Metadata = BuildMetadataPills(detail, entityType, values, ownedFormats),
            PrimaryActions = BuildPrimaryActions(workId, entityType, context, ownedFormats, heroProgress),
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
        var rootWorkId = await LoadCollectionRootWorkIdAsync(
            collectionId,
            requireRootWithChildren: entityType is DetailEntityType.TvShow or DetailEntityType.MovieSeries or DetailEntityType.BookSeries,
            ct);
        var rootValues = rootWorkId.HasValue
            ? await LoadCanonicalMapAsync(rootWorkId.Value, ct)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var values = MergeCanonicalMaps(collectionValues, rootValues);
        var longDescription = FirstText(
            GetValue(values, MetadataFieldConstants.Description),
            GetValue(values, "overview"),
            GetValue(values, "plot_summary"),
            row.Description);
        var heroSummary = await BuildHeroSummaryAsync(row.Tagline, longDescription, row.WikidataQid, values, entityType, ct);
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
        var contributorGroups = await BuildCollectionCreditsAsync(collectionId, rootWorkId, works, entityType, values, ct);
        var characterGroups = await BuildCollectionCharactersAsync(collectionId, row.WikidataQid, ct);
        var heroProgress = BuildCollectionHeroProgress(entityType, works);
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
            Title = ResolveCollectionTitle(entityType, row.DisplayName, rootValues, values),
            Subtitle = BuildCollectionSubtitle(entityType, works, values),
            Tagline = heroSummary,
            Description = longDescription,
            DescriptionAttribution = BuildWikipediaDescriptionAttribution(longDescription, GetValue(values, "wikipedia_url")),
            Artwork = artwork,
            HeroBrand = BuildHeroBrand(
                entityType,
                FirstNonBlank(row.HeroBrandLabel, GetValue(values, "network"), GetValue(values, "studio"), GetValue(values, "broadcaster")),
                FirstNonBlank(row.HeroBrandImageUrl, GetValue(values, "network_logo_url"), GetValue(values, "network_logo"), GetValue(values, "studio_logo_url"), GetValue(values, "broadcaster_logo_url"))),
            Progress = heroProgress,
            Metadata = BuildCollectionMetadata(entityType, works, values),
            PrimaryActions = BuildCollectionActions(collectionId, entityType, context, heroProgress),
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
        var aliases = await _persons.FindAliasesAsync(personId, ct);
        var groupMembers = await PersonCreditQueries.GetGroupMembersAsync(personId, person.IsGroup, _db, ct);
        var memberOfGroups = person.IsGroup
            ? []
            : await PersonCreditQueries.GetGroupMembersAsync(personId, false, _db, ct);
        var wikipediaUrl = await LoadPersonWikipediaUrlAsync(personId, ct);
        var artworkAssets = await _entityAssets.GetByEntityAsync(personId.ToString(), null, ct);
        var banner = PreferredAssetUrl(artworkAssets, "Banner");
        var background = PreferredAssetUrl(artworkAssets, "Background");
        var logo = PreferredAssetUrl(artworkAssets, "Logo");
        var portrait = ApiImageUrls.BuildPersonHeadshotUrl(person.Id, person.LocalHeadshotPath, person.HeadshotUrl);
        var relatedArt = credits.Select(c => c.CoverUrl).Where(url => !string.IsNullOrWhiteSpace(url)).Cast<string>().Take(8).ToList();
        var groups = BuildPersonCreditGroups(credits, context);
        var displayRoles = BuildPersonDisplayRoles(credits, person.Roles);
        var shortDescription = await LoadPersonShortDescriptionAsync(personId, person.WikidataQid, ct);

        return new DetailPageViewModel
        {
            Id = personId.ToString("D"),
            EntityType = entityType,
            PresentationContext = context,
            Title = person.Name,
            Subtitle = person.IsGroup ? "Group" : string.Join(" • ", displayRoles.Take(3)),
            Description = shortDescription,
            DescriptionAttribution = BuildWikipediaDescriptionAttribution(person.Biography, wikipediaUrl),
            PersonDetails = BuildPersonDetails(person, displayRoles, wikipediaUrl, aliases, groupMembers, memberOfGroups),
            Artwork = BuildArtwork(entityType, background, banner, null, null, portrait, new Dictionary<string, string>(), relatedArt, 0, null, logo),
            Metadata = BuildPersonMetadata(displayRoles, credits.Count),
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
                   (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key = 'narrator' LIMIT 1) AS Narrator,
                   us.progress_pct AS ProgressPct
            FROM editions e
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            LEFT JOIN user_states us ON us.asset_id = ma.id
                                    AND us.user_id = @defaultOwnerUserId
            WHERE e.work_id = @workId
              AND ma.status = 'Normal'
            ORDER BY COALESCE(e.format_label, ''), ma.file_path_root;
            """,
            new { workId = workId.ToString(), defaultOwnerUserId = DefaultOwnerUserId.ToString("D") },
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
                    Progress = null,
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
                Progress = BuildFormatProgress(row.ProgressPct),
                Actions = BuildFormatActions(workId, format),
            };
        }).ToList();
    }

    private async Task<WorkContributorResult> BuildWorkContributorsAsync(Guid workId, LibraryItemDetail detail, DetailEntityType entityType, CancellationToken ct)
    {
        var cast = entityType is DetailEntityType.Movie or DetailEntityType.TvEpisode or DetailEntityType.TvSeason or DetailEntityType.TvShow
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
            var entries = await LoadContributorEntriesAsync(workId, canonicalArrayKey, value, canonicalValues, ct);
            if (entries.Count == 0)
                return;

            var credits = new List<EntityCreditViewModel>();
            foreach (var entry in entries.Take(24))
            {
                var name = entry.Name;
                var qid = NormalizeQid(entry.Qid);
                var person = string.IsNullOrWhiteSpace(qid) ? null : await _persons.FindByQidAsync(qid, ct);
                person ??= await _persons.FindByNameAsync(name, ct);
                var imageUrl = person is null
                    ? FirstNonBlank(
                        GetValue(canonicalValues, $"{canonicalArrayKey}_headshot_url"),
                        GetValue(canonicalValues, $"{canonicalArrayKey}_image_url"),
                        GetValue(canonicalValues, $"{canonicalArrayKey}_photo_url"),
                        entries.Count == 1 ? GetValue(canonicalValues, "headshot_url") : null)
                    : ApiImageUrls.BuildPersonHeadshotUrl(person.Id, person.LocalHeadshotPath, person.HeadshotUrl);
                credits.Add(new EntityCreditViewModel
                {
                    EntityId = BuildPersonCreditEntityId(person?.Id, qid ?? person?.WikidataQid, name),
                    EntityType = RelatedEntityType.Person,
                    DisplayName = person?.Name ?? name,
                    ImageUrl = imageUrl,
                    FallbackInitials = Initials(person?.Name ?? name),
                    PrimaryRole = role,
                    SortOrder = entry.SortOrder,
                    IsPrimary = entry.SortOrder == 0,
                    IsCanonical = !string.IsNullOrWhiteSpace(qid ?? person?.WikidataQid),
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
        if (detail.MediaType.Equals("Music", StringComparison.OrdinalIgnoreCase))
            await AddTextCreditAsync("Artists", CreditGroupType.PrimaryArtists, detail.Artist, "Artist", "artist");
        await AddTextCreditAsync("Directors", CreditGroupType.Directors, detail.Director, "Director", "director");
        await AddTextCreditAsync("Writers", CreditGroupType.Writers, detail.Writer, "Writer", "screenwriter");
        await AddTextCreditAsync("Composers", CreditGroupType.MusicCredits, detail.Composer, "Composer", "composer");
        await AddTextCreditAsync("Illustrators", CreditGroupType.Illustrators, detail.Illustrator, "Illustrator", "illustrator");

        if (cast.Count > 0)
        {
            var castCredits = cast.Select((credit, index) => new EntityCreditViewModel
            {
                EntityId = BuildPersonCreditEntityId(credit.PersonId, credit.WikidataQid, credit.Name),
                EntityType = RelatedEntityType.Person,
                DisplayName = credit.Name,
                ImageUrl = credit.HeadshotUrl,
                FallbackInitials = Initials(credit.Name),
                PrimaryRole = "Actor",
                CharacterName = credit.Characters.FirstOrDefault()?.CharacterName,
                CharacterEntityId = credit.Characters.FirstOrDefault()?.FictionalEntityId.ToString("D"),
                CharacterImageUrl = credit.Characters.FirstOrDefault()?.PortraitUrl,
                SortOrder = index,
                IsPrimary = index < 8,
                IsCanonical = !string.IsNullOrWhiteSpace(credit.WikidataQid),
            }).ToList();

            if (castCredits.Count > 0)
            {
                groups.Add(new CreditGroupViewModel
                {
                    Title = "Actors",
                    GroupType = CreditGroupType.Cast,
                    Credits = castCredits,
                });
            }
        }

        return ApplyContributorGroupPresentation(entityType, groups);
    }

    private static IReadOnlyList<CreditGroupViewModel> ApplyContributorGroupPresentation(
        DetailEntityType entityType,
        IReadOnlyList<CreditGroupViewModel> groups)
    {
        return groups
            .Where(group => ShouldShowContributorGroup(entityType, group))
            .Select(group =>
            {
                var presentation = ResolveGroupPresentation(entityType, group);
                return new CreditGroupViewModel
                {
                    Title = presentation.Title,
                    GroupType = group.GroupType,
                    Credits = group.Credits,
                    DisplayPriority = presentation.Priority,
                    IsInitiallyExpanded = presentation.IsInitiallyExpanded,
                    InitialVisibleCount = presentation.InitialVisibleCount,
                };
            })
            .OrderBy(group => group.DisplayPriority)
            .ThenBy(group => group.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool ShouldShowContributorGroup(
        DetailEntityType entityType,
        CreditGroupViewModel group)
    {
        if (entityType is DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode)
            return group.GroupType == CreditGroupType.Cast;

        return true;
    }

    private static (string Title, int Priority, bool IsInitiallyExpanded, int InitialVisibleCount) ResolveGroupPresentation(
        DetailEntityType entityType,
        CreditGroupViewModel group)
    {
        var isVideo = entityType is DetailEntityType.Movie or DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode or DetailEntityType.Universe;
        if (isVideo)
        {
            if (group.GroupType == CreditGroupType.Directors)
                return ("Director", 0, true, 2);
            if (group.GroupType == CreditGroupType.Cast)
                return ("Actors", 1, true, 12);
            if (group.GroupType == CreditGroupType.Writers)
                return ("Writers", 2, false, 4);
            if (group.GroupType == CreditGroupType.Producers)
                return ("Producers", 3, false, 4);
            if (group.GroupType == CreditGroupType.MusicCredits)
                return ("Music", 4, false, 3);
            return (group.Title, 8, false, 4);
        }

        if (entityType is DetailEntityType.Book or DetailEntityType.Audiobook or DetailEntityType.Work)
        {
            return group.GroupType switch
            {
                CreditGroupType.Authors => (group.Title, 0, true, 8),
                CreditGroupType.Narrators => (group.Title, 1, true, 6),
                CreditGroupType.Illustrators => ("Contributors", 3, false, 4),
                CreditGroupType.Writers => ("Contributors", 3, false, 4),
                CreditGroupType.MusicCredits => ("Contributors", 4, false, 3),
                _ => (group.Title, 8, false, 4),
            };
        }

        if (entityType is DetailEntityType.ComicIssue or DetailEntityType.ComicSeries)
        {
            return group.GroupType switch
            {
                CreditGroupType.Writers => (group.Title, 0, true, 6),
                CreditGroupType.Illustrators => ("Artists", 1, true, 8),
                CreditGroupType.CreativeTeam => (group.Title, 2, false, 4),
                _ => (group.Title, 8, false, 4),
            };
        }

        if (entityType is DetailEntityType.MusicAlbum or DetailEntityType.MusicTrack or DetailEntityType.MusicArtist)
        {
            return group.GroupType switch
            {
                CreditGroupType.PrimaryArtists => (group.Title, 0, true, 8),
                CreditGroupType.FeaturedArtists => (group.Title, 1, true, 6),
                CreditGroupType.MusicCredits => ("Composers/Producers", 3, false, 4),
                _ => (group.Title, 8, false, 4),
            };
        }

        return group.GroupType == CreditGroupType.RelatedPeople
            ? (group.Title, 0, true, 8)
            : (group.Title, 8, false, 4);
    }

    private static IReadOnlyList<CreditGroupViewModel> SplitCastGroups(IReadOnlyList<EntityCreditViewModel> credits)
    {
        if (credits.Count == 0)
            return [];

        return
        [
            new CreditGroupViewModel
            {
                Title = "Actors",
                GroupType = CreditGroupType.Cast,
                Credits = credits,
            },
        ];
    }

    private async Task<IReadOnlyList<ContributorEntry>> LoadContributorEntriesAsync(
        Guid workId,
        string canonicalArrayKey,
        string? fallbackValue,
        IReadOnlyDictionary<string, string> canonicalValues,
        CancellationToken ct)
    {
        var targetIds = await LoadContributorTargetIdsAsync(workId, ct);

        foreach (var targetId in targetIds)
        {
            var arrayEntries = await _canonicalArrays.GetValuesAsync(targetId, canonicalArrayKey, ct);
            var entries = DeduplicateContributorEntries(arrayEntries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
                .OrderBy(entry => entry.Ordinal)
                .Select(entry => new ContributorEntry(
                    entry.Value.Trim(),
                    NormalizeQid(entry.ValueQid),
                    entry.Ordinal))
                .ToList());
            entries = await PreferCollectivePseudonymContributorAsync(canonicalArrayKey, entries, ct);
            if (entries.Count > 0)
                return entries;
        }

        foreach (var targetId in targetIds)
        {
            var claimEntries = await LoadContributorEntriesFromClaimsAsync(targetId, canonicalArrayKey, ct);
            claimEntries = await PreferCollectivePseudonymContributorAsync(canonicalArrayKey, claimEntries, ct);
            if (claimEntries.Count > 0)
                return claimEntries;
        }

        if (string.IsNullOrWhiteSpace(fallbackValue))
            return [];

        var fallbackEntries = SplitNames(fallbackValue)
            .Select((name, index) => new ContributorEntry(
                name,
                ResolveCompanionQidFromCanonical(canonicalValues, canonicalArrayKey, name, index),
                index))
            .ToList();

        return DeduplicateContributorEntries(fallbackEntries);
    }

    private async Task<IReadOnlyList<ContributorEntry>> PreferCollectivePseudonymContributorAsync(
        string canonicalArrayKey,
        IReadOnlyList<ContributorEntry> entries,
        CancellationToken ct)
    {
        if (!canonicalArrayKey.Equals("author", StringComparison.OrdinalIgnoreCase) || entries.Count <= 1)
            return entries;

        foreach (var entry in entries.OrderBy(entry => entry.SortOrder))
        {
            var qid = NormalizeQid(entry.Qid);
            if (string.IsNullOrWhiteSpace(qid))
                continue;

            var person = await _persons.FindByQidAsync(qid, ct);
            if (person?.IsPseudonym != true)
                continue;

            return entries
                .Where(candidate => string.Equals(NormalizeQid(candidate.Qid), qid, StringComparison.OrdinalIgnoreCase))
                .Select((candidate, index) => candidate with { SortOrder = index })
                .ToList();
        }

        return entries;
    }

    private async Task<IReadOnlyList<Guid>> LoadContributorTargetIdsAsync(Guid workId, CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<ContributorTargetRow>(new CommandDefinition(
            """
            SELECT w.id AS WorkId,
                   COALESCE(gp.id, p.id, w.id) AS RootWorkId,
                   MIN(ma.id) AS AssetId
            FROM works w
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            LEFT JOIN editions e ON e.work_id = w.id
            LEFT JOIN media_assets ma ON ma.edition_id = e.id
            WHERE w.id = @workId
            GROUP BY w.id, gp.id, p.id;
            """,
            new { workId = workId.ToString("D") },
            cancellationToken: ct));

        if (row is null)
            return [workId];

        var ids = new List<Guid>();
        AddId(row.RootWorkId);
        AddId(row.WorkId);
        AddId(row.AssetId);
        return ids;

        void AddId(Guid? id)
        {
            if (id.HasValue && !ids.Contains(id.Value))
                ids.Add(id.Value);
        }
    }

    private async Task<IReadOnlyList<ContributorEntry>> LoadContributorEntriesFromClaimsAsync(
        Guid entityId,
        string canonicalArrayKey,
        CancellationToken ct)
    {
        var qidKey = canonicalArrayKey + MetadataFieldConstants.CompanionQidSuffix;
        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync<ContributorClaimRow>(new CommandDefinition(
            """
            SELECT mc.rowid       AS RowNumber,
                   mc.claim_key   AS ClaimKey,
                   mc.claim_value AS ClaimValue
            FROM metadata_claims mc
            WHERE mc.entity_id = @entityId
              AND mc.claim_key IN @claimKeys
              AND NULLIF(mc.claim_value, '') IS NOT NULL
            ORDER BY mc.rowid;
            """,
            new { entityId = entityId.ToString("D"), claimKeys = new[] { canonicalArrayKey, qidKey } },
            cancellationToken: ct))).ToList();

        if (rows.Count == 0)
            return [];

        var nameClaims = rows
            .Where(row => row.ClaimKey.Equals(canonicalArrayKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var qidClaims = rows
            .Where(row => row.ClaimKey.Equals(qidKey, StringComparison.OrdinalIgnoreCase))
            .Select(row => ParseQidLabel(row.ClaimValue))
            .ToList();

        var qidByName = qidClaims
            .Where(parsed => !string.IsNullOrWhiteSpace(parsed.Label) && !string.IsNullOrWhiteSpace(parsed.Qid))
            .GroupBy(parsed => parsed.Label!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Qid, StringComparer.OrdinalIgnoreCase);

        var entries = new List<ContributorEntry>();
        if (qidByName.Count > 0)
        {
            foreach (var parsed in qidClaims)
            {
                var name = FirstNonBlank(parsed.Label, parsed.Qid);
                if (!string.IsNullOrWhiteSpace(name))
                    entries.Add(new ContributorEntry(name, parsed.Qid, entries.Count));
            }

            foreach (var claim in nameClaims)
            {
                var name = claim.ClaimValue.Trim();
                if (string.IsNullOrWhiteSpace(name)
                    || LooksLikeAggregateContributorName(name)
                    || qidByName.ContainsKey(name))
                    continue;

                entries.Add(new ContributorEntry(name, null, entries.Count));
            }

            return DeduplicateContributorEntries(entries);
        }

        for (var i = 0; i < nameClaims.Count; i++)
        {
            var name = nameClaims[i].ClaimValue.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            qidByName.TryGetValue(name, out var qid);
            qid ??= i < qidClaims.Count ? qidClaims[i].Qid : null;
            entries.Add(new ContributorEntry(name, qid, i));
        }

        foreach (var parsed in qidClaims)
        {
            var name = FirstNonBlank(parsed.Label, parsed.Qid);
            if (!string.IsNullOrWhiteSpace(name))
                entries.Add(new ContributorEntry(name, parsed.Qid, entries.Count));
        }

        return DeduplicateContributorEntries(entries);
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

    private async Task<SeriesPlacementViewModel?> BuildSeriesPlacementAsync(Guid workId, LibraryItemDetail detail, DetailEntityType entityType, string? requestedSeriesId, CancellationToken ct)
    {
        var availableSeries = ResolveSeriesPlacementOptions(detail, entityType);
        if (availableSeries.Count == 0)
        {
            var localSeries = await ResolveLocalSeriesOptionAsync(workId, entityType, detail.MediaType, ct);
            if (localSeries is null)
                return null;

            availableSeries = [localSeries];
        }

        var defaultSeriesQid = ExtractQid(GetDetailCanonicalValue(detail, "default_series_qid"));
        var requestedQid = ExtractQid(requestedSeriesId);
        var selectedSeries = availableSeries.FirstOrDefault(option =>
            !string.IsNullOrWhiteSpace(requestedQid)
            && string.Equals(option.SeriesId, requestedQid, StringComparison.OrdinalIgnoreCase))
            ?? availableSeries.FirstOrDefault(option =>
            !string.IsNullOrWhiteSpace(defaultSeriesQid)
            && string.Equals(option.SeriesId, defaultSeriesQid, StringComparison.OrdinalIgnoreCase))
            ?? availableSeries[0];
        var seriesTitle = selectedSeries.SeriesTitle;
        var seriesQid = ExtractQid(selectedSeries.SeriesId);

        using var conn = _db.CreateConnection();
        var rawRows = await conn.QueryAsync(new CommandDefinition(
            """
            WITH current_lineage AS (
                SELECT COALESCE(current_grandparent.id, current_parent.id, current_work.id) AS RootWorkId,
                       current_work.collection_id AS CollectionId
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
                 OR (current.CollectionId IS NOT NULL AND w.collection_id = current.CollectionId)
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
                series = seriesTitle,
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
        items = await MergeSeriesManifestPlaceholdersAsync(items, seriesQid, detail.WikidataQid, workId, entityType, ct);

        if (!items.Any(item => item.IsCurrent))
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

        items = SortSeriesItems(items);
        if (items.Count <= 1)
            return null;

        var currentIndex = Math.Max(0, items.FindIndex(i => i.IsCurrent));
        var current = items[currentIndex];
        return new SeriesPlacementViewModel
        {
            SeriesId = seriesQid ?? seriesTitle,
            SeriesTitle = seriesTitle,
            SelectedSeriesId = seriesQid ?? seriesTitle,
            CanChooseSeries = availableSeries.Count > 1,
            CanSetDefaultSeries = availableSeries.Count > 1 && !string.Equals(selectedSeries.SeriesId, defaultSeriesQid, StringComparison.OrdinalIgnoreCase),
            AvailableSeries = availableSeries.Select(option => new SeriesOptionViewModel
            {
                SeriesId = option.SeriesId,
                SeriesTitle = option.SeriesTitle,
                MediaScope = option.MediaScope,
                IsSelected = string.Equals(option.SeriesId, selectedSeries.SeriesId, StringComparison.OrdinalIgnoreCase),
                IsDefault = !string.IsNullOrWhiteSpace(defaultSeriesQid)
                    && string.Equals(option.SeriesId, defaultSeriesQid, StringComparison.OrdinalIgnoreCase),
            }).ToList(),
            UniverseId = detail.UniverseSummary?.UniverseQid,
            UniverseTitle = detail.UniverseSummary?.UniverseName,
            PositionNumber = current.PositionNumber,
            TotalKnownItems = items.Count,
            PositionLabel = BuildSeriesPositionLabel(entityType, current.PositionNumber, items.Count, seriesTitle),
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

    private static IReadOnlyList<SeriesOptionViewModel> ResolveSeriesPlacementOptions(LibraryItemDetail detail, DetailEntityType entityType)
    {
        var mediaScope = SeriesMediaFilter(entityType, detail.MediaType);
        var options = new List<SeriesOptionViewModel>();
        var seriesTitle = FirstText(detail.Series, GetDetailCanonicalValue(detail, MetadataFieldConstants.Series));
        var defaultSeriesQid = ExtractQid(GetDetailCanonicalValue(detail, "default_series_qid"));
        var defaultSeriesTitle = FirstText(GetDetailCanonicalValue(detail, "default_series_label"), GetDetailCanonicalValue(detail, "default_series"));

        AddSeriesOption(options, defaultSeriesQid, defaultSeriesTitle, mediaScope);
        AddSeriesOption(options, ExtractQid(GetDetailCanonicalValue(detail, "series_qid")), seriesTitle, mediaScope);
        AddSeriesOption(options, ExtractQid(GetDetailCanonicalValue(detail, "part_of_the_series_qid")), seriesTitle, mediaScope);
        AddSeriesOption(options, ExtractQid(GetDetailCanonicalValue(detail, "part_of_series_qid")), seriesTitle, mediaScope);

        if (options.Count == 0 && !string.IsNullOrWhiteSpace(seriesTitle))
            AddSeriesOption(options, seriesTitle, seriesTitle, mediaScope);

        return options;
    }

    private static void AddSeriesOption(List<SeriesOptionViewModel> options, string? seriesId, string? title, string mediaScope)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
            return;

        if (options.Any(option => string.Equals(option.SeriesId, seriesId, StringComparison.OrdinalIgnoreCase)))
            return;

        options.Add(new SeriesOptionViewModel
        {
            SeriesId = seriesId.Trim(),
            SeriesTitle = FirstText(title, seriesId) ?? "Series",
            MediaScope = mediaScope,
        });
    }

    private async Task<SeriesOptionViewModel?> ResolveLocalSeriesOptionAsync(Guid workId, DetailEntityType entityType, string mediaType, CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(
            """
            WITH current_lineage AS (
                SELECT COALESCE(current_grandparent.id, current_parent.id, current_work.id) AS RootWorkId,
                       current_work.collection_id AS CollectionId
                FROM works current_work
                LEFT JOIN works current_parent ON current_parent.id = current_work.parent_work_id
                LEFT JOIN works current_grandparent ON current_grandparent.id = current_parent.parent_work_id
                WHERE current_work.id = @workId
            )
            SELECT CAST(COALESCE(
                (SELECT display_name FROM collections c WHERE c.id = current.CollectionId LIMIT 1),
                (SELECT value FROM canonical_values WHERE entity_id = current.RootWorkId AND key = 'series' LIMIT 1),
                (SELECT value FROM canonical_values WHERE entity_id = current.RootWorkId AND key = 'title' LIMIT 1)
            ) AS TEXT) AS SeriesTitle,
            CAST((SELECT wikidata_qid FROM collections c WHERE c.id = current.CollectionId LIMIT 1) AS TEXT) AS SeriesQid
            FROM current_lineage current;
            """,
            new { workId = workId.ToString("D") },
            cancellationToken: ct));

        var title = StringValue(row?.SeriesTitle);
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var qid = ExtractQid(StringValue(row?.SeriesQid));
        return new SeriesOptionViewModel
        {
            SeriesId = qid ?? title,
            SeriesTitle = title,
            MediaScope = SeriesMediaFilter(entityType, mediaType),
        };
    }

    private static string? GetDetailCanonicalValue(LibraryItemDetail detail, string key)
        => detail.CanonicalValues.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

    private async Task<List<SeriesItemViewModel>> MergeSeriesManifestPlaceholdersAsync(
        IReadOnlyList<SeriesItemViewModel> items,
        string? seriesQid,
        string? currentWorkQid,
        Guid currentWorkId,
        DetailEntityType entityType,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(seriesQid))
            return items.ToList();

        var manifestItems = await _seriesManifests.GetItemsBySeriesQidAsync(seriesQid, ct);
        var scopedManifestItems = manifestItems
            .Where(item => IsManifestItemInMediaScope(item, entityType))
            .ToList();
        var connectedManifestItems = BuildConnectedManifestSubset(scopedManifestItems, currentWorkQid);
        if (connectedManifestItems.Count > 1)
            scopedManifestItems = connectedManifestItems;

        if (scopedManifestItems.Count > 0)
            return MergeManifestItems(items, scopedManifestItems, currentWorkQid, currentWorkId, entityType);

        return await MergeLegacySeriesMemberPlaceholdersAsync(items, seriesQid, entityType, ct);
    }

    private static List<SeriesManifestItemRecord> BuildConnectedManifestSubset(
        IReadOnlyList<SeriesManifestItemRecord> manifestItems,
        string? currentWorkQid)
    {
        var qid = ExtractQid(currentWorkQid);
        if (string.IsNullOrWhiteSpace(qid))
            return [];

        var byQid = manifestItems
            .Where(item => !string.IsNullOrWhiteSpace(item.ItemQid))
            .GroupBy(item => item.ItemQid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (!byQid.ContainsKey(qid))
            return [];

        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { qid };
        var pending = new Queue<string>();
        pending.Enqueue(qid);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!byQid.TryGetValue(current, out var item))
                continue;

            foreach (var neighbor in new[] { item.PreviousQid, item.NextQid }.Select(ExtractQid).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>())
            {
                if (byQid.ContainsKey(neighbor) && connected.Add(neighbor))
                    pending.Enqueue(neighbor);
            }

            foreach (var inbound in manifestItems.Where(candidate =>
                string.Equals(ExtractQid(candidate.PreviousQid), current, StringComparison.OrdinalIgnoreCase)
                || string.Equals(ExtractQid(candidate.NextQid), current, StringComparison.OrdinalIgnoreCase)))
            {
                if (connected.Add(inbound.ItemQid))
                    pending.Enqueue(inbound.ItemQid);
            }
        }

        return manifestItems
            .Where(item => connected.Contains(item.ItemQid))
            .ToList();
    }

    private static List<SeriesItemViewModel> MergeManifestItems(
        IReadOnlyList<SeriesItemViewModel> items,
        IReadOnlyList<SeriesManifestItemRecord> manifestItems,
        string? currentWorkQid,
        Guid currentWorkId,
        DetailEntityType entityType)
    {
        var merged = items.ToList();
        var currentQid = ExtractQid(currentWorkQid);
        var ownedPositions = merged
            .Where(item => item.PositionNumber.HasValue)
            .Select(item => item.PositionNumber!.Value)
            .ToHashSet();
        var ownedQids = merged
            .Select(item => ExtractQid(item.Id))
            .Where(qid => !string.IsNullOrWhiteSpace(qid))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var manifestItem in manifestItems)
        {
            var position = manifestItem.ParsedOrdinal.HasValue
                ? (int?)Convert.ToInt32(Math.Round(manifestItem.ParsedOrdinal.Value, MidpointRounding.AwayFromZero))
                : TryParseSeriesPosition(FirstNonBlank(manifestItem.RawOrdinal, manifestItem.SortOrder?.ToString(CultureInfo.InvariantCulture)));
            var isLinkedOwned = manifestItem.LinkedWorkId.HasValue;

            if ((isLinkedOwned || string.Equals(manifestItem.ItemQid, currentQid, StringComparison.OrdinalIgnoreCase))
                && TryApplyManifestPositionToOwnedItem(merged, manifestItem, position, currentWorkId))
            {
                if (position.HasValue)
                    ownedPositions.Add(position.Value);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(manifestItem.ItemQid) && ownedQids.Contains(manifestItem.ItemQid))
                continue;

            if (position.HasValue && ownedPositions.Contains(position.Value))
                continue;

            merged.Add(new SeriesItemViewModel
            {
                Id = $"missing-{manifestItem.ItemQid}",
                EntityType = entityType,
                Title = FirstNonBlank(manifestItem.ItemLabel, manifestItem.ItemQid) ?? "Missing from library",
                PositionNumber = position,
                PositionLabel = position?.ToString(CultureInfo.InvariantCulture) ?? manifestItem.RawOrdinal,
                IsOwned = false,
                ProgressState = LibraryProgressState.Unknown,
            });
        }

        return merged
            .OrderBy(item => item.PositionNumber ?? int.MaxValue)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryApplyManifestPositionToOwnedItem(
        List<SeriesItemViewModel> items,
        SeriesManifestItemRecord manifestItem,
        int? position,
        Guid currentWorkId)
    {
        var index = items.FindIndex(item =>
            (manifestItem.LinkedWorkId.HasValue && string.Equals(item.Id, manifestItem.LinkedWorkId.Value.ToString("D"), StringComparison.OrdinalIgnoreCase))
            || (item.IsCurrent && currentWorkId != Guid.Empty && string.Equals(item.Id, currentWorkId.ToString("D"), StringComparison.OrdinalIgnoreCase)));
        if (index < 0)
            return false;

        var item = items[index];
        if (item.PositionNumber.HasValue && !string.IsNullOrWhiteSpace(item.PositionLabel))
            return true;

        items[index] = new SeriesItemViewModel
        {
            Id = item.Id,
            EntityType = item.EntityType,
            Title = item.Title,
            ArtworkUrl = item.ArtworkUrl,
            PositionNumber = item.PositionNumber ?? position,
            PositionLabel = FirstNonBlank(item.PositionLabel, position?.ToString(CultureInfo.InvariantCulture), manifestItem.RawOrdinal),
            IsCurrent = item.IsCurrent,
            IsOwned = item.IsOwned,
            ProgressState = item.ProgressState,
        };
        return true;
    }

    private static bool IsManifestItemInMediaScope(SeriesManifestItemRecord item, DetailEntityType entityType)
    {
        if (item.LinkedWorkId.HasValue)
            return true;

        if (item.IsCollection)
            return false;

        var text = string.Join(' ', new[]
        {
            item.MediaType,
            item.ItemDescription,
            item.ParentCollectionLabel,
            item.SourcePropertiesJson,
            item.RelationshipsJson,
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        if (string.IsNullOrWhiteSpace(text))
            return true;

        return entityType switch
        {
            DetailEntityType.Movie or DetailEntityType.MovieSeries => ContainsAny(text, "film", "movie")
                && !ContainsAny(text, "short film", "television", "episode", "video game", "novel", "book", "comic"),
            DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode => ContainsAny(text, "television", "tv series", "episode", "season"),
            DetailEntityType.ComicIssue or DetailEntityType.ComicSeries => ContainsAny(text, "comic", "graphic novel", "manga"),
            DetailEntityType.Audiobook => ContainsAny(text, "audiobook", "audio book", "book", "novel"),
            DetailEntityType.Book or DetailEntityType.BookSeries or DetailEntityType.Work => ContainsAny(text, "book", "novel", "literary", "written work")
                && !ContainsAny(text, "comic", "film", "movie", "television", "video game"),
            DetailEntityType.MusicAlbum or DetailEntityType.MusicTrack or DetailEntityType.MusicArtist => ContainsAny(text, "album", "song", "single", "music"),
            _ => true,
        };
    }

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private async Task<List<SeriesItemViewModel>> MergeLegacySeriesMemberPlaceholdersAsync(
        IReadOnlyList<SeriesItemViewModel> items,
        string seriesQid,
        DetailEntityType entityType,
        CancellationToken ct)
    {
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
                   CAST(COALESCE(NULLIF(artist_asset.value, ''), NULLIF(artist_work.value, ''), NULLIF(artist_root.value, '')) AS TEXT) AS Artist,
                   CAST(COALESCE(NULLIF(explicit_asset.value, ''), NULLIF(explicit_work.value, '')) AS TEXT) AS Explicit,
                   CAST(COALESCE(NULLIF(quality_asset.value, ''), NULLIF(quality_work.value, ''), NULLIF(quality_root.value, '')) AS TEXT) AS Quality,
                   CAST(COALESCE(NULLIF(cover_asset.value, ''), NULLIF(poster_asset.value, ''), NULLIF(cover_work.value, ''), NULLIF(poster_work.value, ''), NULLIF(cover_root.value, ''), NULLIF(poster_root.value, '')) AS TEXT) AS ArtworkUrl,
                   CAST(COALESCE(NULLIF(still_asset.value, ''), NULLIF(still_work.value, ''), NULLIF(bg_asset.value, ''), NULLIF(bg_work.value, ''), NULLIF(hero_asset.value, ''), NULLIF(hero_work.value, ''), NULLIF(banner_asset.value, ''), NULLIF(banner_work.value, ''), NULLIF(bg_root.value, ''), NULLIF(hero_root.value, ''), NULLIF(banner_root.value, '')) AS TEXT) AS BackgroundUrl,
                   CAST(COALESCE(NULLIF(cover_state_asset.value, ''), NULLIF(cover_state_work.value, ''), NULLIF(cover_state_root.value, '')) AS TEXT) AS CoverState,
                   CAST(COALESCE(NULLIF(bg_state_asset.value, ''), NULLIF(bg_state_work.value, ''), NULLIF(hero_state_asset.value, ''), NULLIF(hero_state_work.value, ''), NULLIF(banner_state_asset.value, ''), NULLIF(banner_state_work.value, ''), NULLIF(bg_state_root.value, ''), NULLIF(hero_state_root.value, ''), NULLIF(banner_state_root.value, '')) AS TEXT) AS BackgroundState,
                   MAX(us.progress_pct) AS ProgressPercent
            FROM works w
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            LEFT JOIN editions e ON e.work_id = w.id
            LEFT JOIN media_assets ma ON ma.edition_id = e.id
            LEFT JOIN user_states us ON us.asset_id = ma.id
                                    AND us.user_id = @defaultOwnerUserId
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
            LEFT JOIN canonical_values artist_asset ON artist_asset.entity_id = ma.id AND artist_asset.key IN ('artist', 'album_artist')
            LEFT JOIN canonical_values artist_work ON artist_work.entity_id = w.id AND artist_work.key IN ('artist', 'album_artist')
            LEFT JOIN canonical_values artist_root ON artist_root.entity_id = COALESCE(gp.id, p.id, w.id) AND artist_root.key IN ('artist', 'album_artist')
            LEFT JOIN canonical_values explicit_asset ON explicit_asset.entity_id = ma.id AND explicit_asset.key IN ('explicit', 'is_explicit')
            LEFT JOIN canonical_values explicit_work ON explicit_work.entity_id = w.id AND explicit_work.key IN ('explicit', 'is_explicit')
            LEFT JOIN canonical_values quality_asset ON quality_asset.entity_id = ma.id AND quality_asset.key IN ('quality', 'audio_quality')
            LEFT JOIN canonical_values quality_work ON quality_work.entity_id = w.id AND quality_work.key IN ('quality', 'audio_quality')
            LEFT JOIN canonical_values quality_root ON quality_root.entity_id = COALESCE(gp.id, p.id, w.id) AND quality_root.key IN ('quality', 'audio_quality')
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
            new { collectionId = collectionId.ToString(), defaultOwnerUserId = DefaultOwnerUserId.ToString("D") },
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
            StringValue(row.Artist),
            IsTruthy(StringValue(row.Explicit)),
            StringValue(row.Quality),
            DoubleValue(row.ProgressPercent),
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

    private async Task<Guid?> LoadCollectionRootWorkIdAsync(
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

        return Guid.TryParse(rootId, out var rootGuid) ? rootGuid : null;
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
        DetailEntityType entityType,
        CancellationToken ct)
    {
        var shortDescription = FirstText(
            GetValue(canonicalValues, MetadataFieldConstants.ShortDescription),
            IsWatchEntity(entityType) ? tagline : null);

        if (!string.IsNullOrWhiteSpace(shortDescription))
            return Task.FromResult(NormalizeHeroSummary(shortDescription));

        var canonicalSummary = FirstText(
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
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('cover_url', 'cover', 'poster_url', 'poster') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('cover_url', 'cover', 'poster_url', 'poster') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('cover_url', 'cover', 'poster_url', 'poster') LIMIT 1)) AS CoverUrl,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('square_url', 'square') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('square_url', 'square') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('square_url', 'square') LIMIT 1)) AS SquareUrl,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('background_url', 'background') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('background_url', 'background') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('background_url', 'background') LIMIT 1)) AS BackgroundUrl,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('banner_url', 'banner') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('banner_url', 'banner') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('banner_url', 'banner') LIMIT 1)) AS BannerUrl,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'cover_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key = 'cover_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'cover_state' LIMIT 1)) AS CoverState,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'square_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key = 'square_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'square_state' LIMIT 1)) AS SquareState,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'background_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key = 'background_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'background_state' LIMIT 1)) AS BackgroundState,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'banner_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key = 'banner_state' LIMIT 1),
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
        var resolvedLogoUrl = FirstNonBlank(logoUrl, GetValue(values, "clear_logo_url"), GetValue(values, "clear_logo"), GetValue(values, "logo_url"), GetValue(values, "logo"));
        var heroArtwork = HeroArtworkResolver.Resolve(entityType, backdropUrl, bannerUrl, coverUrl, posterUrl, portraitUrl, characterImageUrl, relatedArtwork, resolvedLogoUrl);

        return new ArtworkSet
        {
            BackdropUrl = backdropUrl,
            BannerUrl = bannerUrl,
            PosterUrl = posterUrl,
            CoverUrl = coverUrl,
            LogoUrl = resolvedLogoUrl,
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

    private static IReadOnlyList<MetadataPill> BuildMetadataPills(
        LibraryItemDetail detail,
        DetailEntityType entityType,
        IReadOnlyDictionary<string, string> canonicalValues,
        IReadOnlyList<OwnedFormatViewModel> formats)
    {
        var pills = new List<MetadataPill>();
        AddPlain(pills, FirstNonBlank(GetValue(canonicalValues, "content_rating"), GetValue(canonicalValues, "certification")), "content_rating");
        AddPlain(pills, FormatRating(detail.Rating), "rating");

        foreach (var genre in SplitMetadataValues(detail.Genre).Take(3))
        {
            pills.Add(new MetadataPill
            {
                Label = genre,
                Kind = "genre",
                Route = $"/search?genre={Uri.EscapeDataString(genre)}",
                Tooltip = $"Browse {genre}",
            });
        }

        AddPlain(pills, FormatEntityType(entityType), "type");
        AddPlain(pills, detail.Year, "year");
        AddPlain(pills, FormatRuntime(detail.Runtime), "duration");
        AddPlain(pills, FormatCountLabel(GetValue(canonicalValues, "page_count"), "page"), "page_count");
        AddPlain(pills, FormatCountLabel(GetValue(canonicalValues, "track_count"), "track"), "track_count");
        AddPlain(pills, FormatCountLabel(GetValue(canonicalValues, "season_count"), "season"), "season_count");
        AddPlain(pills, FormatCountLabel(GetValue(canonicalValues, "episode_count"), "episode"), "episode_count");
        AddPlain(pills, ResolveWatchQualityLabel(canonicalValues, detail.PlaybackSummary), "quality");
        if (HasSubtitles(canonicalValues, detail.PlaybackSummary))
            AddPlain(pills, "CC", "subtitles");
        AddPlain(pills, detail.Language, "audio");
        if (HasReadListenCompanion(entityType, formats))
            AddPlain(pills, BuildReadListenAvailabilityLabel(entityType, formats), "sync");

        return pills
            .Where(value => !string.IsNullOrWhiteSpace(value.Label))
            .DistinctBy(value => value.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ProgressViewModel? BuildFormatProgress(double? progressPct)
    {
        if (progressPct is not > 0)
            return null;

        var percent = Math.Clamp(progressPct.Value, 0, 100);
        return new ProgressViewModel
        {
            Percent = percent,
            Label = $"{Math.Max(1, percent):F0}%",
        };
    }

    private static ProgressViewModel? BuildHeroProgress(
        DetailEntityType entityType,
        string? runtime,
        IReadOnlyList<OwnedFormatViewModel> formats)
    {
        if (!IsWatchEntity(entityType))
            return null;

        var progress = formats
            .Select(format => format.Progress)
            .Where(value => value?.Percent is > 0 and < 99.5)
            .OrderByDescending(value => value!.Percent)
            .FirstOrDefault();
        if (progress is null)
            return null;

        var percent = Math.Clamp(progress.Percent, 0, 100);
        var runtimeSource = FirstNonBlank(formats.Select(format => format.Runtime).Prepend(runtime).ToArray());
        return new ProgressViewModel
        {
            Percent = percent,
            Label = BuildHeroProgressLabel(percent, runtimeSource),
        };
    }

    private static ProgressViewModel? BuildCollectionHeroProgress(
        DetailEntityType entityType,
        IReadOnlyList<CollectionWorkSummary> works)
    {
        if (!IsWatchEntity(entityType))
            return null;

        var item = works
            .Where(work => work.ProgressPercent is > 0 and < 99.5)
            .OrderByDescending(work => work.ProgressPercent)
            .FirstOrDefault();
        if (item is null || item.ProgressPercent is null)
            return null;

        var percent = Math.Clamp(item.ProgressPercent.Value, 0, 100);
        return new ProgressViewModel
        {
            Percent = percent,
            Label = BuildHeroProgressLabel(percent, item.Duration),
        };
    }

    private static string BuildHeroProgressLabel(double percent, string? runtime)
    {
        var rounded = Math.Clamp((int)Math.Round(percent, MidpointRounding.AwayFromZero), 1, 99);
        var timeLeft = FormatTimeLeft(runtime, percent);
        return string.IsNullOrWhiteSpace(timeLeft)
            ? $"Continue watching · {rounded}% watched"
            : $"Continue watching · {rounded}% watched · {timeLeft} left";
    }

    private static bool IsWatchEntity(DetailEntityType entityType)
        => entityType is DetailEntityType.Movie or DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode;

    private static IReadOnlyList<DetailAction> BuildPrimaryActions(Guid id, DetailEntityType entityType, DetailPresentationContext context, IReadOnlyList<OwnedFormatViewModel> formats, ProgressViewModel? heroProgress)
    {
        return entityType switch
        {
            DetailEntityType.Movie => [new DetailAction { Key = "watch", Label = heroProgress is null ? "Watch" : "Continue Watching", Icon = "play_arrow", Route = $"/watch/player/resolve?workId={id}", IsPrimary = true }],
            DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode => [new DetailAction { Key = "watch", Label = heroProgress is null ? "Watch" : "Continue Watching", Icon = "play_arrow", IsPrimary = true }],
            DetailEntityType.Book or DetailEntityType.ComicIssue => [new DetailAction { Key = "read", Label = "Read", Icon = "menu_book", Route = $"/book/{id}", IsPrimary = true }],
            DetailEntityType.Audiobook => [new DetailAction { Key = "listen", Label = "Listen", Icon = "headphones", Route = $"/listen/audiobook/{id}", IsPrimary = true }],
            DetailEntityType.Work when formats.Any(f => f.FormatType == MediaFormatType.Ebook) => [new DetailAction { Key = "read", Label = "Read", Icon = "menu_book", Route = $"/book/{id}", IsPrimary = true }],
            DetailEntityType.Work when formats.Any(f => f.FormatType == MediaFormatType.Audiobook) => [new DetailAction { Key = "listen", Label = "Listen", Icon = "headphones", Route = $"/listen/audiobook/{id}", IsPrimary = true }],
            DetailEntityType.MusicAlbum => [new DetailAction { Key = "play-album", Label = "Play", Icon = "play_arrow", IsPrimary = true }],
            DetailEntityType.MusicArtist => [new DetailAction { Key = "play-artist", Label = "Play", Icon = "play_arrow", IsPrimary = true }],
            _ => [new DetailAction { Key = "open", Label = "Open", Icon = "open_in_new", IsPrimary = true }],
        };
    }

    private static IReadOnlyList<DetailAction> BuildSecondaryActions(Guid id, DetailEntityType entityType, IReadOnlyList<OwnedFormatViewModel>? formats = null)
    {
        var actions = new List<DetailAction>();

        var hasReadListenCompanion = HasReadListenCompanion(entityType, formats ?? []);
        var isReadableEntity = IsReadableEntity(entityType);

        if (hasReadListenCompanion)
        {
            actions.Add(new DetailAction
            {
                Key = "read-listen",
                Label = "Read + Listen",
                Subtitle = "Continue seamlessly between reading and listening",
                Icon = "read_listen",
                Tooltip = "Unified Read + Listen is waiting on sync enablement",
                IsDisabled = true,
                IsStub = true,
                DisplayStyle = "premium",
            });
        }

        if (IsWatchEntity(entityType))
        {
            actions.Add(new DetailAction
            {
                Key = "watch-party",
                Label = "Watch Party",
                Subtitle = "Watch together",
                Icon = "groups",
                Tooltip = "Watch Party setup is coming soon",
                IsStub = true,
                DisplayStyle = "icon",
            });
            actions.Add(BuildReactionAction());
            actions.Add(new DetailAction
            {
                Key = "add-to-collection",
                Label = "Watchlist",
                Icon = "add",
                Tooltip = "Add to watchlist",
                DisplayStyle = "icon",
            });

            return actions;
        }

        if (entityType == DetailEntityType.MusicAlbum)
        {
            actions.Add(new DetailAction
            {
                Key = "shuffle",
                Label = "Shuffle",
                Icon = "shuffle",
                Tooltip = "Shuffle album",
                DisplayStyle = "icon",
            });
            actions.Add(new DetailAction
            {
                Key = "save",
                Label = "Save",
                Icon = "playlist_add",
                Tooltip = "Save album",
                DisplayStyle = "icon",
            });
            return actions;
        }

        actions.Add(new DetailAction
        {
            Key = "add-to-collection",
            Label = entityType switch
            {
                DetailEntityType.Book or DetailEntityType.ComicIssue => "Want to Read",
                DetailEntityType.Audiobook => "Want to Listen",
                _ => "Add to collection",
            },
            Icon = "add",
            Tooltip = entityType switch
            {
                DetailEntityType.Book or DetailEntityType.ComicIssue => "Want to Read",
                DetailEntityType.Audiobook => "Want to Listen",
                _ => "Add to collection",
            },
            DisplayStyle = isReadableEntity ? "button" : "icon",
        });

        if (isReadableEntity)
        {
            actions.Add(BuildReactionAction());
            return actions;
        }

        actions.Add(BuildReactionAction());

        return actions;
    }

    private static DetailAction BuildReactionAction()
        => new()
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
        };

    private static bool HasReadListenCompanion(DetailEntityType entityType, IReadOnlyList<OwnedFormatViewModel> formats)
        => entityType is DetailEntityType.Book or DetailEntityType.Audiobook or DetailEntityType.Work
           && formats.Any(f => f.FormatType == MediaFormatType.Ebook)
           && formats.Any(f => f.FormatType == MediaFormatType.Audiobook);

    private static bool IsReadableEntity(DetailEntityType entityType)
        => entityType is DetailEntityType.Book or DetailEntityType.ComicIssue or DetailEntityType.Audiobook or DetailEntityType.Work;

    private static string BuildReadListenAvailabilityLabel(DetailEntityType entityType, IReadOnlyList<OwnedFormatViewModel> formats)
    {
        if (entityType == DetailEntityType.Audiobook)
            return "Ebook available";

        var audiobook = formats.FirstOrDefault(f => f.FormatType == MediaFormatType.Audiobook);
        var runtime = FormatRuntime(audiobook?.Runtime);
        return string.IsNullOrWhiteSpace(runtime)
            ? "Audiobook available"
            : $"Audiobook available · {runtime}";
    }

    private static bool SupportsWatchParty(DetailEntityType entityType)
    {
        _ = entityType;
        // TODO: Replace the safe Movie/TV hero stub with a real playback capability flag when group watch is added.
        return false;
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
            DetailEntityType.TvShow => ["episodes", "overview", "cast", "universe", "details"],
            DetailEntityType.TvSeason => ["episodes", "overview", "cast", "details"],
            DetailEntityType.Movie when hasSeries => ["overview", "cast", "universe", "related", "details"],
            DetailEntityType.Movie => ["overview", "cast", "universe", "related", "details"],
            DetailEntityType.TvEpisode => ["overview", "cast", "characters", "universe", "details"],
            DetailEntityType.Book or DetailEntityType.Audiobook when hasSeries => ["overview", "credits", "chapters", "universe", "editions", "details"],
            DetailEntityType.Book or DetailEntityType.Audiobook => ["overview", "credits", "chapters", "universe", "editions", "details"],
            DetailEntityType.Work when hasSeries => ["overview", "credits", "formats", "chapters", "universe", "editions", "details"],
            DetailEntityType.Work => ["overview", "credits", "formats", "chapters", "universe", "editions", "details"],
            DetailEntityType.ComicIssue when hasSeries => ["overview", "credits", "universe", "editions", "details"],
            DetailEntityType.ComicIssue => ["overview", "credits", "universe", "editions", "details"],
            DetailEntityType.MusicAlbum => ["tracks", "credits", "related", "details"],
            DetailEntityType.MusicTrack => ["overview", "credits", "related", "details"],
            DetailEntityType.MusicArtist when context == DetailPresentationContext.Listen => ["overview", "albums", "tracks", "appears-on", "credits", "related", "details"],
            DetailEntityType.Person => ["details"],
            DetailEntityType.Character => ["overview", "appearances", "portrayals", "relationships", "universe", "details"],
            DetailEntityType.Universe => ["overview", "timeline", "media", "characters", "people", "relationships", "details"],
            _ when hasSeries => ["overview", "people", "characters", "universe", "related", "details"],
            _ => ["overview", "people", "characters", "universe", "related", "details"],
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

    private static string? FormatCountLabel(string? value, string singular)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            return trimmed;

        var label = count == 1 ? singular : singular + "s";
        return $"{count.ToString(CultureInfo.InvariantCulture)} {label}";
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

    private static string? ResolveWatchQualityLabel(
        IReadOnlyDictionary<string, string> canonicalValues,
        PlaybackTechnicalSummary? playbackSummary)
    {
        var explicitQuality = FirstNonBlank(GetValue(canonicalValues, "quality"), GetValue(canonicalValues, "video_quality"));
        if (!string.IsNullOrWhiteSpace(explicitQuality))
            return NormalizeWatchQualityLabel(explicitQuality);

        return NormalizeWatchQualityLabel(playbackSummary?.VideoResolutionLabel);
    }

    private static string? NormalizeWatchQualityLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        return normalized.Equals("2160p", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("UHD", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Ultra HD", StringComparison.OrdinalIgnoreCase)
            ? "4K"
            : normalized;
    }

    private static bool HasSubtitles(
        IReadOnlyDictionary<string, string> canonicalValues,
        PlaybackTechnicalSummary? playbackSummary)
        => !string.IsNullOrWhiteSpace(GetValue(canonicalValues, "subtitle_languages"))
            || !string.IsNullOrWhiteSpace(GetValue(canonicalValues, "subtitles"))
            || !string.IsNullOrWhiteSpace(playbackSummary?.SubtitleSummary)
            || playbackSummary?.SubtitleLanguages.Count > 0;

    private static string? FormatRuntime(string? runtime)
    {
        if (string.IsNullOrWhiteSpace(runtime))
            return null;

        var trimmed = runtime.Trim();
        if (!double.TryParse(trimmed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minutes))
            return trimmed;

        if (minutes <= 0)
            return null;

        var totalMinutes = (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
        var hours = totalMinutes / 60;
        var remainingMinutes = totalMinutes % 60;

        return hours > 0
            ? remainingMinutes > 0 ? $"{hours}h {remainingMinutes}m" : $"{hours}h"
            : $"{totalMinutes}m";
    }

    private static string? FormatTimeLeft(string? runtime, double progressPercent)
    {
        var totalSeconds = TryParseDurationSeconds(runtime);
        if (totalSeconds is null or <= 0)
            return null;

        var remainingSeconds = totalSeconds.Value * (100d - Math.Clamp(progressPercent, 0, 100)) / 100d;
        if (remainingSeconds <= 60)
            return null;

        var remainingMinutes = (int)Math.Ceiling(remainingSeconds / 60d);
        var hours = remainingMinutes / 60;
        var minutes = remainingMinutes % 60;
        return hours > 0
            ? minutes > 0 ? $"{hours}h {minutes:D2}m" : $"{hours}h"
            : $"{remainingMinutes}m";
    }

    private static int? TryParseDurationSeconds(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            return null;

        var trimmed = duration.Trim();
        if (trimmed.Contains(':', StringComparison.Ordinal))
            return TryParseClockDurationSeconds(trimmed);

        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) || minutes <= 0)
            return null;

        return (int)Math.Round(minutes * 60d, MidpointRounding.AwayFromZero);
    }

    private static string? FormatTrackDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            return null;

        var trimmed = duration.Trim();
        if (trimmed.Contains(':', StringComparison.Ordinal))
            return trimmed;

        return FormatRuntime(trimmed);
    }

    private static string? FormatAlbumDuration(IReadOnlyList<CollectionWorkSummary> works)
    {
        var seconds = works
            .Select(work => TryParseClockDurationSeconds(work.Duration))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        if (seconds.Count == 0)
            return null;

        var totalSeconds = seconds.Sum();
        var totalMinutes = (int)Math.Round(totalSeconds / 60d, MidpointRounding.AwayFromZero);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;

        return hours > 0
            ? minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h"
            : $"{totalMinutes}m";
    }

    private static int? TryParseClockDurationSeconds(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration) || !duration.Contains(':', StringComparison.Ordinal))
            return null;

        var parts = duration.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 3)
            return null;

        var total = 0;
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return null;

            total = (total * 60) + value;
        }

        return total;
    }

    private static bool IsTruthy(string? value)
        => value?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "explicit";

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

    private static string? BuildSubtitle(
        LibraryItemDetail detail,
        DetailEntityType entityType,
        IReadOnlyDictionary<string, string> values,
        MultiFormatState state)
    {
        if (state == (MultiFormatState)(-1))
            return "Book + Audiobook • Separate Progress";

        return entityType switch
        {
            DetailEntityType.Book => detail.Author,
            DetailEntityType.Audiobook => FirstNonBlank(detail.Narrator, detail.Author),
            DetailEntityType.Movie => FirstNonBlank(detail.Director, GetValue(values, "studio"), detail.Year, "Movie"),
            DetailEntityType.MusicTrack => string.Join(" â€¢ ", new[] { detail.Artist, GetValue(values, "album") }.Where(s => !string.IsNullOrWhiteSpace(s))),
            DetailEntityType.ComicIssue => FirstNonBlank(detail.Writer, detail.Illustrator, detail.Author),
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
            var episodeWorks = DeduplicateTvEpisodeSummaries(works);
            return episodeWorks.GroupBy(w => string.IsNullOrWhiteSpace(w.Season) ? "Season 1" : $"Season {w.Season}")
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

    private static IReadOnlyList<CollectionWorkSummary> DeduplicateTvEpisodeSummaries(IReadOnlyList<CollectionWorkSummary> works)
    {
        return works
            .GroupBy(work => new
            {
                Season = NormalizeEpisodeKey(work.Season),
                Episode = NormalizeEpisodeKey(work.Episode),
                Title = NormalizeTextKey(work.Title),
            })
            .Select(group => group
                .OrderByDescending(work => !string.IsNullOrWhiteSpace(work.BackgroundUrl))
                .ThenByDescending(work => !string.IsNullOrWhiteSpace(work.ArtworkUrl))
                .ThenBy(work => work.Ordinal ?? int.MaxValue)
                .First())
            .OrderBy(work => TryParseInt(work.Season) ?? int.MaxValue)
            .ThenBy(work => TryParseInt(work.Episode) ?? work.Ordinal ?? int.MaxValue)
            .ThenBy(work => work.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeEpisodeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().TrimStart('0');
        return normalized.Length == 0 ? "0" : normalized;
    }

    private static string NormalizeTextKey(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static MediaGroupingItemViewModel ToMediaItem(CollectionWorkSummary work)
        => new()
        {
            Id = work.Id,
            EntityType = InferMediaItemEntityType(work),
            Title = work.Title,
            Subtitle = InferMediaItemEntityType(work) == DetailEntityType.MusicTrack
                ? FirstNonBlank(work.Artist, work.Year, FormatTrackDuration(work.Duration))
                : FirstNonBlank(FormatSeasonEpisode(work.Season, work.Episode), work.Year, FormatTrackDuration(work.Duration)),
            Description = work.Description,
            ArtworkUrl = FirstNonBlank(work.BackgroundUrl, work.ArtworkUrl),
            TrackNumber = work.TrackNumber,
            Duration = FormatTrackDuration(work.Duration),
            Artist = work.Artist,
            IsExplicit = work.IsExplicit,
            Quality = work.Quality,
            ProgressPercent = work.ProgressPercent,
            Metadata = BuildEpisodeMetadata(FormatTrackDuration(work.Duration), work.Year),
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
        DetailEntityType.MusicTrack => $"/details/musictrack/{work.Id}?context=listen",
        _ => $"/book/{work.Id}",
    };

    private static IReadOnlyList<MetadataPill> BuildCollectionMetadata(
        DetailEntityType entityType,
        IReadOnlyList<CollectionWorkSummary> works,
        IReadOnlyDictionary<string, string> values)
    {
        if (entityType == DetailEntityType.MusicAlbum)
        {
            var pills = new List<MetadataPill>();
            AddPlain(pills, FormatEntityType(entityType), "type");
            AddPlain(pills, FirstNonBlank(GetValue(values, "year"), GetValue(values, "release_year"), works.Select(w => w.Year).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y))), "year");
            AddPlain(pills, FormatCountLabel(GetValue(values, "track_count") ?? works.Count.ToString(CultureInfo.InvariantCulture), "track"), "track_count");
            AddPlain(pills, FormatAlbumDuration(works), "duration");
            AddPlain(pills, GetValue(values, "genre"), "genre");
            AddPlain(pills, FirstNonBlank(GetValue(values, "quality"), GetValue(values, "audio_quality"), works.Select(w => w.Quality).FirstOrDefault(q => !string.IsNullOrWhiteSpace(q))), "quality");
            return pills
                .Where(value => !string.IsNullOrWhiteSpace(value.Label))
                .DistinctBy(value => $"{value.Kind}:{value.Label}", StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return [new MetadataPill { Label = FormatEntityType(entityType), Kind = "type" }, new MetadataPill { Label = $"{works.Count} item{(works.Count == 1 ? "" : "s")}", Kind = "count" }];
    }

    private static IReadOnlyList<DetailAction> BuildCollectionActions(Guid id, DetailEntityType entityType, DetailPresentationContext context, ProgressViewModel? heroProgress)
        => entityType switch
        {
            DetailEntityType.TvShow => [new DetailAction { Key = "watch", Label = heroProgress is null ? "Watch" : "Continue Watching", Icon = "play_arrow", IsPrimary = true }],
            DetailEntityType.MusicAlbum => [new DetailAction { Key = "play-album", Label = "Play", Icon = "play_arrow", IsPrimary = true }],
            _ => [new DetailAction { Key = "open", Label = "Open", Icon = "open_in_new", IsPrimary = true }],
        };

    private async Task<IReadOnlyList<CreditGroupViewModel>> BuildCollectionCreditsAsync(
        Guid collectionId,
        Guid? rootWorkId,
        IReadOnlyList<CollectionWorkSummary> works,
        DetailEntityType entityType,
        IReadOnlyDictionary<string, string> canonicalValues,
        CancellationToken ct)
    {
        if (entityType != DetailEntityType.TvShow)
            return await BuildCollectionTextCreditsAsync(collectionId, entityType, canonicalValues, ct);

        rootWorkId ??= works
            .Select(work => Guid.TryParse(work.Id, out var parsed) ? parsed : (Guid?)null)
            .FirstOrDefault(id => id.HasValue);

        if (!rootWorkId.HasValue)
            return [];

        var cast = await CastCreditQueries.BuildForWorkAsync(rootWorkId.Value, _canonicalArrays, _persons, _db, ct);
        if (cast.Count == 0)
            return [];

        var credits = cast.Select((credit, index) => new EntityCreditViewModel
        {
            EntityId = BuildPersonCreditEntityId(credit.PersonId, credit.WikidataQid, credit.Name),
            EntityType = RelatedEntityType.Person,
            DisplayName = credit.Name,
            ImageUrl = credit.HeadshotUrl,
            FallbackInitials = Initials(credit.Name),
            PrimaryRole = "Actor",
            CharacterName = credit.Characters.FirstOrDefault()?.CharacterName,
            CharacterEntityId = credit.Characters.FirstOrDefault()?.FictionalEntityId.ToString("D"),
            CharacterImageUrl = credit.Characters.FirstOrDefault()?.PortraitUrl,
            SortOrder = index,
            IsPrimary = index < 8,
            IsCanonical = !string.IsNullOrWhiteSpace(credit.WikidataQid),
        }).ToList();

        return ApplyContributorGroupPresentation(entityType, SplitCastGroups(credits));
    }

    private async Task<IReadOnlyList<CreditGroupViewModel>> BuildCollectionTextCreditsAsync(
        Guid collectionId,
        DetailEntityType entityType,
        IReadOnlyDictionary<string, string> canonicalValues,
        CancellationToken ct)
    {
        var groups = new List<CreditGroupViewModel>();

        async Task AddTextCreditAsync(string title, CreditGroupType type, string role, string canonicalArrayKey)
        {
            var entries = await LoadCollectionContributorEntriesAsync(
                collectionId,
                canonicalArrayKey,
                GetValue(canonicalValues, canonicalArrayKey),
                canonicalValues,
                ct);
            if (entries.Count == 0)
                return;

            var credits = new List<EntityCreditViewModel>();
            foreach (var entry in entries.Take(24))
            {
                var qid = NormalizeQid(entry.Qid);
                var person = string.IsNullOrWhiteSpace(qid) ? null : await _persons.FindByQidAsync(qid, ct);
                person ??= await _persons.FindByNameAsync(entry.Name, ct);
                var imageUrl = person is null
                    ? FirstNonBlank(
                        GetValue(canonicalValues, $"{canonicalArrayKey}_headshot_url"),
                        GetValue(canonicalValues, $"{canonicalArrayKey}_image_url"),
                        GetValue(canonicalValues, $"{canonicalArrayKey}_photo_url"),
                        entries.Count == 1 ? GetValue(canonicalValues, "headshot_url") : null)
                    : ApiImageUrls.BuildPersonHeadshotUrl(person.Id, person.LocalHeadshotPath, person.HeadshotUrl);

                credits.Add(new EntityCreditViewModel
                {
                    EntityId = BuildPersonCreditEntityId(person?.Id, qid ?? person?.WikidataQid, entry.Name),
                    EntityType = RelatedEntityType.Person,
                    DisplayName = person?.Name ?? entry.Name,
                    ImageUrl = imageUrl,
                    FallbackInitials = Initials(person?.Name ?? entry.Name),
                    PrimaryRole = role,
                    SortOrder = entry.SortOrder,
                    IsPrimary = entry.SortOrder == 0,
                    IsCanonical = !string.IsNullOrWhiteSpace(qid ?? person?.WikidataQid),
                });
            }

            groups.Add(new CreditGroupViewModel
            {
                Title = title,
                GroupType = type,
                Credits = credits,
            });
        }

        switch (entityType)
        {
            case DetailEntityType.MusicAlbum:
                await AddTextCreditAsync("Artists", CreditGroupType.PrimaryArtists, "Artist", "artist");
                await AddTextCreditAsync("Performers", CreditGroupType.FeaturedArtists, "Performer", "performer");
                await AddTextCreditAsync("Composers", CreditGroupType.MusicCredits, "Composer", "composer");
                break;
            case DetailEntityType.BookSeries:
                await AddTextCreditAsync("Authors", CreditGroupType.Authors, "Author", "author");
                break;
            case DetailEntityType.MovieSeries:
                await AddTextCreditAsync("Directors", CreditGroupType.Directors, "Director", "director");
                await AddTextCreditAsync("Writers", CreditGroupType.Writers, "Writer", "screenwriter");
                break;
            case DetailEntityType.ComicSeries:
                await AddTextCreditAsync("Writers", CreditGroupType.Writers, "Writer", "screenwriter");
                await AddTextCreditAsync("Artists", CreditGroupType.Illustrators, "Artist", "illustrator");
                break;
        }

        return ApplyContributorGroupPresentation(entityType, groups);
    }

    private async Task<IReadOnlyList<ContributorEntry>> LoadCollectionContributorEntriesAsync(
        Guid collectionId,
        string canonicalArrayKey,
        string? fallbackValue,
        IReadOnlyDictionary<string, string> canonicalValues,
        CancellationToken ct)
    {
        var arrayEntries = await _canonicalArrays.GetValuesAsync(collectionId, canonicalArrayKey, ct);
        var entries = DeduplicateContributorEntries(arrayEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .OrderBy(entry => entry.Ordinal)
            .Select(entry => new ContributorEntry(
                entry.Value.Trim(),
                NormalizeQid(entry.ValueQid),
                entry.Ordinal))
            .ToList());
        if (entries.Count > 0)
            return entries;

        entries = await LoadContributorEntriesFromClaimsAsync(collectionId, canonicalArrayKey, ct);
        if (entries.Count > 0)
            return entries;

        if (string.IsNullOrWhiteSpace(fallbackValue))
            return [];

        return DeduplicateContributorEntries(SplitNames(fallbackValue)
            .Select((name, index) => new ContributorEntry(
                name,
                ResolveCompanionQidFromCanonical(canonicalValues, canonicalArrayKey, name, index),
                index))
            .ToList());
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
            SELECT cpl.rowid AS LinkOrder,
                   p.id AS PersonId,
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
            ORDER BY cpl.rowid, fe.label, cp.is_default DESC;
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
            .Select(group =>
            {
                var sourceOrder = group.Min(row => row.LinkOrder);
                var preferredCharacter = group
                    .OrderBy(row => row.LinkOrder)
                    .ThenByDescending(row => row.PortraitIsDefault)
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
                    SortOrder = (int)Math.Min(sourceOrder, int.MaxValue),
                    IsCanonical = !string.IsNullOrWhiteSpace(group.Key.PersonQid),
                };
            })
            .OrderBy(credit => credit.SortOrder)
            .ThenBy(credit => credit.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select((credit, index) => new EntityCreditViewModel
            {
                EntityId = credit.EntityId,
                EntityType = credit.EntityType,
                DisplayName = credit.DisplayName,
                ImageUrl = credit.ImageUrl,
                FallbackInitials = credit.FallbackInitials,
                PrimaryRole = credit.PrimaryRole,
                SecondaryRole = credit.SecondaryRole,
                CharacterName = credit.CharacterName,
                CharacterEntityId = credit.CharacterEntityId,
                CharacterImageUrl = credit.CharacterImageUrl,
                SortOrder = index,
                IsPrimary = index < 8,
                IsCanonical = credit.IsCanonical,
                SourceName = credit.SourceName,
                SourceId = credit.SourceId,
            })
            .Take(24)
            .ToList();

        return credits.Count == 0
            ? []
            : ApplyContributorGroupPresentation(DetailEntityType.Universe, SplitCastGroups(credits));
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
        var cast = CreditsFor(groups, CreditGroupType.Cast);
        var directors = CreditsFor(groups, CreditGroupType.Directors);
        var authors = CreditsFor(groups, CreditGroupType.Authors);
        var narrators = CreditsFor(groups, CreditGroupType.Narrators);
        var writers = CreditsFor(groups, CreditGroupType.Writers);
        var illustrators = CreditsFor(groups, CreditGroupType.Illustrators);
        var artists = CreditsFor(groups, CreditGroupType.PrimaryArtists);
        var featuredArtists = CreditsFor(groups, CreditGroupType.FeaturedArtists);
        var musicCredits = CreditsFor(groups, CreditGroupType.MusicCredits);

        var preview = entityType switch
        {
            DetailEntityType.Movie => directors.Take(1).Concat(cast.Take(5)).ToList(),
            DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode => cast.Take(5).ToList(),
            DetailEntityType.Book => authors.Take(2).ToList(),
            DetailEntityType.Audiobook => authors.Take(2).Concat(narrators.Take(2)).ToList(),
            DetailEntityType.Work => authors.Take(2).Concat(narrators.Take(2)).ToList(),
            DetailEntityType.ComicIssue or DetailEntityType.ComicSeries => writers.Take(2).Concat(illustrators.Take(2)).ToList(),
            DetailEntityType.MusicAlbum or DetailEntityType.MusicTrack => artists.Take(2).Concat(featuredArtists.Take(2)).Concat(musicCredits.Take(1)).ToList(),
            DetailEntityType.MusicArtist => artists.Take(2).Concat(featuredArtists.Take(2)).Concat(musicCredits.Take(2)).ToList(),
            DetailEntityType.Universe or DetailEntityType.MovieSeries or DetailEntityType.BookSeries => cast.Take(6).ToList(),
            _ => [],
        };

        preview = DeduplicatePreviewCredits(preview).ToList();
        return preview.Count > 0
            ? preview
            : DeduplicatePreviewCredits(groups.SelectMany(g => g.Credits).OrderBy(c => c.SortOrder)).Take(6).ToList();
    }

    private static IReadOnlyList<EntityCreditViewModel> CreditsFor(
        IReadOnlyList<CreditGroupViewModel> groups,
        CreditGroupType groupType)
        => groups
            .Where(group => group.GroupType == groupType)
            .SelectMany(group => group.Credits)
            .OrderBy(credit => credit.SortOrder)
            .ToList();

    private static IEnumerable<EntityCreditViewModel> DeduplicatePreviewCredits(IEnumerable<EntityCreditViewModel> credits)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var credit in credits)
        {
            var key = !string.IsNullOrWhiteSpace(credit.EntityId)
                ? credit.EntityId
                : $"{credit.EntityType}:{credit.DisplayName}";
            if (seen.Add(key))
                yield return credit;
        }
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

    private static string BuildCollectionSubtitle(
        DetailEntityType entityType,
        IReadOnlyList<CollectionWorkSummary> works,
        IReadOnlyDictionary<string, string> values)
    {
        if (entityType == DetailEntityType.MusicAlbum)
            return FirstNonBlank(GetValue(values, "album_artist"), GetValue(values, "artist"), works.Select(w => w.Artist).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)), "Album")!;

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
                    Id = CreditDisplayId(c),
                    EntityType = MapCreditToEntityType(c),
                    Title = c.Title,
                    Subtitle = string.Join(" • ", new[] { c.Role, c.Year }.Where(v => !string.IsNullOrWhiteSpace(v))),
                    ArtworkUrl = c.CoverUrl,
                    Actions = [new DetailAction { Key = "open", Label = "Open", Icon = "open_in_new", Route = BuildCreditRoute(c) }],
                    IsOwned = true,
                }).ToList(),
            }).ToList();

    private static IReadOnlyList<MetadataPill> BuildPersonMetadata(IReadOnlyList<string> roles, int creditCount)
        => roles.Take(4).Select(role => new MetadataPill { Label = role }).Append(new MetadataPill { Label = $"{creditCount} library credits" }).ToList();

    private static IReadOnlyList<string> BuildPersonDisplayRoles(
        IReadOnlyList<MediaEngine.Api.Models.PersonLibraryCreditDto> credits,
        IReadOnlyList<string> fallbackRoles)
    {
        var mediaRoles = credits
            .Select(credit => NormalizePersonRole(credit.Role))
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .GroupBy(role => role!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => PersonRoleRank(group.Key))
            .ThenByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key)
            .ToList();

        if (mediaRoles.Count > 0)
            return mediaRoles;

        return fallbackRoles
            .Select(NormalizePersonRole)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();
    }

    private static string? NormalizePersonRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;

        var normalized = role.Trim().Replace('_', ' ').Replace('-', ' ');
        return normalized.ToLowerInvariant() switch
        {
        "screenwriter" => "Writer",
            "writer" => "Writer",
            "voice actor" => "Voice Actor",
            "voiceactor" => "Voice Actor",
            "primary artist" => "Artist",
            "featured artist" => "Performer",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant()),
        };
    }

    private static int PersonRoleRank(string role) => role.ToLowerInvariant() switch
    {
        "author" => 0,
        "actor" => 1,
        "director" => 2,
        "writer" => 3,
        "producer" => 4,
        "artist" => 5,
        "illustrator" => 6,
        "narrator" => 7,
        "voice actor" => 8,
        "performer" => 9,
        "composer" => 10,
        _ => 50,
    };

    private async Task<string?> LoadPersonWikipediaUrlAsync(Guid personId, CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            """
            SELECT value
            FROM canonical_values
            WHERE entity_id = @personId
              AND key = 'wikipedia_url'
              AND value IS NOT NULL
              AND TRIM(value) <> ''
            ORDER BY last_scored_at DESC
            LIMIT 1;
            """,
            new { personId = personId.ToString("D") },
            cancellationToken: ct));
    }

    private async Task<string?> LoadPersonShortDescriptionAsync(Guid personId, string? qid, CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var description = await conn.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            """
            SELECT value
            FROM canonical_values
            WHERE entity_id = @personId
              AND key = 'short_description'
              AND value IS NOT NULL
              AND TRIM(value) <> ''
            ORDER BY last_scored_at DESC
            LIMIT 1;
            """,
            new { personId = personId.ToString("D") },
            cancellationToken: ct));

        if (!string.IsNullOrWhiteSpace(description))
            return description;

        if (string.IsNullOrWhiteSpace(qid))
            return null;

        var labelDescription = await conn.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            """
            SELECT description
            FROM qid_labels
            WHERE qid = @qid
              AND description IS NOT NULL
              AND TRIM(description) <> ''
            LIMIT 1;
            """,
            new { qid },
            cancellationToken: ct));

        return LooksLikeWikidataShortDescription(labelDescription)
            ? labelDescription
            : null;
    }

    private static bool LooksLikeWikidataShortDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return trimmed.Length <= 220
            && !trimmed.Contains('\n')
            && !trimmed.Contains(". ", StringComparison.Ordinal);
    }

    private static DescriptionAttributionViewModel? BuildWikipediaDescriptionAttribution(string? description, string? wikipediaUrl)
    {
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(wikipediaUrl))
            return null;

        return new DescriptionAttributionViewModel
        {
            SourceName = "Wikipedia",
            SourceUrl = wikipediaUrl,
            LicenseName = "CC BY-SA 4.0",
            LicenseUrl = "https://creativecommons.org/licenses/by-sa/4.0/",
            Notice = "Text from Wikipedia is available under the Creative Commons Attribution-ShareAlike 4.0 License; additional terms may apply.",
        };
    }

    private static PersonDetailFacts BuildPersonDetails(
        MediaEngine.Domain.Entities.Person person,
        IReadOnlyList<string> displayRoles,
        string? wikipediaUrl,
        IReadOnlyList<MediaEngine.Domain.Entities.Person> aliases,
        IReadOnlyList<MediaEngine.Api.Models.PersonGroupMemberDto> groupMembers,
        IReadOnlyList<MediaEngine.Api.Models.PersonGroupMemberDto> memberOfGroups)
        => new()
        {
            WikidataQid = person.WikidataQid,
            WikidataUrl = null,
            Biography = person.Biography,
            Occupation = person.Occupation,
            Roles = displayRoles,
            DateOfBirth = person.DateOfBirth,
            DateOfDeath = person.DateOfDeath,
            PlaceOfBirth = person.PlaceOfBirth,
            PlaceOfDeath = person.PlaceOfDeath,
            Nationality = person.Nationality,
            IsPseudonym = person.IsPseudonym,
            IsGroup = person.IsGroup,
            CreatedAt = person.CreatedAt,
            EnrichedAt = person.EnrichedAt,
            ExternalLinks = BuildPersonExternalLinks(person, wikipediaUrl),
            Aliases = aliases.Select(alias => new PersonRelatedLink
            {
                Id = alias.Id.ToString("D"),
                Name = alias.Name,
                Subtitle = alias.IsPseudonym ? "Pen name" : "Related identity",
                Route = $"/details/person/{alias.Id:D}",
            }).ToList(),
            GroupMembers = groupMembers.Select(member => new PersonRelatedLink
            {
                Id = member.Id.ToString("D"),
                Name = member.Name,
                Subtitle = member.DateRange,
                Route = $"/details/person/{member.Id:D}",
            }).ToList(),
            MemberOfGroups = memberOfGroups.Select(group => new PersonRelatedLink
            {
                Id = group.Id.ToString("D"),
                Name = group.Name,
                Subtitle = group.DateRange,
                Route = $"/details/person/{group.Id:D}",
            }).ToList(),
        };

    private static IReadOnlyList<PersonExternalLink> BuildPersonExternalLinks(MediaEngine.Domain.Entities.Person person, string? wikipediaUrl)
    {
        var links = new List<PersonExternalLink>();
        AddPersonExternalLink(links, "website", "Website", person.Website, "WEB");
        AddPersonExternalLink(links, "wikipedia", "Wikipedia", wikipediaUrl, "W");
        AddPersonExternalLink(links, "instagram", "Instagram", BuildSocialUrl("instagram", person.Instagram), "IG");
        AddPersonExternalLink(links, "twitter", "X", BuildSocialUrl("twitter", person.Twitter), "X");
        AddPersonExternalLink(links, "tiktok", "TikTok", BuildSocialUrl("tiktok", person.TikTok), "TT");
        AddPersonExternalLink(links, "mastodon", "Mastodon", BuildSocialUrl("mastodon", person.Mastodon), "M");
        return links;
    }

    private static void AddPersonExternalLink(List<PersonExternalLink> links, string key, string label, string? url, string iconLabel)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        links.Add(new PersonExternalLink
        {
            Key = key,
            Label = label,
            Url = url,
            IconLabel = iconLabel,
        });
    }

    private static string? BuildSocialUrl(string platform, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        var value = rawValue.Trim();
        var isUrl = value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (isUrl)
            return value;

        var handle = value.TrimStart('@');
        return platform switch
        {
            "instagram" => $"https://instagram.com/{handle}",
            "twitter" => $"https://x.com/{handle}",
            "tiktok" => $"https://tiktok.com/@{handle}",
            "mastodon" when value.Contains('@') && value.Contains('.') => BuildMastodonUrl(value),
            _ => value,
        };
    }

    private static string BuildMastodonUrl(string value)
    {
        var parts = value.Split('@', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? $"https://{parts[1]}/@{parts[0]}" : value;
    }

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

    private static DetailEntityType MapCreditToEntityType(MediaEngine.Api.Models.PersonLibraryCreditDto credit)
        => credit.CollectionId.HasValue && credit.MediaType?.Contains("tv", StringComparison.OrdinalIgnoreCase) == true
            ? DetailEntityType.TvShow
            : MapMediaTypeToEntityType(credit.MediaType);

    private static string CreditDisplayId(MediaEngine.Api.Models.PersonLibraryCreditDto credit)
        => MapCreditToEntityType(credit) == DetailEntityType.TvShow && credit.CollectionId.HasValue
            ? credit.CollectionId.Value.ToString("D")
            : credit.WorkId.ToString("D");

    private static string? BuildCreditRoute(MediaEngine.Api.Models.PersonLibraryCreditDto credit)
        => MapMediaTypeToEntityType(credit.MediaType) switch
        {
            DetailEntityType.Movie => $"/watch/movie/{credit.WorkId}",
            DetailEntityType.TvEpisode when credit.CollectionId.HasValue => $"/watch/tv/show/{credit.CollectionId.Value}",
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
        "media" => "Media in Library",
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

    private static string BuildPersonCreditEntityId(Guid? personId, string? qid, string name)
        => personId?.ToString("D")
            ?? NormalizeQid(qid)
            ?? name;

    private static string ResolveCollectionTitle(
        DetailEntityType entityType,
        string? displayName,
        IReadOnlyDictionary<string, string> rootValues,
        IReadOnlyDictionary<string, string> values)
    {
        if (entityType == DetailEntityType.TvShow)
        {
            return FirstNonBlank(
                GetValue(rootValues, MetadataFieldConstants.Title),
                GetValue(rootValues, MetadataFieldConstants.ShowName),
                GetValue(values, MetadataFieldConstants.Title),
                GetValue(values, MetadataFieldConstants.ShowName),
                StripUniverseSuffix(displayName),
                displayName,
                "TV Show");
        }

        return FirstNonBlank(displayName, GetValue(values, MetadataFieldConstants.Title), "Collection");
    }

    private static string? StripUniverseSuffix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        const string suffix = " universe";
        var trimmed = value.Trim();
        return trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^suffix.Length].Trim()
            : trimmed;
    }

    private static bool LooksLikeAggregateContributorName(string value)
        => value.Contains(" & ", StringComparison.Ordinal)
            || value.Contains(" and ", StringComparison.OrdinalIgnoreCase)
            || value.Contains(" + ", StringComparison.Ordinal);

    private static IReadOnlyList<ContributorEntry> DeduplicateContributorEntries(IReadOnlyList<ContributorEntry> entries)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ContributorEntry>();
        foreach (var entry in entries.OrderBy(e => e.SortOrder))
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            var key = NormalizeQid(entry.Qid) ?? entry.Name.Trim();
            if (seen.Add(key))
                result.Add(entry with { Name = entry.Name.Trim(), Qid = NormalizeQid(entry.Qid), SortOrder = result.Count });
        }

        return result;
    }

    private static string? ResolveCompanionQidFromCanonical(
        IReadOnlyDictionary<string, string> canonicalValues,
        string canonicalArrayKey,
        string name,
        int index)
    {
        var raw = GetValue(canonicalValues, canonicalArrayKey + MetadataFieldConstants.CompanionQidSuffix);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var parsed = SplitCanonicalSegments(raw)
            .Select(ParseQidLabel)
            .Where(value => !string.IsNullOrWhiteSpace(value.Qid))
            .ToList();

        var byName = parsed.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value.Label)
            && value.Label.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(byName.Qid))
            return byName.Qid;

        return index >= 0 && index < parsed.Count ? parsed[index].Qid : null;
    }

    private static IReadOnlyList<string> SplitCanonicalSegments(string value)
    {
        var separator = value.Contains("|||", StringComparison.Ordinal) ? "|||" : ";";
        return value.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static (string? Qid, string? Label) ParseQidLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (null, null);

        var trimmed = value.Trim();
        var delimiter = trimmed.IndexOf("::", StringComparison.Ordinal);
        if (delimiter > 0)
            return (NormalizeQid(trimmed[..delimiter]), FirstNonBlank(trimmed[(delimiter + 2)..], null));

        return (NormalizeQid(trimmed), null);
    }

    private static string? NormalizeQid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        var delimiter = trimmed.IndexOf("::", StringComparison.Ordinal);
        if (delimiter > 0)
            trimmed = trimmed[..delimiter].Trim();

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

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

    private static double? DoubleValue(object? value)
    {
        if (value is null or DBNull)
            return null;

        return value switch
        {
            double d => d,
            float f => f,
            decimal m => (double)m,
            int i => i,
            long l => l,
            _ => double.TryParse(Convert.ToString(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
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
    private sealed record OwnedFormatRow(Guid EditionId, string? FormatLabel, Guid AssetId, string FilePathRoot, string? AssetCoverUrl, string? EditionCoverUrl, string? Runtime, string? PageCount, string? Narrator, double? ProgressPct);
    private sealed record CollectionDetailRow(Guid Id, string? DisplayName, string? WikidataQid, string? Description, string? Tagline, string? CoverUrl, string? BackgroundUrl, string? BannerUrl, string? LogoUrl, string? HeroBrandLabel, string? HeroBrandImageUrl);
    private sealed record SeriesRow(string WorkId, string Title, string? MediaType, string? PositionLabel, string? ArtworkUrl);
    private sealed record CollectionWorkSummary(string Id, string MediaType, int? Ordinal, string Title, string? Description, string? Season, string? Episode, string? TrackNumber, string? Duration, string? Year, string? Artist, bool IsExplicit, string? Quality, double? ProgressPercent, string? ArtworkUrl, string? BackgroundUrl);
    private sealed record ContributorEntry(string Name, string? Qid, int SortOrder);

    private sealed class ContributorTargetRow
    {
        public Guid WorkId { get; init; }
        public Guid? RootWorkId { get; init; }
        public Guid? AssetId { get; init; }
    }

    private sealed class ContributorClaimRow
    {
        public long RowNumber { get; init; }
        public string ClaimKey { get; init; } = string.Empty;
        public string ClaimValue { get; init; } = string.Empty;
    }

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
        public long LinkOrder { get; init; }
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
