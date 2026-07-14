using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using MediaEngine.Api.Endpoints;
using MediaEngine.Api.Services.Display;
using MediaEngine.Api.Services.Playback;
using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Contracts.Details;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Storage;
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
    private readonly IPersonCreditReadService _personCredits;
    private readonly DetailRecommendationService _recommendations;
    private readonly PlaybackCapabilitiesService? _playback;
    private readonly ILogger<DetailComposerService>? _logger;

    public DetailComposerService(
        IDatabaseConnection db,
        ILibraryItemRepository libraryItems,
        IPersonRepository persons,
        IEntityAssetRepository entityAssets,
        ICanonicalValueArrayRepository canonicalArrays,
        ISeriesManifestRepository seriesManifests,
        IPersonCreditReadService personCredits,
        DetailRecommendationService recommendations,
        PlaybackCapabilitiesService? playback = null,
        ILogger<DetailComposerService>? logger = null)
    {
        _db = db;
        _libraryItems = libraryItems;
        _persons = persons;
        _entityAssets = entityAssets;
        _canonicalArrays = canonicalArrays;
        _seriesManifests = seriesManifests;
        _personCredits = personCredits;
        _recommendations = recommendations;
        _playback = playback;
        _logger = logger;
    }

    public async Task<DetailPageViewModel?> BuildAsync(
        DetailEntityType entityType,
        Guid id,
        DetailPresentationContext context,
        CancellationToken ct = default,
        string? selectedContainerId = null,
        Guid? profileId = null)
    {
        var isAdminView = context is DetailPresentationContext.Admin;
        var favoriteWorkIds = await LoadFavoriteWorkIdsAsync(profileId, ct);

        return entityType switch
        {
            DetailEntityType.Person or DetailEntityType.MusicArtist => await BuildPersonAsync(id, entityType, context, isAdminView, ct),
            DetailEntityType.Collection or DetailEntityType.TvShow or DetailEntityType.MovieSeries or DetailEntityType.BookSeries
                or DetailEntityType.ComicSeries or DetailEntityType.MusicAlbum => await BuildCollectionAsync(id, entityType, context, isAdminView, favoriteWorkIds, ct),
            DetailEntityType.Character => await BuildCharacterAsync(id, context, isAdminView, ct),
            DetailEntityType.Universe => await BuildUniverseAsync(id, context, isAdminView, ct),
            _ => await BuildWorkAsync(id, entityType, context, isAdminView, selectedContainerId, favoriteWorkIds, profileId, ct),
        };
    }

    public static bool TryParseEntityType(string value, out DetailEntityType entityType)
    {
        entityType = default;
        if (value.Contains("podcast", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

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
        {
            return ArtworkPresentationMode.PairedEditionGradient;
        }

        if (!string.IsNullOrWhiteSpace(backdropUrl) || !string.IsNullOrWhiteSpace(bannerUrl))
        {
            return ArtworkPresentationMode.CinematicBackdrop;
        }

        if (entityType is DetailEntityType.Person or DetailEntityType.MusicArtist && !string.IsNullOrWhiteSpace(portraitUrl))
        {
            return ArtworkPresentationMode.PortraitEcho;
        }

        if (!string.IsNullOrWhiteSpace(coverUrl) || !string.IsNullOrWhiteSpace(posterUrl))
        {
            return ArtworkPresentationMode.ColorGradientFromArtwork;
        }

        if (relatedArtworkCount > 1)
        {
            return ArtworkPresentationMode.CollageGradient;
        }

        return ArtworkPresentationMode.GeneratedIdentity;
    }

    private async Task<DetailPageViewModel?> BuildWorkAsync(
        Guid workId,
        DetailEntityType requestedType,
        DetailPresentationContext context,
        bool isAdminView,
        string? selectedContainerId,
        IReadOnlySet<Guid> favoriteWorkIds,
        Guid? profileId,
        CancellationToken ct)
    {
        var detail = await _libraryItems.GetDetailAsync(workId, ct);
        if (detail is null)
        {
            return null;
        }

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
        var sequencePlacement = await BuildSequencePlacementAsync(workId, detail, entityType, selectedContainerId, ct);
        var mediaGroups = await BuildWorkMediaGroupsAsync(workId, entityType, profileId, ct);
        var heroProgress = BuildHeroProgress(entityType, detail.Runtime, ownedFormats)
            ?? BuildAudiobookHeroProgress(entityType, detail.Runtime, mediaGroups);
        var descriptionSelection = ResolveLongDescription(detail, values, entityType);
        var longDescription = descriptionSelection.Text;
        var heroSummary = BuildHeroSummary(values);
        var displayOverrides = await LoadWorkDisplayOverridesAsync(workId, ct);
        var displayTitle = ResolveDisplayTitleOverride(displayOverrides, entityType);
        var relationships = BuildRelationshipStrip(detail, sequencePlacement);

        return new DetailPageViewModel
        {
            Id = workId.ToString("D"),
            EntityType = entityType,
            PresentationContext = context,
            EditorTarget = new DetailEditorTarget
            {
                EntityId = workId.ToString("D"),
                EntityKind = "Work",
                ContainerMode = IsCanonicalContainerEntity(entityType) ? "Canonical" : "Singular",
                InitialTab = "details",
            },
            Title = ResolveWorkDisplayTitle(displayTitle, detail, values, entityType),
            Subtitle = BuildSubtitle(detail, entityType, values, multiFormatState),
            Tagline = heroSummary,
            Description = longDescription,
            DescriptionAttribution = BuildDescriptionAttribution(descriptionSelection, detail, values),
            SourceLinks = BuildExternalSourceLinks(detail.WikidataQid, GetValue(values, "wikipedia_url"), sequencePlacement, values),
            Facts = BuildWorkFacts(detail, entityType, values, contributorGroups),
            Artwork = artwork,
            HeroBrand = BuildHeroBrand(
                entityType,
                FirstNonBlank(GetValue(values, "network"), GetValue(values, "studio"), GetValue(values, "broadcaster")),
                FirstNonBlank(GetValue(values, "network_logo_url"), GetValue(values, "network_logo"), GetValue(values, "studio_logo_url"), GetValue(values, "broadcaster_logo_url"))),
            Progress = heroProgress,
            OwnedFormats = ownedFormats,
            MultiFormatState = multiFormatState,
            SyncCapability = BuildSyncCapability(workId, ownedFormats, multiFormatState),
            SequencePlacement = sequencePlacement,
            Metadata = BuildMetadataPills(detail, entityType, values, ownedFormats),
            PrimaryActions = BuildPrimaryActions(workId, entityType, context, ownedFormats, heroProgress),
            SecondaryActions = BuildSecondaryActions(workId, entityType, favoriteWorkIds.Contains(workId), ownedFormats),
            OverflowActions = BuildOverflowActions(workId, entityType, isAdminView),
            ContributorGroups = contributorGroups,
            PreviewContributors = BuildPreviewContributors(entityType, contributorGroups),
            CharacterGroups = characters,
            PreviewCharacters = characters.SelectMany(g => g.Characters).Take(12).ToList(),
            RelationshipStrip = relationships,
            Tabs = BuildTabs(
                entityType,
                context,
                isAdminView,
                sequencePlacement is not null,
                HasUniverseRelationship(relationships),
                HasChapterGroup(mediaGroups)),
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
        IReadOnlySet<Guid> favoriteWorkIds,
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
            new { collectionId = GuidSql.ToBlob(collectionId) },
            cancellationToken: ct));

        var hasCollectionRow = rawRow is not null;
        var row = hasCollectionRow
            ? new CollectionDetailRow(
                Guid.Parse(StringValue(rawRow!.Id) ?? collectionId.ToString("D")),
                StringValue(rawRow.DisplayName),
                StringValue(rawRow.WikidataQid),
                StringValue(rawRow.Description),
                StringValue(rawRow.Tagline),
                StringValue(rawRow.CoverUrl),
                StringValue(rawRow.BackgroundUrl),
                StringValue(rawRow.BannerUrl),
                StringValue(rawRow.LogoUrl),
                StringValue(rawRow.HeroBrandLabel),
                StringValue(rawRow.HeroBrandImageUrl))
            : entityType == DetailEntityType.TvShow
                ? await LoadTvShowRootDetailRowAsync(collectionId, ct)
                : null;

        if (row is null)
        {
            return null;
        }

        var collectionValues = hasCollectionRow
            ? await LoadCanonicalMapAsync(collectionId, ct)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rootWorkId = hasCollectionRow
            ? await LoadCollectionRootWorkIdAsync(
                collectionId,
                requireRootWithChildren: entityType is DetailEntityType.TvShow or DetailEntityType.MusicAlbum or DetailEntityType.MovieSeries or DetailEntityType.BookSeries or DetailEntityType.ComicSeries,
                ct)
            : collectionId;
        var works = await LoadCollectionWorksAsync(collectionId, rootWorkId, ct);
        if (!hasCollectionRow && entityType == DetailEntityType.TvShow && works.Count == 0)
        {
            return null;
        }

        if (entityType == DetailEntityType.Collection)
        {
            entityType = InferCollectionEntityType(works);
        }

        var relatedArt = works
            .SelectMany(w => new[] { w.BackgroundUrl, w.ArtworkUrl })
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        var rootValues = rootWorkId.HasValue
            ? await LoadCanonicalMapAsync(rootWorkId.Value, ct)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var values = MergeCanonicalMaps(collectionValues, rootValues);
        var longDescription = FirstText(
            GetValue(values, MetadataFieldConstants.Description),
            GetValue(values, "overview"),
            GetValue(values, "plot_summary"),
            row.Description);
        var heroSummary = BuildHeroSummary(values);
        // Episode artwork must never stand in for show artwork. An unenriched TV show
        // deliberately falls back to its own cover (or the generated placeholder).
        var allowChildArtworkFallback = entityType != DetailEntityType.TvShow;
        var fallbackBackdrop = allowChildArtworkFallback
            ? works.Select(w => w.BackgroundUrl).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url))
            : null;
        var fallbackCover = allowChildArtworkFallback
            ? works.Select(w => w.ArtworkUrl).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url))
            : null;
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
        var manifest = await _seriesManifests.GetViewByCollectionIdAsync(collectionId, ct);
        var displayWorks = MergeCollectionManifestPlaceholders(entityType, works, manifest);
        var expectedTotal = AuthoritativeManifestTotal(manifest);
        var artwork = BuildArtwork(
            entityType,
            collectionBackdrop,
            collectionBanner,
            collectionCover,
            collectionCover,
            null,
            values,
            allowChildArtworkFallback ? relatedArt : [],
            0,
            null,
            collectionLogo);
        var relationships = BuildCollectionRelationships(row, entityType);
        var collectionTitle = ResolveCollectionTitle(entityType, row.DisplayName, rootValues, values);
        var sequencePlacement = BuildCollectionSequencePlacement(
            collectionId,
            entityType,
            collectionTitle,
            row.WikidataQid,
            longDescription,
            displayWorks,
            expectedTotal);

        return new DetailPageViewModel
        {
            Id = collectionId.ToString("D"),
            EntityType = entityType,
            PresentationContext = context,
            EditorTarget = BuildCollectionEditorTarget(collectionId, entityType, rootWorkId),
            Title = collectionTitle,
            Subtitle = BuildCollectionSubtitle(entityType, displayWorks, values),
            Tagline = heroSummary,
            Description = longDescription,
            DescriptionAttribution = BuildWikipediaDescriptionAttribution(longDescription, GetValue(values, "wikipedia_url")),
            SourceLinks = BuildExternalSourceLinks(row.WikidataQid, GetValue(values, "wikipedia_url"), null, values),
            Facts = BuildCollectionFacts(entityType, displayWorks, values, contributorGroups, row.WikidataQid),
            Artwork = artwork,
            HeroBrand = BuildHeroBrand(
                entityType,
                FirstNonBlank(row.HeroBrandLabel, GetValue(values, "network"), GetValue(values, "studio"), GetValue(values, "broadcaster")),
                FirstNonBlank(row.HeroBrandImageUrl, GetValue(values, "network_logo_url"), GetValue(values, "network_logo"), GetValue(values, "studio_logo_url"), GetValue(values, "broadcaster_logo_url"))),
            Progress = heroProgress,
            Metadata = BuildCollectionMetadata(entityType, displayWorks, values),
            PrimaryActions = BuildCollectionActions(collectionId, entityType, context, heroProgress),
            SecondaryActions = BuildSecondaryActions(rootWorkId ?? collectionId, entityType, rootWorkId.HasValue && favoriteWorkIds.Contains(rootWorkId.Value)),
            OverflowActions = BuildOverflowActions(collectionId, entityType, isAdminView),
            SequencePlacement = sequencePlacement,
            ContributorGroups = contributorGroups,
            PreviewContributors = BuildPreviewContributors(entityType, contributorGroups),
            CharacterGroups = characterGroups,
            PreviewCharacters = characterGroups.SelectMany(g => g.Characters).Take(12).ToList(),
            RelationshipStrip = relationships,
            Tabs = BuildTabs(entityType, context, isAdminView, hasUniverse: HasUniverseRelationship(relationships)),
            MediaGroups = entityType == DetailEntityType.TvShow
                ? []
                : BuildCollectionMediaGroups(entityType, displayWorks, favoriteWorkIds, expectedTotal),
            IdentityStatus = ResolveIdentityStatus(row.WikidataQid, null, null),
            LibraryStatus = LibraryStatus.Owned,
            IsAdminView = isAdminView,
        };
    }

    private async Task<CollectionDetailRow?> LoadTvShowRootDetailRowAsync(Guid rootWorkId, CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var rawRow = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(
            """
            SELECT w.id AS Id,
                   COALESCE(
                       (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = w.id AND key = 'show_name' LIMIT 1),
                       (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = w.id AND key = 'title' LIMIT 1),
                       'TV Show') AS DisplayName,
                   COALESCE(NULLIF(w.wikidata_qid, ''), (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = w.id AND key = 'wikidata_qid' LIMIT 1)) AS WikidataQid,
                   (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = w.id AND key IN ('description', 'overview') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1) AS Description,
                   (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = w.id AND key = 'tagline' AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1) AS Tagline,
                   (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = w.id AND key IN ('cover_url', 'cover', 'poster_url', 'poster') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1) AS CoverUrl,
                   (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = w.id AND key IN ('background_url', 'background', 'hero_url', 'hero') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1) AS BackgroundUrl,
                   (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = w.id AND key IN ('banner_url', 'banner', 'hero_url', 'hero') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1) AS BannerUrl,
                   (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = w.id AND key IN ('logo_url', 'logo') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1) AS LogoUrl,
                   (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = w.id AND key IN ('network', 'studio', 'broadcaster') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1) AS HeroBrandLabel,
                   (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = w.id AND key IN ('network_logo_url', 'network_logo', 'studio_logo_url', 'broadcaster_logo_url') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1) AS HeroBrandImageUrl
            FROM works w
            WHERE w.id = @rootWorkId
              AND (
                  LOWER(w.media_type) IN ('tv', 'television', 'tv show', 'tv shows')
                  OR EXISTS (
                      SELECT 1
                      FROM works child
                      WHERE child.parent_work_id = w.id
                        AND LOWER(child.media_type) IN ('tv', 'television', 'tv show', 'tv shows')
                      LIMIT 1
                  )
                  OR EXISTS (
                      SELECT 1
                      FROM works season
                      INNER JOIN works episode ON episode.parent_work_id = season.id
                      WHERE season.parent_work_id = w.id
                        AND LOWER(episode.media_type) IN ('tv', 'television', 'tv show', 'tv shows')
                      LIMIT 1
                  )
              )
            LIMIT 1;
            """,
            new { rootWorkId = GuidSql.ToBlob(rootWorkId) },
            cancellationToken: ct));

        return rawRow is null
            ? null
            : new CollectionDetailRow(
                Guid.Parse(StringValue(rawRow.Id) ?? rootWorkId.ToString("D")),
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
        {
            return null;
        }

        var credits = await _personCredits.GetLibraryCreditsAsync(personId, ct);
        var characterRoles = await _personCredits.GetCharacterRolesAsync(personId, ct);
        var aliases = await _persons.FindAliasesAsync(personId, ct);
        var groupMembers = await _personCredits.GetGroupMembersAsync(personId, person.IsGroup, ct);
        var memberOfGroups = person.IsGroup
            ? []
            : await _personCredits.GetGroupMembersAsync(personId, false, ct);
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
            SourceLinks = BuildExternalSourceLinks(person.WikidataQid, wikipediaUrl, null),
            PersonDetails = BuildPersonDetails(person, displayRoles, wikipediaUrl, aliases, groupMembers, memberOfGroups),
            Facts = BuildPersonFacts(person, displayRoles),
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
            new { id = characterId },
            cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

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
            new { id = characterId },
            cancellationToken: ct));

        var portrait = portraits.Select(p => ApiImageUrls.BuildCharacterPortraitUrl(p.Id, p.LocalImagePath, p.ImageUrl)).FirstOrDefault();
        var artwork = BuildArtwork(DetailEntityType.Character, null, null, null, null, portrait ?? row.ImageUrl, new Dictionary<string, string>(), [], 0, null);
        var universeQid = ExtractQid(row.UniverseQid);
        IReadOnlyList<RelationshipGroup> relationships = string.IsNullOrWhiteSpace(row.UniverseQid)
            ? []
            : [new RelationshipGroup
            {
                Title = "Universe",
                Items = [new RelatedEntityChip
                {
                    Id = universeQid ?? row.UniverseQid!,
                    EntityType = RelatedEntityType.Universe,
                    Label = row.UniverseLabel ?? row.UniverseQid!,
                    Route = BuildUniverseExploreRoute(universeQid),
                }],
            }];

        return new DetailPageViewModel
        {
            Id = characterId.ToString("D"),
            EntityType = DetailEntityType.Character,
            PresentationContext = context,
            Title = row.Label,
            Subtitle = FirstNonBlank(row.EntitySubType, "Character") + (string.IsNullOrWhiteSpace(row.UniverseLabel) ? "" : $" • {row.UniverseLabel}"),
            SourceLinks = BuildExternalSourceLinks(row.WikidataQid, null, null),
            Artwork = artwork,
            Metadata = [new MetadataPill { Label = "Character" }, .. MaybePill(row.UniverseLabel)],
            PrimaryActions = [new DetailAction { Key = "appearances", Label = "View Appearances", Icon = "auto_stories", IsPrimary = true }],
            OverflowActions = BuildOverflowActions(characterId, DetailEntityType.Character, isAdminView),
            RelationshipStrip = relationships,
            Tabs = BuildTabs(DetailEntityType.Character, context, isAdminView, hasUniverse: HasUniverseRelationship(relationships)),
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
            new { id },
            cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        var works = await LoadCollectionWorksAsync(id, rootWorkId: null, ct);
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
            SourceLinks = BuildExternalSourceLinks(row.WikidataQid, null, null),
            Artwork = BuildArtwork(DetailEntityType.Universe, row.BackgroundUrl, row.BannerUrl, row.CoverUrl, row.CoverUrl, null, new Dictionary<string, string>(), relatedArt, 0, null),
            Metadata = [new MetadataPill { Label = "Universe" }, new MetadataPill { Label = $"{works.Count} items" }],
            PrimaryActions = [new DetailAction { Key = "timeline", Label = "Explore Timeline", Icon = "account_tree", Route = string.IsNullOrWhiteSpace(row.WikidataQid) ? null : $"/universe/{row.WikidataQid}/explore", IsPrimary = true }],
            OverflowActions = BuildOverflowActions(id, DetailEntityType.Universe, isAdminView),
            ContributorGroups = contributorGroups,
            PreviewContributors = BuildPreviewContributors(DetailEntityType.Universe, contributorGroups),
            CharacterGroups = characterGroups,
            PreviewCharacters = characterGroups.SelectMany(g => g.Characters).Take(12).ToList(),
            RelationshipStrip = relationships,
            Tabs = BuildTabs(DetailEntityType.Universe, context, isAdminView, hasUniverse: true),
            MediaGroups = BuildCollectionMediaGroups(DetailEntityType.Universe, works, new HashSet<Guid>(), expectedTotal: null),
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
            new { workId, defaultOwnerUserId = DefaultOwnerUserId },
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
            ? await _personCredits.BuildForWorkAsync(workId, ct)
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
            {
                return;
            }

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
                        GetValue(canonicalValues, $"{canonicalArrayKey}_profile_url"),
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
        {
            await AddTextCreditAsync("Artists", CreditGroupType.PrimaryArtists, detail.Artist, "Artist", "artist");
        }

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

        if (entityType is DetailEntityType.TvEpisode)
        {
            await AddTextCreditAsync("Guest Stars", CreditGroupType.Cast, GetValue(canonicalValues, MetadataFieldConstants.GuestStar), "Guest Star", MetadataFieldConstants.GuestStar);
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
        {
            return group.GroupType == CreditGroupType.Cast;
        }

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
            {
                return ("Director", 0, true, 2);
            }

            if (group.GroupType == CreditGroupType.Cast)
            {
                return ("Actors", 1, true, 12);
            }

            if (group.GroupType == CreditGroupType.Writers)
            {
                return ("Writers", 2, false, 4);
            }

            if (group.GroupType == CreditGroupType.Producers)
            {
                return ("Producers", 3, false, 4);
            }

            if (group.GroupType == CreditGroupType.MusicCredits)
            {
                return ("Music", 4, false, 3);
            }

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
        {
            return [];
        }

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
            {
                return entries;
            }
        }

        foreach (var targetId in targetIds)
        {
            var claimEntries = await LoadContributorEntriesFromClaimsAsync(targetId, canonicalArrayKey, ct);
            claimEntries = await PreferCollectivePseudonymContributorAsync(canonicalArrayKey, claimEntries, ct);
            if (claimEntries.Count > 0)
            {
                return claimEntries;
            }
        }

        if (string.IsNullOrWhiteSpace(fallbackValue))
        {
            return [];
        }

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
        {
            return entries;
        }

        foreach (var entry in entries.OrderBy(entry => entry.SortOrder))
        {
            var qid = NormalizeQid(entry.Qid);
            if (string.IsNullOrWhiteSpace(qid))
            {
                continue;
            }

            var person = await _persons.FindByQidAsync(qid, ct);
            if (person?.IsPseudonym != true)
            {
                continue;
            }

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
            new { workId },
            cancellationToken: ct));

        if (row is null)
        {
            return [workId];
        }

        var ids = new List<Guid>();
        AddId(row.RootWorkId);
        AddId(row.WorkId);
        AddId(row.AssetId);
        return ids;

        void AddId(Guid? id)
        {
            if (id.HasValue && !ids.Contains(id.Value))
            {
                ids.Add(id.Value);
            }
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
            new { entityId, claimKeys = new[] { canonicalArrayKey, qidKey } },
            cancellationToken: ct))).ToList();

        if (rows.Count == 0)
        {
            return [];
        }

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
                {
                    entries.Add(new ContributorEntry(name, parsed.Qid, entries.Count));
                }
            }

            foreach (var claim in nameClaims)
            {
                var name = claim.ClaimValue.Trim();
                if (string.IsNullOrWhiteSpace(name)
                    || LooksLikeAggregateContributorName(name)
                    || qidByName.ContainsKey(name))
                {
                    continue;
                }

                entries.Add(new ContributorEntry(name, null, entries.Count));
            }

            return DeduplicateContributorEntries(entries);
        }

        for (var i = 0; i < nameClaims.Count; i++)
        {
            var name = nameClaims[i].ClaimValue.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            qidByName.TryGetValue(name, out var qid);
            qid ??= i < qidClaims.Count ? qidClaims[i].Qid : null;
            entries.Add(new ContributorEntry(name, qid, i));
        }

        foreach (var parsed in qidClaims)
        {
            var name = FirstNonBlank(parsed.Label, parsed.Qid);
            if (!string.IsNullOrWhiteSpace(name))
            {
                entries.Add(new ContributorEntry(name, parsed.Qid, entries.Count));
            }
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

    private async Task<SequencePlacementViewModel?> BuildSequencePlacementAsync(Guid workId, LibraryItemDetail detail, DetailEntityType entityType, string? requestedContainerId, CancellationToken ct)
    {
        var labels = ResolveSequenceLabels(entityType);
        var availableContainers = new List<SequenceContainerOptionViewModel>();
        var localContainer = await ResolveLocalSequenceContainerOptionAsync(workId, entityType, detail.MediaType, ct);
        if (localContainer is not null)
        {
            AddSequenceContainerOption(
                availableContainers,
                localContainer.ContainerId,
                localContainer.ContainerTitle,
                localContainer.MediaScope ?? SeriesMediaFilter(entityType, detail.MediaType),
                localContainer.SourceContainerId,
                localContainer.EquivalentContainerIds);
        }

        var allLinkedContainers = await ResolveLinkedManifestSequenceContainerOptionsAsync(workId, entityType, detail.MediaType, ct);
        var wikidataLinkedContainers = allLinkedContainers
            .Where(option => IsWikidataQid(option.SourceContainerId) || IsWikidataQid(option.ContainerId))
            .ToList();
        var linkedContainers = PreferWikidataLinkedSequenceContainers(allLinkedContainers);

        if (wikidataLinkedContainers.Count > 0)
        {
            availableContainers.RemoveAll(option =>
                IsProviderBackedSequenceContainer(option)
                && !wikidataLinkedContainers.Any(wikidata => ShouldMergeSequenceContainerOptions(option, wikidata)));
        }

        foreach (var option in linkedContainers)
        {
            if (IsComicSequenceEntity(entityType)
                && !availableContainers.Any(existing => ShouldMergeSequenceContainerOptions(existing, option)))
            {
                continue;
            }

            AddSequenceContainerOption(
                availableContainers,
                option.ContainerId,
                option.ContainerTitle,
                option.MediaScope ?? SeriesMediaFilter(entityType, detail.MediaType),
                option.SourceContainerId,
                option.EquivalentContainerIds);
        }

        var canonicalContainers = ResolveSequenceContainerOptions(detail, entityType);
        var hasTrustedSequenceContainer = availableContainers.Any(IsLocalOrProviderBackedSequenceContainer)
            || canonicalContainers.Any(option =>
                IsManifestBackedSequenceContainerId(option.ContainerId)
                || IsManifestBackedSequenceContainerId(option.SourceContainerId));
        if (hasTrustedSequenceContainer)
        {
            availableContainers.RemoveAll(IsTitleOnlySequenceContainerOption);
        }

        foreach (var option in canonicalContainers)
        {
            if (IsComicSequenceEntity(entityType) && IsWikidataOnlySequenceContainer(option))
            {
                continue;
            }

            if (linkedContainers.Count > 0
                && !linkedContainers.Any(linked => ShouldMergeSequenceContainerOptions(linked, option)))
            {
                continue;
            }

            if (hasTrustedSequenceContainer && IsTitleOnlySequenceContainerOption(option))
            {
                continue;
            }

            AddSequenceContainerOption(
                availableContainers,
                option.ContainerId,
                option.ContainerTitle,
                option.MediaScope ?? SeriesMediaFilter(entityType, detail.MediaType),
                option.SourceContainerId,
                option.EquivalentContainerIds);
        }

        if (availableContainers.Count == 0)
        {
            return null;
        }

        var hasExplicitSequenceEvidence = linkedContainers.Count > 0
            || (localContainer is not null && IsLocalOrProviderBackedSequenceContainer(localContainer))
            || canonicalContainers.Any(option => IsWikidataQid(NormalizeSequenceContainerId(option.ContainerId)));
        var defaultContainerId = NormalizeSequenceContainerId(GetDetailCanonicalValue(detail, "default_sequence_container_id"));
        var requestedQid = NormalizeSequenceContainerId(requestedContainerId);
        var selectedContainer = availableContainers.FirstOrDefault(option =>
            !string.IsNullOrWhiteSpace(requestedQid)
            && SequenceContainerOptionMatches(option, requestedQid))
            ?? availableContainers.FirstOrDefault(option =>
            !string.IsNullOrWhiteSpace(defaultContainerId)
            && SequenceContainerOptionMatches(option, defaultContainerId))
            ?? availableContainers[0];
        var containerTitle = SeriesDisplayFormatter.NormalizeContainerTitle(selectedContainer.ContainerTitle, isStructuralSeries: true)
            ?? selectedContainer.ContainerTitle;
        var containerId = NormalizeSequenceContainerId(selectedContainer.ContainerId) ?? selectedContainer.ContainerId;
        var sourceContainerId = NormalizeSequenceContainerId(selectedContainer.SourceContainerId) ?? selectedContainer.SourceContainerId;
        var manifestContainerId = IsManifestBackedSequenceContainerId(sourceContainerId)
            ? sourceContainerId
            : containerId;

        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync<SequenceRow>(new CommandDefinition(
            """
            WITH current_lineage AS (
                SELECT COALESCE(current_grandparent.id, current_parent.id, current_work.id) AS RootWorkId,
                       current_work.collection_id AS CollectionId
                FROM works current_work
                LEFT JOIN works current_parent ON current_parent.id = current_work.parent_work_id
                LEFT JOIN works current_grandparent ON current_grandparent.id = current_parent.parent_work_id
                WHERE current_work.id = @workId
            )
            SELECT w.id AS WorkId,
                   CAST(COALESCE(
                       (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key = 'issue_title' LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = w.id AND key = 'issue_title' LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key = 'episode_title' LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = w.id AND key = 'episode_title' LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key = 'title' LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = w.id AND key = 'title' LIMIT 1),
                       'Untitled') AS TEXT) AS Title,
                   CAST(w.media_type AS TEXT) AS MediaType,
                   CAST(COALESCE(
                       (SELECT claim_value FROM metadata_claims WHERE entity_id = ma.id AND claim_key = 'series_position' AND provider_id = @wikidataProviderId ORDER BY confidence DESC, claimed_at DESC LIMIT 1),
                       (SELECT claim_value FROM metadata_claims WHERE entity_id = w.id AND claim_key = 'series_position' AND provider_id = @wikidataProviderId ORDER BY confidence DESC, claimed_at DESC LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key IN ('series_position', 'issue_number') LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = w.id AND key IN ('series_position', 'ordinal') LIMIT 1),
                       CASE WHEN w.ordinal_sort IS NOT NULL AND ABS(w.ordinal_sort - ROUND(w.ordinal_sort)) > 0.0001 THEN CAST(w.ordinal_sort AS TEXT) END,
                       CASE WHEN w.ordinal IS NOT NULL THEN CAST(w.ordinal AS TEXT) END) AS TEXT) AS PositionLabel,
                   w.ordinal_sort AS PositionSort,
                   CAST(COALESCE(
                       (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key = 'season_number' LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = w.id AND key = 'season_number' LIMIT 1)) AS TEXT) AS SeasonLabel,
                   CAST(COALESCE(
                       (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key = 'episode_number' LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = w.id AND key = 'episode_number' LIMIT 1)) AS TEXT) AS EpisodeLabel,
                   CAST(COALESCE(
                       (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key IN ('cover_url', 'cover') LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = w.id AND key IN ('cover_url', 'cover') LIMIT 1)) AS TEXT) AS ArtworkUrl
                  ,CAST(COALESCE(
                       (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key IN ('publication_date', 'release_date', 'year') LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = w.id AND key IN ('publication_date', 'release_date', 'year') LIMIT 1)) AS TEXT) AS PublicationDate
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
                workId,
                series = containerTitle,
                mediaFilter = SeriesMediaFilter(entityType, detail.MediaType),
                wikidataProviderId = WellKnownProviders.Wikidata.ToString(),
            },
            cancellationToken: ct))).ToList();

        var items = rows.Select(row =>
        {
            var positionLabel = ResolveSequencePositionLabel(entityType, row.PositionLabel, row.EpisodeLabel);
            var positionNumber = TryParseSeriesPosition(positionLabel);
            var positionSort = row.PositionSort ?? TryParseSeriesPositionSort(positionLabel);
            var group = ResolveSequenceGroup(entityType, row.SeasonLabel);
            return new SequenceItemViewModel
            {
                Id = row.WorkId.ToString("D"),
                EntityType = entityType,
                Title = ResolveSequenceItemTitle(entityType, row.Title, containerTitle, positionLabel),
                ArtworkUrl = row.ArtworkUrl,
                PublicationDate = row.PublicationDate,
                PositionNumber = positionNumber,
                PositionSort = positionSort,
                PositionLabel = positionLabel ?? positionNumber?.ToString(CultureInfo.InvariantCulture),
                PositionText = FormatSequencePositionText(entityType, positionLabel, positionNumber),
                GroupKey = group.Key,
                GroupTitle = group.Title,
                IsCurrent = row.WorkId == workId,
                IsOwned = true,
                ProgressState = LibraryProgressState.Unknown,
            };
        }).ToList();
        if (!IsComicSequenceEntity(entityType))
        {
            items = await MergeSequenceManifestPlaceholdersAsync(items, manifestContainerId, detail.WikidataQid, workId, entityType, ct);
            items = await ApplyExactManifestPositionsAsync(items, manifestContainerId, entityType, ct);
        }

        if (!items.Any(item => item.IsCurrent))
        {
            var fallbackPositionLabel = entityType == DetailEntityType.TvEpisode
                ? FirstText(detail.EpisodeNumber, GetDetailCanonicalValue(detail, MetadataFieldConstants.EpisodeNumber))
                : detail.SeriesPosition;
            var fallbackPositionNumber = TryParseSeriesPosition(fallbackPositionLabel);
            var fallbackPositionSort = TryParseSeriesPositionSort(fallbackPositionLabel);
            var fallbackGroup = ResolveSequenceGroup(entityType, FirstText(detail.SeasonNumber, GetDetailCanonicalValue(detail, MetadataFieldConstants.SeasonNumber)));
            items.Add(new SequenceItemViewModel
            {
                Id = workId.ToString("D"),
                EntityType = entityType,
                Title = detail.Title,
                ArtworkUrl = detail.CoverUrl,
                PublicationDate = FirstNonBlank(detail.ReleaseDate, detail.Year),
                PositionLabel = fallbackPositionLabel,
                PositionNumber = fallbackPositionNumber,
                PositionSort = fallbackPositionSort,
                PositionText = FormatSequencePositionText(entityType, fallbackPositionLabel, fallbackPositionNumber),
                GroupKey = fallbackGroup.Key,
                GroupTitle = fallbackGroup.Title,
                IsCurrent = true,
                IsOwned = true,
            });
        }

        items = DeduplicateManifestMergeItems(items).ToList();
        items = NormalizeSequenceItems(items, entityType);
        items = SortSequenceItems(items);
        var hasPositionEvidence = items.Any(HasSequencePositionEvidence);
        if (items.Count <= 1 && !hasExplicitSequenceEvidence)
        {
            return null;
        }

        if (!hasExplicitSequenceEvidence && !hasPositionEvidence)
        {
            return null;
        }

        var expectedTotalCandidate = await LoadSequenceExpectedTotalAsync(containerId, ct)
            ?? await LoadSequenceExpectedTotalAsync(sourceContainerId, ct);
        var containerMetadata = await LoadSequenceContainerMetadataAsync(containerId, sourceContainerId, ct);
        var containerDescription = containerMetadata?.Description
            ?? GetDetailCanonicalValue(detail, "series_description")
            ?? (entityType is DetailEntityType.BookSeries or DetailEntityType.ComicSeries
                or DetailEntityType.MovieSeries or DetailEntityType.TvShow
                    ? detail.Description
                    : null);
        var currentIndex = Math.Max(0, items.FindIndex(i => i.IsCurrent));
        var current = items[currentIndex];
        var mainSequenceItemCount = items.Count(item =>
            string.IsNullOrWhiteSpace(item.MembershipScope)
            || string.Equals(item.MembershipScope, SeriesMembershipScopeNames.MainSequence, StringComparison.OrdinalIgnoreCase));
        var expectedTotal = expectedTotalCandidate is > 0 && mainSequenceItemCount >= expectedTotalCandidate
            ? expectedTotalCandidate
            : null;
        var groups = BuildSequenceGroups(items, labels.ItemPluralLabel, expectedTotal);
        var currentGroup = groups.FirstOrDefault(group => string.Equals(group.Key, current.GroupKey ?? "all", StringComparison.OrdinalIgnoreCase));
        var totalKnownItems = currentGroup?.TotalKnownItems ?? Math.Max(items.Count, expectedTotal ?? 0);
        var distinctContainers = DeduplicateSequenceContainerOptions(availableContainers);
        return new SequencePlacementViewModel
        {
            ContainerId = containerId,
            SourceContainerId = sourceContainerId,
            ContainerTitle = containerTitle,
            ContainerDescription = containerDescription,
            ContainerWikipediaUrl = containerMetadata?.WikipediaUrl,
            SelectedContainerId = containerId,
            CanChooseContainer = distinctContainers.Count > 1,
            CanSetDefaultContainer = distinctContainers.Count > 1
                && !SequenceContainerOptionMatches(selectedContainer, defaultContainerId),
            AvailableContainers = distinctContainers.Select(option => new SequenceContainerOptionViewModel
            {
                ContainerId = option.ContainerId,
                SourceContainerId = option.SourceContainerId,
                ContainerTitle = SeriesDisplayFormatter.NormalizeContainerTitle(option.ContainerTitle, isStructuralSeries: true)
                    ?? option.ContainerTitle,
                MediaScope = option.MediaScope,
                EquivalentContainerIds = option.EquivalentContainerIds,
                IsSelected = SequenceContainerOptionMatches(option, selectedContainer.ContainerId)
                    || SequenceContainerOptionMatches(option, selectedContainer.SourceContainerId),
                IsDefault = !string.IsNullOrWhiteSpace(defaultContainerId)
                    && SequenceContainerOptionMatches(option, defaultContainerId),
            }).ToList(),
            UniverseId = detail.UniverseSummary?.UniverseQid,
            UniverseTitle = detail.UniverseSummary?.UniverseName,
            ContainerLabel = labels.ContainerLabel,
            ItemLabel = labels.ItemLabel,
            ItemPluralLabel = labels.ItemPluralLabel,
            GroupLabel = groups.Count > 1 ? "Series scope" : labels.GroupLabel,
            CurrentGroupKey = current.GroupKey ?? groups.FirstOrDefault()?.Key,
            PositionNumber = current.PositionNumber,
            PositionSort = current.PositionSort,
            TotalKnownItems = totalKnownItems,
            HasAuthoritativeTotal = expectedTotal.HasValue,
            PositionLabel = current.PositionLabel,
            PositionText = current.PositionText,
            PositionSummary = BuildSequencePositionSummary(entityType, current, containerTitle, labels),
            OrderingType = entityType switch
            {
                DetailEntityType.TvEpisode => SequenceOrderingType.EpisodeNumber,
                DetailEntityType.ComicIssue => SequenceOrderingType.IssueNumber,
                _ => SequenceOrderingType.LibraryOrder,
            },
            PreviousItem = currentIndex > 0 ? items[currentIndex - 1] : null,
            CurrentItem = current,
            NextItem = currentIndex < items.Count - 1 ? items[currentIndex + 1] : null,
            OrderedItems = items,
            Groups = groups,
        };
    }

    private async Task<SequenceContainerMetadataRow?> LoadSequenceContainerMetadataAsync(
        string? containerId,
        string? sourceContainerId,
        CancellationToken ct)
    {
        var localId = Guid.TryParse(containerId, out var parsedContainerId)
            ? parsedContainerId
            : Guid.TryParse(sourceContainerId, out var parsedSourceContainerId)
                ? parsedSourceContainerId
                : (Guid?)null;
        var containerQid = IsWikidataQid(containerId) ? containerId : null;
        var sourceQid = IsWikidataQid(sourceContainerId) ? sourceContainerId : null;

        if (localId is null && containerQid is null && sourceQid is null)
            return null;

        using var conn = _db.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<SequenceContainerMetadataDbRow>(new CommandDefinition(
            """
            SELECT CAST(COALESCE(
                       NULLIF(TRIM(c.description), ''),
                       (SELECT NULLIF(TRIM(CAST(cv.value AS TEXT)), '')
                        FROM canonical_values cv
                        WHERE cv.entity_id = c.id
                          AND cv.key IN ('wikipedia_extract', 'description', 'overview')
                        ORDER BY CASE cv.key WHEN 'wikipedia_extract' THEN 0 WHEN 'description' THEN 1 ELSE 2 END
                        LIMIT 1),
                       (SELECT NULLIF(TRIM(ql.description), '')
                        FROM qid_labels ql
                        WHERE ql.qid = c.wikidata_qid
                        LIMIT 1)) AS TEXT) AS Description,
                   CAST((SELECT NULLIF(TRIM(CAST(cv.value AS TEXT)), '')
                    FROM canonical_values cv
                    WHERE cv.entity_id = c.id
                      AND cv.key = 'wikipedia_url'
                    LIMIT 1) AS TEXT) AS WikipediaUrl
            FROM collections c
            WHERE (@localId IS NOT NULL AND c.id = @localId)
               OR (@containerQid IS NOT NULL AND c.wikidata_qid = @containerQid)
               OR (@sourceQid IS NOT NULL AND c.wikidata_qid = @sourceQid)
            ORDER BY CASE WHEN @localId IS NOT NULL AND c.id = @localId THEN 0 ELSE 1 END
            LIMIT 1
            """,
            new { localId, containerQid, sourceQid },
            cancellationToken: ct));

        return row is null
            ? null
            : new SequenceContainerMetadataRow(
                NormalizeSqliteText(row.Description),
                NormalizeSqliteText(row.WikipediaUrl));
    }

    private static string? NormalizeSqliteText(object? value)
    {
        var text = value switch
        {
            null or DBNull => null,
            string stringValue => stringValue,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private async Task<int?> LoadSequenceExpectedTotalAsync(string? containerId, CancellationToken ct)
    {
        var normalized = NormalizeSequenceContainerId(containerId);
        using var conn = _db.CreateConnection();

        if (Guid.TryParse(normalized, out var collectionId))
        {
            var collectionTotal = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
                """
                SELECT CAST(value AS INTEGER)
                FROM canonical_values
                WHERE entity_id = @collectionId
                  AND key = @key
                  AND CAST(value AS INTEGER) > 0
                  AND EXISTS (
                      SELECT 1
                      FROM canonical_values scope
                      WHERE scope.entity_id = @collectionId
                        AND scope.key = @scopeKey
                        AND scope.value = @mainSequenceScope)
                LIMIT 1;
                """,
                new
                {
                    collectionId,
                    key = MetadataFieldConstants.SequenceTotal,
                    scopeKey = MetadataFieldConstants.SequenceTotalScope,
                    mainSequenceScope = SequenceCountScope.MainSequence.ToString(),
                },
                cancellationToken: ct));

            if (collectionTotal is > 0)
                return collectionTotal;
        }

        if (!IsManifestBackedSequenceContainerId(normalized))
        {
            return null;
        }

        return await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            """
            SELECT CAST(COALESCE(
                json_extract(api_metadata_json, '$.expectedTotal'),
                json_extract(api_metadata_json, '$.expected_total')) AS INTEGER)
            FROM series_manifest_hydrations
            WHERE series_qid = @seriesQid
              AND json_extract(api_metadata_json, '$.completeness') = 'Complete'
              AND COALESCE(
                    CAST(json_extract(api_metadata_json, '$.expectedTotal') AS INTEGER),
                    CAST(json_extract(api_metadata_json, '$.expected_total') AS INTEGER),
                    0) > 0
            ORDER BY last_hydrated_at DESC
            LIMIT 1;
            """,
            new { seriesQid = normalized },
            cancellationToken: ct));
    }

    private static int? AuthoritativeManifestTotal(SeriesManifestViewDto? manifest)
    {
        if (manifest?.ExpectedTotal is not > 0
            || string.Equals(manifest.ExpectedTotalSource, "wikidata-manifest-rows", StringComparison.OrdinalIgnoreCase)
            || manifest.ExpectedTotalConfidence is < 0.8)
        {
            return null;
        }

        return manifest.ExpectedTotal;
    }

    private static bool HasSequencePositionEvidence(SequenceItemViewModel item)
        => item.PositionNumber.HasValue
           || !string.IsNullOrWhiteSpace(item.PositionLabel)
           || !string.IsNullOrWhiteSpace(item.PositionText);

    private async Task<IReadOnlyList<MediaGroupingViewModel>> BuildWorkMediaGroupsAsync(Guid workId, DetailEntityType entityType, Guid? profileId, CancellationToken ct)
    {
        if (entityType == DetailEntityType.Audiobook)
        {
            var groups = new List<MediaGroupingViewModel>();
            var chapterGroup = await BuildAudiobookChapterGroupAsync(workId, profileId, ct);
            if (chapterGroup is not null)
            {
                groups.Add(chapterGroup);
            }

            var audioRecommendations = await _recommendations.LoadAsync(workId, entityType, ct);
            if (audioRecommendations.Count > 0)
            {
                groups.Add(new MediaGroupingViewModel
                {
                    Key = "more-like-this",
                    Title = "More Like This",
                    Items = audioRecommendations,
                });
            }

            return groups;
        }

        var recommendations = await _recommendations.LoadAsync(workId, entityType, ct);
        if (recommendations.Count == 0)
        {
            return [];
        }

        return
        [
            new MediaGroupingViewModel
            {
                Key = "more-like-this",
                Title = "More Like This",
                Items = recommendations,
            }
        ];
    }

    private async Task<MediaGroupingViewModel?> BuildAudiobookChapterGroupAsync(Guid workId, Guid? profileId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<AudiobookAssetRow>(new CommandDefinition(
            """
            SELECT w.id AS WorkId,
                   ma.id AS AssetId,
                   COALESCE(
                       MAX(CASE WHEN wcv.key = 'title' THEN wcv.value END),
                       MAX(CASE WHEN acv.key = 'title' THEN acv.value END),
                       'Full audiobook'
                   ) AS Title,
                   COALESCE(
                       MAX(CASE WHEN wcv.key = 'author' THEN wcv.value END),
                       MAX(CASE WHEN acv.key = 'author' THEN acv.value END)
                   ) AS Author,
                   COALESCE(
                       MAX(CASE WHEN wcv.key = 'narrator' THEN wcv.value END),
                       MAX(CASE WHEN acv.key = 'narrator' THEN acv.value END)
                   ) AS Narrator,
                   COALESCE(
                       MAX(CASE WHEN wcv.key IN ('duration_seconds', 'duration_sec') THEN wcv.value END),
                       MAX(CASE WHEN acv.key IN ('duration_seconds', 'duration_sec') THEN acv.value END)
                   ) AS DurationSecondsValue,
                   COALESCE(
                       MAX(CASE WHEN wcv.key = 'duration' THEN wcv.value END),
                       MAX(CASE WHEN acv.key = 'duration' THEN acv.value END),
                       MAX(CASE WHEN wcv.key = 'runtime' THEN wcv.value END),
                       MAX(CASE WHEN acv.key = 'runtime' THEN acv.value END)
                   ) AS Duration
            FROM works w
            INNER JOIN editions e ON e.work_id = w.id
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            LEFT JOIN canonical_values wcv ON wcv.entity_id = w.id
            LEFT JOIN canonical_values acv ON acv.entity_id = ma.id
            WHERE w.id = @workId
              AND LOWER(w.media_type) IN ('audiobook', 'audiobooks', 'audio')
            GROUP BY w.id, ma.id
            ORDER BY ma.presented_at IS NULL, ma.presented_at DESC, ma.file_path_root
            LIMIT 1;
            """,
            new { workId },
            cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        MediaEngine.Contracts.Playback.PlaybackManifestDto? manifest = null;
        if (_playback is not null && row.AssetId != Guid.Empty)
        {
            try
            {
                manifest = await _playback.BuildManifestAsync(row.AssetId, "web", profileId, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Could not build playback manifest for audiobook asset {AssetId}; falling back to full-audiobook detail row.",
                    row.AssetId);
            }
        }

        var chapters = manifest?.Chapters ?? [];
        var totalDurationSeconds = ResolveAudiobookTotalDurationSeconds(row, chapters);
        var resume = await LoadAudiobookResumeAsync(conn, row.WorkId, row.AssetId, manifest?.Resume, totalDurationSeconds, ct);
        var resumeSeconds = resume?.PositionSeconds;
        var items = chapters.Count > 0
            ? chapters.Select(chapter => ToAudiobookChapterItem(row, chapter, resumeSeconds)).ToList()
            : [ToFullAudiobookItem(row, manifest, resume)];

        return new MediaGroupingViewModel
        {
            Key = "chapters",
            Title = chapters.Count > 0 ? "Chapters" : "Playback",
            Items = items,
            OwnedCount = items.Count,
            TotalCount = items.Count,
        };
    }

    private static MediaGroupingItemViewModel ToAudiobookChapterItem(AudiobookAssetRow row, MediaEngine.Contracts.Playback.PlaybackChapterDto chapter, double? resumeSeconds)
    {
        var durationSeconds = chapter.EndSeconds.HasValue && chapter.EndSeconds.Value > chapter.StartSeconds
            ? chapter.EndSeconds.Value - chapter.StartSeconds
            : chapter.Index == 0 && chapter.StartSeconds <= 0
                ? TryParseAudioDurationSeconds(row.DurationSecondsValue) ?? TryParseDurationSeconds(row.Duration)
                : (double?)null;
        var progressPercent = CalculateChapterProgress(resumeSeconds, chapter.StartSeconds, chapter.EndSeconds);

        return new MediaGroupingItemViewModel
        {
            Id = row.WorkId.ToString("D"),
            EntityType = DetailEntityType.Audiobook,
            Title = string.IsNullOrWhiteSpace(chapter.Title) ? $"Chapter {chapter.Index + 1}" : chapter.Title,
            Subtitle = FirstText(row.Author, row.Narrator),
            ArtworkUrl = $"/stream/{row.AssetId}/cover",
            TrackNumber = (chapter.Index + 1).ToString(CultureInfo.InvariantCulture),
            Duration = FormatSecondsDuration(durationSeconds),
            DurationSeconds = durationSeconds,
            AssetId = row.AssetId.ToString("D"),
            ChapterIndex = chapter.Index,
            StartSeconds = chapter.StartSeconds,
            EndSeconds = chapter.EndSeconds,
            ResumePositionSeconds = IsPositionWithinChapter(resumeSeconds, chapter.StartSeconds, chapter.EndSeconds) ? resumeSeconds : null,
            ProgressPercent = progressPercent,
            Metadata = BuildEpisodeMetadata(FormatSecondsDuration(durationSeconds), null),
            Actions = [new DetailAction { Key = "play-chapter", Label = progressPercent is > 0 and < 100 ? "Continue" : "Play", Icon = "play_arrow" }],
            IsOwned = true,
            ProgressState = progressPercent >= 100
                ? LibraryProgressState.Completed
                : progressPercent is > 0
                    ? LibraryProgressState.InProgress
                    : LibraryProgressState.Unstarted,
        };
    }

    private static MediaGroupingItemViewModel ToFullAudiobookItem(AudiobookAssetRow row, MediaEngine.Contracts.Playback.PlaybackManifestDto? manifest, MediaEngine.Contracts.Playback.PlaybackResumeDto? resume)
    {
        double? durationSeconds = TryParseAudioDurationSeconds(row.DurationSecondsValue)
            ?? TryParseDurationSeconds(row.Duration);
        durationSeconds ??= manifest?.Chapters
            .Where(chapter => chapter.EndSeconds.HasValue)
            .Select(chapter => chapter.EndSeconds!.Value)
            .DefaultIfEmpty()
            .Max();
        if (durationSeconds <= 0)
        {
            durationSeconds = null;
        }

        return new MediaGroupingItemViewModel
        {
            Id = row.WorkId.ToString("D"),
            EntityType = DetailEntityType.Audiobook,
            Title = "Full audiobook",
            Subtitle = FirstText(row.Author, row.Narrator),
            ArtworkUrl = $"/stream/{row.AssetId}/cover",
            TrackNumber = "1",
            Duration = FormatSecondsDuration(durationSeconds),
            DurationSeconds = durationSeconds,
            AssetId = row.AssetId.ToString("D"),
            ChapterIndex = 0,
            StartSeconds = 0,
            EndSeconds = durationSeconds,
            ResumePositionSeconds = resume?.PositionSeconds,
            ProgressPercent = resume?.ProgressPct,
            Metadata = BuildEpisodeMetadata(FormatSecondsDuration(durationSeconds), null),
            Actions = [new DetailAction { Key = "play-chapter", Label = resume?.PositionSeconds is > 0 ? "Continue" : "Play", Icon = "play_arrow" }],
            IsOwned = true,
            ProgressState = resume?.ProgressPct >= 100
                ? LibraryProgressState.Completed
                : resume?.PositionSeconds is > 0
                    ? LibraryProgressState.InProgress
                    : LibraryProgressState.Unstarted,
        };
    }

    private static async Task<MediaEngine.Contracts.Playback.PlaybackResumeDto?> LoadAudiobookResumeAsync(
        System.Data.IDbConnection conn,
        Guid workId,
        Guid assetId,
        MediaEngine.Contracts.Playback.PlaybackResumeDto? fallback,
        double? durationSeconds,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var rows = (await conn.QueryAsync<AudiobookResumeRow>(new CommandDefinition(
            """
            SELECT 0 AS SourceRank,
                   last_position_seconds AS PositionSeconds,
                   duration_seconds AS DurationSeconds,
                   CASE WHEN duration_seconds IS NOT NULL AND duration_seconds > 0 THEN (last_position_seconds / duration_seconds) * 100.0 ELSE NULL END AS ProgressPct,
                   last_heartbeat_at AS LastAccessed,
                   NULL AS ExtendedProperties
            FROM audiobook_listen_active_segments
            WHERE work_id = @workId
              AND asset_id = @assetId
            UNION ALL
            SELECT 1 AS SourceRank,
                   position_seconds AS PositionSeconds,
                   duration_seconds AS DurationSeconds,
                   progress_pct AS ProgressPct,
                   ended_at AS LastAccessed,
                   NULL AS ExtendedProperties
            FROM audiobook_listen_history
            WHERE work_id = @workId
              AND asset_id = @assetId
            UNION ALL
            SELECT 2 AS SourceRank,
                   NULL AS PositionSeconds,
                   NULL AS DurationSeconds,
                   progress_pct AS ProgressPct,
                   last_accessed AS LastAccessed,
                   extended_properties AS ExtendedProperties
            FROM user_states
            WHERE asset_id = @assetId
            ORDER BY SourceRank ASC, LastAccessed DESC
            LIMIT 25;
            """,
            new { workId, assetId },
            cancellationToken: ct))).ToList();

        if (rows.Count == 0)
        {
            return NormalizeAudiobookResumePosition(fallback, durationSeconds);
        }

        var resumes = rows
            .Select(row => BuildAudiobookResume(row, fallback, durationSeconds))
            .Where(resume => resume is not null)
            .Select(resume => resume!)
            .ToList();

        return resumes.FirstOrDefault(IsMeaningfulAudiobookResume)
            ?? (IsMeaningfulAudiobookResume(NormalizeAudiobookResumePosition(fallback, durationSeconds))
                ? NormalizeAudiobookResumePosition(fallback, durationSeconds)
                : null)
            ?? resumes.FirstOrDefault()
            ?? NormalizeAudiobookResumePosition(fallback, durationSeconds);
    }

    private static MediaEngine.Contracts.Playback.PlaybackResumeDto? BuildAudiobookResume(
        AudiobookResumeRow row,
        MediaEngine.Contracts.Playback.PlaybackResumeDto? fallback,
        double? knownDurationSeconds)
    {
        var positionSeconds = row.PositionSeconds
            ?? TryReadExtendedPropertyDouble(row.ExtendedProperties, "position_seconds")
            ?? (row.SourceRank == 2 ? fallback?.PositionSeconds : null);
        var durationSeconds = row.DurationSeconds
            ?? TryReadExtendedPropertyDouble(row.ExtendedProperties, "duration_seconds")
            ?? knownDurationSeconds;
        var progressPct = row.ProgressPct
            ?? (positionSeconds.HasValue && durationSeconds is > 0
                ? positionSeconds.Value / durationSeconds.Value * 100d
                : fallback?.ProgressPct);
        if (!positionSeconds.HasValue && progressPct is > 0 and < 100 && durationSeconds is > 0)
        {
            positionSeconds = durationSeconds.Value * Math.Clamp(progressPct.Value, 0, 100) / 100d;
        }
        if (positionSeconds is >= 0
            && progressPct is > 1 and < 100
            && durationSeconds is > 0
            && positionSeconds.Value <= new MediaEngine.Contracts.Playback.ListeningSettingsDto().AudiobookNearStartGuardSeconds)
        {
            positionSeconds = durationSeconds.Value * Math.Clamp(progressPct.Value, 0, 100) / 100d;
        }
        if (positionSeconds.HasValue)
        {
            positionSeconds = durationSeconds is > 0
                ? Math.Clamp(positionSeconds.Value, 0, durationSeconds.Value)
                : Math.Max(0, positionSeconds.Value);
        }

        if (!positionSeconds.HasValue && !progressPct.HasValue)
        {
            return NormalizeAudiobookResumePosition(fallback, knownDurationSeconds);
        }

        return new MediaEngine.Contracts.Playback.PlaybackResumeDto
        {
            PositionSeconds = positionSeconds,
            ProgressPct = Math.Clamp(progressPct ?? 0, 0, 100),
            LastAccessed = DateTimeOffset.TryParse(row.LastAccessed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : fallback?.LastAccessed,
        };
    }

    private static double? ResolveAudiobookTotalDurationSeconds(
        AudiobookAssetRow row,
        IReadOnlyList<MediaEngine.Contracts.Playback.PlaybackChapterDto> chapters)
    {
        var chapterEnd = chapters
            .Where(chapter => chapter.EndSeconds is > 0)
            .Select(chapter => chapter.EndSeconds!.Value)
            .DefaultIfEmpty()
            .Max();
        if (chapterEnd > 0)
        {
            return chapterEnd;
        }

        return TryParseAudioDurationSeconds(row.DurationSecondsValue)
            ?? TryParseDurationSeconds(row.Duration);
    }

    private static MediaEngine.Contracts.Playback.PlaybackResumeDto? NormalizeAudiobookResumePosition(
        MediaEngine.Contracts.Playback.PlaybackResumeDto? resume,
        double? durationSeconds)
    {
        if (resume is null)
        {
            return null;
        }

        var duration = durationSeconds is > 0 ? durationSeconds.Value : (double?)null;
        var progress = Math.Clamp(resume.ProgressPct, 0, 100);
        var position = resume.PositionSeconds;
        if (!position.HasValue && progress is > 0 and < 100 && duration.HasValue)
        {
            position = duration.Value * progress / 100d;
        }

        if (position.HasValue)
        {
            position = duration.HasValue
                ? Math.Clamp(position.Value, 0, duration.Value)
                : Math.Max(0, position.Value);
            if (duration.HasValue && progress <= 0)
            {
                progress = Math.Clamp(position.Value / duration.Value * 100d, 0, 100);
            }
        }

        return resume with
        {
            PositionSeconds = position,
            ProgressPct = progress,
        };
    }

    private static bool IsMeaningfulAudiobookResume(MediaEngine.Contracts.Playback.PlaybackResumeDto? resume)
        => resume?.PositionSeconds is > 0 || resume?.ProgressPct is > 0;

    private static bool IsPositionWithinChapter(double? positionSeconds, double startSeconds, double? endSeconds)
        => positionSeconds.HasValue
            && positionSeconds.Value >= startSeconds
            && (!endSeconds.HasValue || positionSeconds.Value < endSeconds.Value);

    private static double? TryReadExtendedPropertyDouble(string? extendedProperties, string key)
    {
        if (string.IsNullOrWhiteSpace(extendedProperties))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(extendedProperties);
            if (!doc.RootElement.TryGetProperty(key, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var numeric))
            {
                return numeric;
            }

            if (value.ValueKind == JsonValueKind.String
                && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
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

    private async Task<List<SequenceContainerOptionViewModel>> ResolveLinkedManifestSequenceContainerOptionsAsync(
        Guid workId,
        DetailEntityType entityType,
        string mediaType,
        CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync(new CommandDefinition(
            """
            SELECT smi.series_qid AS ContainerId,
                   smi.collection_id AS CollectionId,
                   CAST(COALESCE(NULLIF(h.series_label, ''), NULLIF(c.display_name, ''), NULLIF(smi.parent_collection_label, ''), smi.series_qid) AS TEXT) AS ContainerTitle,
                   COALESCE(
                       MAX(COALESCE(
                           CAST(json_extract(h.api_metadata_json, '$.expectedTotal') AS INTEGER),
                           CAST(json_extract(h.api_metadata_json, '$.expected_total') AS INTEGER))),
                       COUNT(*)) AS ItemCount,
                   MIN(CASE WHEN smi.parsed_ordinal IS NOT NULL OR NULLIF(smi.raw_ordinal, '') IS NOT NULL THEN 0 ELSE 1 END) AS HasOrderingRank
            FROM series_manifest_items smi
            LEFT JOIN series_manifest_hydrations h ON h.series_qid = smi.series_qid
            LEFT JOIN collections c ON c.id = smi.collection_id
            WHERE smi.linked_work_id = @workId
              AND NULLIF(smi.series_qid, '') IS NOT NULL
              AND smi.membership_scope IN ('MainSequence', 'Supplementary', 'Unpositioned')
              AND COALESCE(CAST(json_extract(h.api_metadata_json, '$.containerKind') AS TEXT), 'OrderedSeries') NOT IN ('Franchise', 'Universe', 'WikimediaList', 'PublisherOrProductionList')
            GROUP BY smi.series_qid
            ORDER BY HasOrderingRank, ItemCount DESC, ContainerTitle;
            """,
            new { workId },
            cancellationToken: ct));

        var mediaScope = SeriesMediaFilter(entityType, mediaType);
        var options = new List<SequenceContainerOptionViewModel>();
        foreach (var row in rows)
        {
            var collectionId = StringValue(row.CollectionId);
            var containerId = StringValue(row.ContainerId);
            IReadOnlyList<SeriesManifestItemRecord> manifestItems = string.IsNullOrWhiteSpace(containerId)
                ? Array.Empty<SeriesManifestItemRecord>()
                : await _seriesManifests.GetItemsBySeriesQidAsync(containerId, ct);
            var memberFingerprint = BuildManifestMemberFingerprint(manifestItems);
            IReadOnlyList<string> collectionAliases = new[] { collectionId, memberFingerprint }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToList();
            AddSequenceContainerOption(
                options,
                containerId,
                FormatSequenceContainerTitle(StringValue(row.ContainerTitle)),
                mediaScope,
                sourceContainerId: StringValue(row.ContainerId),
                equivalentContainerIds: collectionAliases);
        }

        return options;
    }

    private static string? BuildManifestMemberFingerprint(IReadOnlyList<SeriesManifestItemRecord> items)
    {
        var positionedMainMembers = items
            .Where(item => string.Equals(item.MembershipScope, SeriesMembershipScopeNames.MainSequence, StringComparison.OrdinalIgnoreCase))
            .Select(item => new
            {
                Title = NormalizeSeriesTitle(item.ItemLabel),
                Position = ManifestSourcePosition(item),
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Title) && item.Position.HasValue)
            .OrderBy(item => item.Position)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (positionedMainMembers.Count < 2)
        {
            return null;
        }

        var identity = string.Join('|', positionedMainMembers.Select(item =>
            $"{item.Position!.Value.ToString("0.####", CultureInfo.InvariantCulture)}:{item.Title}"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return $"sequence-fingerprint:{hash}";
    }

    private static IReadOnlyList<SequenceContainerOptionViewModel> ResolveSequenceContainerOptions(LibraryItemDetail detail, DetailEntityType entityType)
    {
        var mediaScope = SeriesMediaFilter(entityType, detail.MediaType);
        var options = new List<SequenceContainerOptionViewModel>();
        var seriesTitle = FirstText(detail.Series, GetDetailCanonicalValue(detail, MetadataFieldConstants.Series));
        var defaultContainerId = NormalizeSequenceContainerId(GetDetailCanonicalValue(detail, "default_sequence_container_id"));
        var defaultContainerTitle = GetDetailCanonicalValue(detail, "default_sequence_container_label");

        AddSequenceContainerOption(options, defaultContainerId, defaultContainerTitle, mediaScope, sourceContainerId: defaultContainerId);
        AddSequenceContainerOptionFromCanonicalQid(options, GetDetailCanonicalValue(detail, "series_qid"), seriesTitle, mediaScope);
        AddSequenceContainerOptionFromCanonicalQid(options, GetDetailCanonicalValue(detail, "part_of_the_series_qid"), seriesTitle, mediaScope);
        AddSequenceContainerOptionFromCanonicalQid(options, GetDetailCanonicalValue(detail, "part_of_series_qid"), seriesTitle, mediaScope);

        if (options.Count == 0 && !string.IsNullOrWhiteSpace(seriesTitle))
        {
            AddSequenceContainerOption(options, seriesTitle, seriesTitle, mediaScope, sourceContainerId: null);
        }

        return options;
    }

    private static void AddSequenceContainerOptionFromCanonicalQid(List<SequenceContainerOptionViewModel> options, string? rawQidValue, string? title, string mediaScope)
    {
        var parsed = ParseQidLabel(rawQidValue);
        AddSequenceContainerOption(options, parsed.Qid, FirstText(title, parsed.Label), mediaScope, sourceContainerId: parsed.Qid);
    }

    private static void AddSequenceContainerOption(
        List<SequenceContainerOptionViewModel> options,
        string? containerId,
        string? title,
        string mediaScope,
        string? sourceContainerId = null,
        IReadOnlyList<string>? equivalentContainerIds = null)
    {
        if (string.IsNullOrWhiteSpace(containerId))
        {
            return;
        }

        var normalizedContainerId = NormalizeSequenceContainerId(containerId) ?? containerId.Trim();
        var normalizedSourceContainerId = NormalizeSequenceContainerId(sourceContainerId) ?? sourceContainerId?.Trim();
        var candidate = new SequenceContainerOptionViewModel
        {
            ContainerId = normalizedContainerId,
            SourceContainerId = normalizedSourceContainerId,
            ContainerTitle = FormatSequenceContainerTitle(FirstText(title, containerId)) ?? "Series",
            MediaScope = mediaScope,
            EquivalentContainerIds = BuildSequenceContainerAliases(
                normalizedContainerId,
                normalizedSourceContainerId,
                equivalentContainerIds),
        };

        var existingIndex = options.FindIndex(option => ShouldMergeSequenceContainerOptions(option, candidate));
        if (existingIndex >= 0)
        {
            options[existingIndex] = MergeSequenceContainerOptions(options[existingIndex], candidate);
            return;
        }

        options.Add(candidate);
    }

    private static IReadOnlyList<SequenceContainerOptionViewModel> DeduplicateSequenceContainerOptions(
        IReadOnlyList<SequenceContainerOptionViewModel> options)
    {
        if (options.Count <= 1)
        {
            return options;
        }

        var distinct = new List<SequenceContainerOptionViewModel>();
        foreach (var option in options)
        {
            var existingIndex = distinct.FindIndex(existing => ShouldMergeSequenceContainerOptions(existing, option));
            if (existingIndex >= 0)
            {
                distinct[existingIndex] = MergeSequenceContainerOptions(distinct[existingIndex], option);
                continue;
            }

            distinct.Add(option);
        }

        return distinct;
    }

    private static IReadOnlyList<string> BuildSequenceContainerAliases(
        string? containerId,
        string? sourceContainerId,
        IReadOnlyList<string>? extraIds)
    {
        var aliases = new List<string>();
        AddSequenceContainerAlias(aliases, containerId);
        AddSequenceContainerAlias(aliases, sourceContainerId);
        if (extraIds is not null)
        {
            foreach (var id in extraIds)
            {
                AddSequenceContainerAlias(aliases, id);
            }
        }

        return aliases;
    }

    private static void AddSequenceContainerAlias(List<string> aliases, string? value)
    {
        var normalized = NormalizeSequenceContainerId(value) ?? value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || aliases.Any(alias => SequenceContainerIdEquals(alias, normalized)))
        {
            return;
        }

        aliases.Add(normalized);
    }

    private static bool ShouldMergeSequenceContainerOptions(
        SequenceContainerOptionViewModel existing,
        SequenceContainerOptionViewModel candidate)
    {
        if (SequenceContainerOptionMatches(existing, candidate.ContainerId)
            || SequenceContainerOptionMatches(existing, candidate.SourceContainerId)
            || candidate.EquivalentContainerIds.Any(alias => SequenceContainerOptionMatches(existing, alias)))
        {
            return true;
        }

        return false;
    }

    private static SequenceContainerOptionViewModel MergeSequenceContainerOptions(
        SequenceContainerOptionViewModel existing,
        SequenceContainerOptionViewModel candidate)
    {
        var aliases = BuildSequenceContainerAliases(
            PreferRoutableContainerId(existing.ContainerId, candidate.ContainerId),
            PreferSourceContainerId(existing, candidate),
            existing.EquivalentContainerIds.Concat(candidate.EquivalentContainerIds).ToList());

        return new SequenceContainerOptionViewModel
        {
            ContainerId = PreferRoutableContainerId(existing.ContainerId, candidate.ContainerId),
            SourceContainerId = PreferSourceContainerId(existing, candidate),
            ContainerTitle = PreferSequenceContainerTitle(existing, candidate),
            MediaScope = FirstText(existing.MediaScope, candidate.MediaScope),
            IsSelected = existing.IsSelected || candidate.IsSelected,
            IsDefault = existing.IsDefault || candidate.IsDefault,
            EquivalentContainerIds = aliases,
        };
    }

    private static string PreferRoutableContainerId(string existingId, string candidateId)
        => Guid.TryParse(existingId, out _)
            ? existingId
            : Guid.TryParse(candidateId, out _)
                ? candidateId
                : existingId;

    private static string? PreferSourceContainerId(
        SequenceContainerOptionViewModel existing,
        SequenceContainerOptionViewModel candidate)
    {
        var ids = new[]
        {
            existing.SourceContainerId,
            candidate.SourceContainerId,
            existing.ContainerId,
            candidate.ContainerId,
        };

        return ids.FirstOrDefault(IsWikidataQid)
            ?? ids.FirstOrDefault(IsProviderSequenceContainerId)
            ?? ids.FirstOrDefault(id => !Guid.TryParse(id, out _));
    }

    private static string PreferSequenceContainerTitle(
        SequenceContainerOptionViewModel existing,
        SequenceContainerOptionViewModel candidate)
    {
        if (IsWikidataQid(candidate.ContainerId)
            && IsProviderSequenceContainerId(existing.ContainerId)
            && !string.IsNullOrWhiteSpace(candidate.ContainerTitle))
        {
            return candidate.ContainerTitle;
        }

        if (Guid.TryParse(existing.ContainerId, out _)
            && IsManifestBackedSequenceContainerId(candidate.ContainerId)
            && !string.IsNullOrWhiteSpace(candidate.ContainerTitle))
        {
            return candidate.ContainerTitle;
        }

        if (string.IsNullOrWhiteSpace(existing.ContainerTitle) || IsSequenceContainerIdLike(existing.ContainerTitle))
        {
            return candidate.ContainerTitle;
        }

        return existing.ContainerTitle;
    }

    private static bool SequenceContainerOptionMatches(SequenceContainerOptionViewModel? option, string? containerId)
    {
        if (option is null || string.IsNullOrWhiteSpace(containerId))
        {
            return false;
        }

        return SequenceContainerIdEquals(option.ContainerId, containerId)
            || SequenceContainerIdEquals(option.SourceContainerId, containerId)
            || option.EquivalentContainerIds.Any(alias => SequenceContainerIdEquals(alias, containerId));
    }

    private static bool IsLocalOrProviderBackedSequenceContainer(SequenceContainerOptionViewModel option)
        => Guid.TryParse(option.ContainerId, out _)
           || Guid.TryParse(option.SourceContainerId, out _)
           || IsProviderSequenceContainerId(option.ContainerId)
           || IsProviderSequenceContainerId(option.SourceContainerId)
           || option.EquivalentContainerIds.Any(alias => Guid.TryParse(alias, out _) || IsProviderSequenceContainerId(alias));

    private static bool IsProviderBackedSequenceContainer(SequenceContainerOptionViewModel option)
        => IsProviderSequenceContainerId(option.ContainerId)
           || IsProviderSequenceContainerId(option.SourceContainerId)
           || option.EquivalentContainerIds.Any(IsProviderSequenceContainerId);

    private static bool IsComicSequenceEntity(DetailEntityType entityType)
        => entityType is DetailEntityType.ComicIssue or DetailEntityType.ComicSeries;

    private static bool IsWikidataOnlySequenceContainer(SequenceContainerOptionViewModel option)
    {
        var identities = new[] { option.ContainerId, option.SourceContainerId }
            .Concat(option.EquivalentContainerIds)
            .Where(identity => !string.IsNullOrWhiteSpace(identity))
            .ToList();
        return identities.Any(IsWikidataQid)
            && !identities.Any(identity => Guid.TryParse(identity, out _) || IsProviderSequenceContainerId(identity));
    }

    private static List<SequenceContainerOptionViewModel> PreferWikidataLinkedSequenceContainers(
        IReadOnlyList<SequenceContainerOptionViewModel> options)
    {
        var wikidataOptions = options
            .Where(option => IsWikidataQid(option.SourceContainerId) || IsWikidataQid(option.ContainerId))
            .ToList();
        if (wikidataOptions.Count == 0)
        {
            return options.ToList();
        }

        return options
            .Where(option =>
                IsWikidataQid(option.SourceContainerId)
                || IsWikidataQid(option.ContainerId)
                || wikidataOptions.Any(wikidata => ShouldMergeSequenceContainerOptions(option, wikidata)))
            .ToList();
    }

    private static bool IsTitleOnlySequenceContainerOption(SequenceContainerOptionViewModel option)
        => string.IsNullOrWhiteSpace(option.SourceContainerId)
           && !Guid.TryParse(option.ContainerId, out _)
           && !IsManifestBackedSequenceContainerId(option.ContainerId);

    private static bool IsManifestBackedSequenceContainerId(string? containerId)
        => IsWikidataQid(containerId) || IsProviderSequenceContainerId(containerId);

    private static bool IsProviderSequenceContainerId(string? containerId)
        => !string.IsNullOrWhiteSpace(containerId)
           && containerId.Contains(':', StringComparison.Ordinal)
           && !IsWikidataQid(containerId);

    private static bool IsSequenceContainerIdLike(string? value)
        => IsWikidataQid(value)
           || Guid.TryParse(value, out _)
           || IsProviderSequenceContainerId(value);

    private async Task<SequenceContainerOptionViewModel?> ResolveLocalSequenceContainerOptionAsync(Guid workId, DetailEntityType entityType, string mediaType, CancellationToken ct)
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
            current.CollectionId AS CollectionId,
            CAST((SELECT wikidata_qid FROM collections c WHERE c.id = current.CollectionId LIMIT 1) AS TEXT) AS SeriesQid,
            CAST((SELECT rule_hash FROM collections c WHERE c.id = current.CollectionId LIMIT 1) AS TEXT) AS ProviderKey
            FROM current_lineage current;
            """,
            new { workId },
            cancellationToken: ct));

        var title = StringValue(row?.SeriesTitle);
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var qid = ExtractQid(StringValue(row?.SeriesQid));
        var providerKey = StringValue(row?.ProviderKey);
        var collectionId = StringValue(row?.CollectionId);
        return new SequenceContainerOptionViewModel
        {
            ContainerId = collectionId ?? qid ?? title,
            SourceContainerId = FirstText(qid, providerKey),
            ContainerTitle = title,
            MediaScope = SeriesMediaFilter(entityType, mediaType),
            EquivalentContainerIds = BuildSequenceContainerAliases(
                collectionId,
                qid,
                string.IsNullOrWhiteSpace(providerKey) ? Array.Empty<string>() : [providerKey]),
        };
    }

    private static string? GetDetailCanonicalValue(LibraryItemDetail detail, string key)
        => detail.CanonicalValues.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

    private static DateTimeOffset? GetCanonicalLastScoredAt(LibraryItemDetail detail, string key)
        => detail.CanonicalValues.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))?.LastScoredAt;

    private static string? GetCanonicalProviderId(LibraryItemDetail detail, string key)
        => detail.CanonicalValues.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))?.WinningProviderId;

    private async Task<List<SequenceItemViewModel>> MergeSequenceManifestPlaceholdersAsync(
        IReadOnlyList<SequenceItemViewModel> items,
        string? containerId,
        string? currentWorkQid,
        Guid currentWorkId,
        DetailEntityType entityType,
        CancellationToken ct)
    {
        var normalizedContainerId = NormalizeSequenceContainerId(containerId);
        if (string.IsNullOrWhiteSpace(normalizedContainerId))
        {
            return items.ToList();
        }

        var manifestItems = await LoadManifestItemsForSequenceContainerAsync(normalizedContainerId, ct);
        var exactManifestItems = manifestItems
            .Where(item => SequenceContainerIdEquals(item.SeriesQid, normalizedContainerId))
            .ToList();
        if (exactManifestItems.Count > 0)
        {
            manifestItems = exactManifestItems;
        }

        var scopedManifestItems = manifestItems
            .Where(item => IsManifestItemInMediaScope(item, entityType))
            .ToList();
        var connectedManifestItems = BuildConnectedManifestSubset(scopedManifestItems, currentWorkQid);
        if (connectedManifestItems.Count > 1
            && IsWatchEntityType(entityType)
            && !IsParentSequenceContainer(scopedManifestItems, normalizedContainerId))
        {
            scopedManifestItems = connectedManifestItems;
        }

        if (scopedManifestItems.Count > 0)
        {
            return MergeManifestItems(items, scopedManifestItems, currentWorkQid, currentWorkId, entityType);
        }

        return await MergeLegacySequenceMemberPlaceholdersAsync(items, normalizedContainerId, entityType, ct);
    }

    private async Task<List<SequenceItemViewModel>> ApplyExactManifestPositionsAsync(
        List<SequenceItemViewModel> items,
        string? containerId,
        DetailEntityType entityType,
        CancellationToken ct)
    {
        var normalizedContainerId = NormalizeSequenceContainerId(containerId);
        if (items.Count == 0 || !IsManifestBackedSequenceContainerId(normalizedContainerId))
        {
            return items;
        }

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync(new CommandDefinition(
            """
            SELECT linked_work_id AS LinkedWorkId,
                   item_label AS ItemLabel,
                   raw_ordinal AS RawOrdinal,
                   parsed_ordinal AS ParsedOrdinal,
                   ordinal_scope_qid AS OrdinalScopeQid,
                   sort_order AS SortOrder,
                   membership_scope AS MembershipScope
            FROM series_manifest_items
            WHERE series_qid = @seriesQid
            ORDER BY COALESCE(sort_order, 999999), COALESCE(item_label, item_qid), item_qid;
            """,
            new { seriesQid = normalizedContainerId },
            cancellationToken: ct));

        var updated = items.ToList();
        foreach (var row in rows)
        {
            var rowObject = (object)row;
            var sourcePosition = ToManifestSourcePosition(rowObject, normalizedContainerId!);
            var positionSort = sourcePosition ?? DoubleValue(GetDapperValue(rowObject, "SortOrder"));
            if (!positionSort.HasValue)
            {
                continue;
            }
            var position = ToDisplayPositionNumber(sourcePosition);
            var positionLabel = sourcePosition.HasValue
                ? FirstNonBlank(
                    StringValue(GetDapperValue(rowObject, "RawOrdinal")),
                    FormatSequenceSort(sourcePosition))
                : null;
            var membershipScope = FirstNonBlank(
                StringValue(GetDapperValue(rowObject, "MembershipScope")),
                SeriesMembershipScopeNames.MainSequence)!;
            var group = ManifestScopeGroup(membershipScope);

            var linkedWorkId = StringValue(GetDapperValue(rowObject, "LinkedWorkId"));
            var normalizedTitle = NormalizeSeriesTitle(StringValue(GetDapperValue(rowObject, "ItemLabel")));
            var index = updated.FindIndex(item =>
                (!string.IsNullOrWhiteSpace(linkedWorkId)
                    && string.Equals(item.Id, linkedWorkId, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(normalizedTitle)
                    && string.Equals(NormalizeSeriesTitle(item.Title), normalizedTitle, StringComparison.OrdinalIgnoreCase)));
            if (index < 0)
            {
                continue;
            }

            var item = updated[index];
            updated[index] = new SequenceItemViewModel
            {
                Id = item.Id,
                EntityType = item.EntityType,
                Title = item.Title,
                ArtworkUrl = item.ArtworkUrl,
                PublicationDate = item.PublicationDate,
                PositionNumber = position ?? item.PositionNumber,
                PositionSort = positionSort,
                PositionLabel = sourcePosition.HasValue ? positionLabel : item.PositionLabel,
                PositionText = sourcePosition.HasValue
                    ? FormatSequencePositionText(entityType, positionLabel, position)
                    : item.PositionText,
                GroupKey = group.Key,
                GroupTitle = group.Title,
                MembershipScope = membershipScope,
                IsCurrent = item.IsCurrent,
                IsOwned = item.IsOwned,
                ProgressState = item.ProgressState,
            };
        }

        return updated;
    }

    private static double? ToManifestSourcePosition(object row, string seriesQid)
    {
        var ordinalScopeQid = StringValue(GetDapperValue(row, "OrdinalScopeQid"));
        if (!string.IsNullOrWhiteSpace(ordinalScopeQid)
            && !SequenceContainerIdEquals(ordinalScopeQid, seriesQid))
        {
            return null;
        }

        var parsedOrdinal = DoubleValue(GetDapperValue(row, "ParsedOrdinal"));
        if (parsedOrdinal.HasValue)
        {
            return parsedOrdinal;
        }

        var rawOrdinal = StringValue(GetDapperValue(row, "RawOrdinal"));
        var rawPosition = TryParseSeriesPositionSort(rawOrdinal);
        if (rawPosition.HasValue)
        {
            return rawPosition;
        }

        return null;
    }

    private static object? GetDapperValue(object row, string key)
        => row is IDictionary<string, object> values && values.TryGetValue(key, out var value)
            ? value
            : null;

    private static bool IsParentSequenceContainer(IReadOnlyList<SeriesManifestItemRecord> manifestItems, string containerId)
        => manifestItems.Any(item =>
            SequenceContainerIdEquals(item.ParentCollectionQid, containerId)
            && !SequenceContainerIdEquals(item.SeriesQid, containerId));

    private async Task<IReadOnlyList<SeriesManifestItemRecord>> LoadManifestItemsForSequenceContainerAsync(
        string containerId,
        CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var exactRows = await conn.QueryAsync<SeriesManifestItemRow>(new CommandDefinition(
            """
            SELECT id AS Id,
                   collection_id AS CollectionId,
                   series_qid AS SeriesQid,
                   item_qid AS ItemQid,
                   item_label AS ItemLabel,
                   item_description AS ItemDescription,
                   media_type AS MediaType,
                   media_kind AS MediaKind,
                   instance_of_qids_json AS InstanceOfQidsJson,
                   raw_ordinal AS RawOrdinal,
                   parsed_ordinal AS ParsedOrdinal,
                   ordinal_scope_qid AS OrdinalScopeQid,
                   sort_order AS SortOrder,
                   publication_date AS PublicationDate,
                   previous_qid AS PreviousQid,
                   next_qid AS NextQid,
                   parent_collection_qid AS ParentCollectionQid,
                   parent_collection_label AS ParentCollectionLabel,
                   is_collection AS IsCollection,
                   is_expanded_from_collection AS IsExpandedFromCollection,
                   membership_scope AS MembershipScope,
                   source_properties_json AS SourcePropertiesJson,
                   relationships_json AS RelationshipsJson,
                   order_source AS OrderSource,
                   ownership_state AS OwnershipState,
                   linked_work_id AS LinkedWorkId,
                   last_hydrated_at AS LastHydratedAt,
                   created_at AS CreatedAt,
                   updated_at AS UpdatedAt
            FROM series_manifest_items
            WHERE series_qid = @containerId
            ORDER BY COALESCE(sort_order, 999999), COALESCE(item_label, item_qid), item_qid;
            """,
            new { containerId },
            cancellationToken: ct));
        var exactItems = exactRows.Select(row => row.ToEntity()).ToList();
        if (exactItems.Count > 0)
        {
            return exactItems;
        }

        var seriesItems = await _seriesManifests.GetItemsBySeriesQidAsync(containerId, ct);
        if (seriesItems.Count > 0)
        {
            return seriesItems;
        }

        var rows = await conn.QueryAsync<SeriesManifestItemRow>(new CommandDefinition(
            """
            SELECT id AS Id,
                   collection_id AS CollectionId,
                   series_qid AS SeriesQid,
                   item_qid AS ItemQid,
                   item_label AS ItemLabel,
                   item_description AS ItemDescription,
                   media_type AS MediaType,
                   media_kind AS MediaKind,
                   instance_of_qids_json AS InstanceOfQidsJson,
                   raw_ordinal AS RawOrdinal,
                   parsed_ordinal AS ParsedOrdinal,
                   ordinal_scope_qid AS OrdinalScopeQid,
                   sort_order AS SortOrder,
                   publication_date AS PublicationDate,
                   previous_qid AS PreviousQid,
                   next_qid AS NextQid,
                   parent_collection_qid AS ParentCollectionQid,
                   parent_collection_label AS ParentCollectionLabel,
                   is_collection AS IsCollection,
                   is_expanded_from_collection AS IsExpandedFromCollection,
                   membership_scope AS MembershipScope,
                   source_properties_json AS SourcePropertiesJson,
                   relationships_json AS RelationshipsJson,
                   order_source AS OrderSource,
                   ownership_state AS OwnershipState,
                   linked_work_id AS LinkedWorkId,
                   last_hydrated_at AS LastHydratedAt,
                   created_at AS CreatedAt,
                   updated_at AS UpdatedAt
            FROM series_manifest_items
            WHERE parent_collection_qid = @containerId
            ORDER BY COALESCE(sort_order, 999999), COALESCE(item_label, item_qid), item_qid;
            """,
            new { containerId },
            cancellationToken: ct));

        return rows.Select(row => row.ToEntity()).ToList();
    }

    private static List<SeriesManifestItemRecord> BuildConnectedManifestSubset(
        IReadOnlyList<SeriesManifestItemRecord> manifestItems,
        string? currentWorkQid)
    {
        var qid = ExtractQid(currentWorkQid);
        if (string.IsNullOrWhiteSpace(qid))
        {
            return [];
        }

        var byQid = manifestItems
            .Where(item => !string.IsNullOrWhiteSpace(item.ItemQid))
            .GroupBy(item => item.ItemQid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (!byQid.ContainsKey(qid))
        {
            return [];
        }

        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { qid };
        var pending = new Queue<string>();
        pending.Enqueue(qid);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!byQid.TryGetValue(current, out var item))
            {
                continue;
            }

            foreach (var neighbor in new[] { item.PreviousQid, item.NextQid }.Select(ExtractQid).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>())
            {
                if (byQid.ContainsKey(neighbor) && connected.Add(neighbor))
                {
                    pending.Enqueue(neighbor);
                }
            }

            foreach (var inbound in manifestItems.Where(candidate =>
                string.Equals(ExtractQid(candidate.PreviousQid), current, StringComparison.OrdinalIgnoreCase)
                || string.Equals(ExtractQid(candidate.NextQid), current, StringComparison.OrdinalIgnoreCase)))
            {
                if (connected.Add(inbound.ItemQid))
                {
                    pending.Enqueue(inbound.ItemQid);
                }
            }
        }

        return manifestItems
            .Where(item => connected.Contains(item.ItemQid))
            .ToList();
    }

    private static List<SequenceItemViewModel> MergeManifestItems(
        IReadOnlyList<SequenceItemViewModel> items,
        IReadOnlyList<SeriesManifestItemRecord> manifestItems,
        string? currentWorkQid,
        Guid currentWorkId,
        DetailEntityType entityType)
    {
        var merged = items.ToList();
        var currentQid = ExtractQid(currentWorkQid);
        var ownedPositions = BuildOwnedPositionSet(merged);
        var ownedQids = merged
            .Select(item => ExtractQid(item.Id))
            .Where(qid => !string.IsNullOrWhiteSpace(qid))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ownedTitles = merged
            .Where(item => item.IsOwned)
            .Select(item => NormalizeSeriesTitle(item.Title))
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var manifestItem in manifestItems)
        {
            var sourcePosition = ManifestSourcePosition(manifestItem);
            var positionSort = ManifestOrderingSort(manifestItem);
            var position = ToDisplayPositionNumber(sourcePosition);
            var positionLabel = sourcePosition.HasValue
                ? FirstNonBlank(manifestItem.RawOrdinal, FormatSequenceSort(sourcePosition))
                : null;
            var isLinkedOwned = manifestItem.LinkedWorkId.HasValue;
            var isCurrentManifestItem = string.Equals(
                manifestItem.ItemQid,
                currentQid,
                StringComparison.OrdinalIgnoreCase);

            if ((isLinkedOwned || isCurrentManifestItem)
                && TryApplyManifestPositionToOwnedItem(
                    merged,
                    manifestItem,
                    positionSort,
                    sourcePosition,
                    currentWorkId,
                    isCurrentManifestItem))
            {
                ownedPositions = BuildOwnedPositionSet(merged);

                continue;
            }

            var normalizedManifestTitle = NormalizeSeriesTitle(manifestItem.ItemLabel);
            if (!string.IsNullOrWhiteSpace(normalizedManifestTitle)
                && ownedTitles.Contains(normalizedManifestTitle)
                && TryApplyManifestPositionToOwnedItemByTitle(merged, normalizedManifestTitle, positionSort, sourcePosition, manifestItem))
            {
                ownedPositions = BuildOwnedPositionSet(merged);

                continue;
            }

            if (!string.IsNullOrWhiteSpace(manifestItem.ItemQid) && ownedQids.Contains(manifestItem.ItemQid))
            {
                continue;
            }

            var positionKey = SequencePositionKey(sourcePosition);
            if (!string.IsNullOrWhiteSpace(positionKey) && ownedPositions.Contains(positionKey))
            {
                continue;
            }

            merged.Add(new SequenceItemViewModel
            {
                Id = $"missing-{manifestItem.ItemQid}",
                EntityType = entityType,
                Title = FirstNonBlank(manifestItem.ItemLabel, manifestItem.ItemQid) ?? "Missing from library",
                PublicationDate = manifestItem.PublicationDate,
                PositionNumber = position,
                PositionSort = positionSort,
                PositionLabel = positionLabel,
                GroupKey = ManifestScopeGroup(manifestItem.MembershipScope).Key,
                GroupTitle = ManifestScopeGroup(manifestItem.MembershipScope).Title,
                MembershipScope = manifestItem.MembershipScope,
                IsOwned = false,
                ProgressState = LibraryProgressState.Unknown,
            });

            if (!string.IsNullOrWhiteSpace(positionKey))
            {
                ownedPositions.Add(positionKey);
            }
        }

        return DeduplicateManifestMergeItems(merged)
            .OrderBy(item => item.PositionSort ?? item.PositionNumber ?? double.MaxValue)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static double? ManifestSourcePosition(SeriesManifestItemRecord item)
    {
        if (!string.IsNullOrWhiteSpace(item.OrdinalScopeQid)
            && !SequenceContainerIdEquals(item.OrdinalScopeQid, item.SeriesQid))
        {
            return null;
        }

        if (item.ParsedOrdinal.HasValue)
        {
            return item.ParsedOrdinal.Value;
        }

        return TryParseSeriesPositionSort(item.RawOrdinal);
    }

    private static double? ManifestOrderingSort(SeriesManifestItemRecord item)
        => ManifestSourcePosition(item) ?? item.SortOrder;

    private static HashSet<string> BuildOwnedPositionSet(IEnumerable<SequenceItemViewModel> items)
        => items
            .Select(item => SequencePositionKey(item.PositionSort ?? item.PositionNumber))
            .OfType<string>()
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<SequenceItemViewModel> DeduplicateManifestMergeItems(
        IEnumerable<SequenceItemViewModel> items)
    {
        return items
            .GroupBy(BuildManifestMergeKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.IsOwned)
                .ThenByDescending(item => item.IsCurrent)
                .First());
    }

    private static string BuildManifestMergeKey(SequenceItemViewModel item)
    {
        if (item.Id.StartsWith("missing-", StringComparison.OrdinalIgnoreCase))
            return $"qid:{item.Id["missing-".Length..]}";

        var title = NormalizeSeriesTitle(item.Title);
        var positionKey = SequencePositionKey(item.PositionSort ?? item.PositionNumber);
        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(positionKey))
            return $"title-position:{title}:{positionKey}";

        if (Guid.TryParse(item.Id, out var linkedWorkId))
            return $"work:{linkedWorkId:D}";

        if (!string.IsNullOrWhiteSpace(title))
            return $"title:{title}";

        return $"id:{item.Id}";
    }

    private static bool TryApplyManifestPositionToOwnedItemByTitle(
        List<SequenceItemViewModel> items,
        string normalizedTitle,
        double? positionSort,
        double? sourcePosition,
        SeriesManifestItemRecord manifestItem)
    {
        var index = items.FindIndex(item =>
            item.IsOwned
            && string.Equals(NormalizeSeriesTitle(item.Title), normalizedTitle, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        var item = items[index];
        var position = ToDisplayPositionNumber(sourcePosition);
        var manifestPositionLabel = sourcePosition.HasValue
            ? FirstNonBlank(manifestItem.RawOrdinal, FormatSequenceSort(sourcePosition))
            : null;
        var group = ManifestScopeGroup(manifestItem.MembershipScope);

        items[index] = new SequenceItemViewModel
        {
            Id = item.Id,
            EntityType = item.EntityType,
            Title = item.Title,
            ArtworkUrl = item.ArtworkUrl,
            PublicationDate = FirstNonBlank(manifestItem.PublicationDate, item.PublicationDate),
            PositionNumber = position ?? item.PositionNumber,
            PositionSort = positionSort ?? item.PositionSort,
            PositionLabel = manifestPositionLabel ?? item.PositionLabel,
            PositionText = sourcePosition.HasValue ? null : item.PositionText,
            GroupKey = group.Key,
            GroupTitle = group.Title,
            MembershipScope = manifestItem.MembershipScope,
            IsCurrent = item.IsCurrent,
            IsOwned = item.IsOwned,
            ProgressState = item.ProgressState,
        };
        return true;
    }

    private static bool TryApplyManifestPositionToOwnedItem(
        List<SequenceItemViewModel> items,
        SeriesManifestItemRecord manifestItem,
        double? positionSort,
        double? sourcePosition,
        Guid currentWorkId,
        bool allowCurrentWorkFallback)
    {
        var index = items.FindIndex(item =>
            (manifestItem.LinkedWorkId.HasValue && string.Equals(item.Id, manifestItem.LinkedWorkId.Value.ToString("D"), StringComparison.OrdinalIgnoreCase))
            || (allowCurrentWorkFallback
                && item.IsCurrent
                && currentWorkId != Guid.Empty
                && string.Equals(item.Id, currentWorkId.ToString("D"), StringComparison.OrdinalIgnoreCase)));
        if (index < 0)
        {
            return false;
        }

        var item = items[index];
        var position = ToDisplayPositionNumber(sourcePosition);
        var manifestPositionLabel = sourcePosition.HasValue
            ? FirstNonBlank(manifestItem.RawOrdinal, FormatSequenceSort(sourcePosition))
            : null;
        var group = ManifestScopeGroup(manifestItem.MembershipScope);

        items[index] = new SequenceItemViewModel
        {
            Id = item.Id,
            EntityType = item.EntityType,
            Title = item.Title,
            ArtworkUrl = item.ArtworkUrl,
            PublicationDate = FirstNonBlank(manifestItem.PublicationDate, item.PublicationDate),
            PositionNumber = position ?? item.PositionNumber,
            PositionSort = positionSort ?? item.PositionSort,
            PositionLabel = manifestPositionLabel ?? item.PositionLabel,
            PositionText = sourcePosition.HasValue ? null : item.PositionText,
            GroupKey = group.Key,
            GroupTitle = group.Title,
            MembershipScope = manifestItem.MembershipScope,
            IsCurrent = item.IsCurrent,
            IsOwned = item.IsOwned,
            ProgressState = item.ProgressState,
        };
        return true;
    }

    private static (string Key, string Title) ManifestScopeGroup(string? membershipScope)
        => membershipScope switch
        {
            SeriesMembershipScopeNames.Supplementary => ("supplementary", "Short Fiction & Extras"),
            SeriesMembershipScopeNames.CollectedContent => ("collected-content", "Collected Content"),
            SeriesMembershipScopeNames.BroaderContext => ("broader-context", "Broader Context"),
            SeriesMembershipScopeNames.Unpositioned => ("unpositioned", "Unnumbered & Extras"),
            _ => ("main-sequence", "Main Series"),
        };

    private static bool IsManifestItemInMediaScope(SeriesManifestItemRecord item, DetailEntityType entityType)
    {
        if (item.IsCollection
            && !(string.Equals(item.MembershipScope, SeriesMembershipScopeNames.MainSequence, StringComparison.OrdinalIgnoreCase)
                && ManifestSourcePosition(item).HasValue))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(item.MediaKind)
            && !string.Equals(item.MediaKind, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return entityType switch
            {
                DetailEntityType.Movie or DetailEntityType.MovieSeries => item.MediaKind.Equals("Film", StringComparison.OrdinalIgnoreCase),
                DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode => item.MediaKind.Equals("Television", StringComparison.OrdinalIgnoreCase),
                DetailEntityType.ComicIssue or DetailEntityType.ComicSeries => item.MediaKind.Equals("Comic", StringComparison.OrdinalIgnoreCase),
                DetailEntityType.Audiobook => item.MediaKind.Equals("Audiobook", StringComparison.OrdinalIgnoreCase)
                    || item.MediaKind.Equals("LiteraryWork", StringComparison.OrdinalIgnoreCase),
                DetailEntityType.Book or DetailEntityType.BookSeries or DetailEntityType.Work => item.MediaKind.Equals("LiteraryWork", StringComparison.OrdinalIgnoreCase),
                DetailEntityType.MusicAlbum or DetailEntityType.MusicTrack or DetailEntityType.MusicArtist => item.MediaKind.Equals("Music", StringComparison.OrdinalIgnoreCase),
                _ => !item.MediaKind.Equals("StageWork", StringComparison.OrdinalIgnoreCase),
            };
        }

        var text = string.Join(' ', new[]
        {
            item.MediaType,
            item.ItemDescription,
            item.ParentCollectionLabel,
            item.SourcePropertiesJson,
            item.RelationshipsJson,
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

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

    private static bool IsWatchEntityType(DetailEntityType entityType)
        => entityType is DetailEntityType.Movie
            or DetailEntityType.MovieSeries
            or DetailEntityType.TvShow
            or DetailEntityType.TvSeason
            or DetailEntityType.TvEpisode;

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private async Task<List<SequenceItemViewModel>> MergeLegacySequenceMemberPlaceholdersAsync(
        IReadOnlyList<SequenceItemViewModel> items,
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
        var ownedPositions = BuildOwnedPositionSet(merged);

        foreach (var member in rawMembers)
        {
            var positionSort = TryParseSeriesPositionSort(StringValue(member.Position));
            var position = ToDisplayPositionNumber(positionSort);
            var positionKey = SequencePositionKey(positionSort);
            if (string.IsNullOrWhiteSpace(positionKey) || ownedPositions.Contains(positionKey))
            {
                continue;
            }

            merged.Add(new SequenceItemViewModel
            {
                Id = $"missing-{seriesQid}-{positionKey}",
                EntityType = entityType,
                Title = FirstNonBlank(StringValue(member.WorkLabel), $"Book {FormatSequenceSort(positionSort)}"),
                PositionNumber = position,
                PositionSort = positionSort,
                PositionLabel = FormatSequenceSort(positionSort),
                IsOwned = false,
                ProgressState = LibraryProgressState.Unknown,
            });
        }

        return merged
            .OrderBy(item => item.PositionSort ?? item.PositionNumber ?? double.MaxValue)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<SequenceItemViewModel> AddMissingSequencePlaceholders(
        IReadOnlyList<SequenceItemViewModel> items,
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
        {
            return unnumbered;
        }

        var max = numbered.Keys.Max();
        var filled = new List<SequenceItemViewModel>(max);
        for (var position = 1; position <= max; position++)
        {
            if (numbered.TryGetValue(position, out var existing))
            {
                filled.Add(existing);
                continue;
            }

            filled.Add(new SequenceItemViewModel
            {
                Id = $"missing-{position}",
                EntityType = entityType,
                Title = "Missing from library",
                PositionNumber = position,
                PositionSort = position,
                PositionLabel = position.ToString(),
                IsOwned = false,
                ProgressState = LibraryProgressState.Unknown,
            });
        }

        filled.AddRange(unnumbered);
        return filled;
    }

    private static List<SequenceItemViewModel> SortSequenceItems(IEnumerable<SequenceItemViewModel> items)
        => items
            .OrderBy(item => SequenceScopeSort(item.MembershipScope))
            .ThenBy(item => TryParseSeriesPosition(item.GroupKey?.Replace("season-", string.Empty, StringComparison.OrdinalIgnoreCase)) ?? int.MaxValue)
            .ThenBy(item => item.PositionSort ?? item.PositionNumber ?? double.MaxValue)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static int SequenceScopeSort(string? membershipScope)
        => membershipScope switch
        {
            SeriesMembershipScopeNames.Supplementary => 1,
            SeriesMembershipScopeNames.CollectedContent => 2,
            SeriesMembershipScopeNames.BroaderContext => 3,
            SeriesMembershipScopeNames.Unpositioned => 1,
            _ => 0,
        };

    private static string? NormalizeSeriesTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            .ToArray());

        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? NormalizeSequenceContainerTitleForOptionMatch(string? value)
    {
        var normalized = NormalizeSeriesTitle(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 1 && string.Equals(words[0], "the", StringComparison.OrdinalIgnoreCase))
        {
            words.RemoveAt(0);
        }

        while (words.Count > 1 && IsGenericSequenceContainerWord(words[^1]))
        {
            words.RemoveAt(words.Count - 1);
        }

        return words.Count == 0 ? normalized : string.Join(' ', words);
    }

    private static bool IsGenericSequenceContainerWord(string value)
        => string.Equals(value, "collection", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "series", StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<CollectionWorkSummary>> LoadCollectionWorksAsync(Guid collectionId, Guid? rootWorkId, CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var rawRows = await conn.QueryAsync(new CommandDefinition(
            """
            SELECT w.id AS Id,
                   ma.id AS AssetId,
                   CAST(w.media_type AS TEXT) AS MediaType,
                   w.ordinal AS Ordinal,
                   CAST(w.display_overrides_json AS TEXT) AS WorkDisplayOverridesJson,
                   CAST(COALESCE(
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key = 'issue_title' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'issue_title' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key = 'episode_title' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'episode_title' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key = 'title' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'title' LIMIT 1),
                       'Untitled') AS TEXT) AS Title,
                   CAST(COALESCE(
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'issue_description' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key = 'issue_description' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'episode_description' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key = 'episode_description' LIMIT 1),
                       (SELECT NULLIF(CAST(claim_value AS TEXT), '') FROM metadata_claims WHERE entity_id = w.id AND claim_key IN ('issue_description', 'issue_overview') AND NULLIF(CAST(claim_value AS TEXT), '') IS NOT NULL ORDER BY confidence DESC, claimed_at DESC LIMIT 1),
                       (SELECT NULLIF(CAST(claim_value AS TEXT), '') FROM metadata_claims WHERE entity_id = ma.id AND claim_key IN ('issue_description', 'issue_overview') AND NULLIF(CAST(claim_value AS TEXT), '') IS NOT NULL ORDER BY confidence DESC, claimed_at DESC LIMIT 1),
                       (SELECT NULLIF(CAST(claim_value AS TEXT), '') FROM metadata_claims WHERE entity_id = w.id AND claim_key IN ('episode_description', 'episode_overview') AND NULLIF(CAST(claim_value AS TEXT), '') IS NOT NULL ORDER BY confidence DESC, claimed_at DESC LIMIT 1),
                       (SELECT NULLIF(CAST(claim_value AS TEXT), '') FROM metadata_claims WHERE entity_id = ma.id AND claim_key IN ('episode_description', 'episode_overview') AND NULLIF(CAST(claim_value AS TEXT), '') IS NOT NULL ORDER BY confidence DESC, claimed_at DESC LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key = 'description' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key = 'overview' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'description' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'overview' LIMIT 1)) AS TEXT) AS Description,
                   CAST(COALESCE(
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key = 'season_number' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'season_number' LIMIT 1),
                       '') AS TEXT) AS Season,
                   CAST(COALESCE(
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key = 'episode_number' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'episode_number' LIMIT 1),
                       '') AS TEXT) AS Episode,
                   CAST(COALESCE(
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key = 'track_number' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'track_number' LIMIT 1),
                       '') AS TEXT) AS TrackNumber,
                   CAST(COALESCE(
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key = 'runtime' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key = 'duration' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'runtime' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'duration' LIMIT 1)) AS TEXT) AS Duration,
                   CAST(COALESCE(
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key IN ('year', 'release_year') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key IN ('year', 'release_year') LIMIT 1)) AS TEXT) AS Year,
                   CAST(COALESCE(
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key IN ('artist', 'album_artist') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key IN ('artist', 'album_artist') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = COALESCE(gp.id, p.id, w.id) AND cv.key IN ('artist', 'album_artist') LIMIT 1)) AS TEXT) AS Artist,
                   CAST(COALESCE(
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key IN ('explicit', 'is_explicit') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key IN ('explicit', 'is_explicit') LIMIT 1)) AS TEXT) AS Explicit,
                   CAST(COALESCE(
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key IN ('quality', 'audio_quality') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key IN ('quality', 'audio_quality') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = COALESCE(gp.id, p.id, w.id) AND cv.key IN ('quality', 'audio_quality') LIMIT 1)) AS TEXT) AS Quality,
                   CAST(COALESCE(
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key IN ('cover_url', 'cover') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key IN ('poster_url', 'poster') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key IN ('cover_url', 'cover') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key IN ('poster_url', 'poster') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = COALESCE(gp.id, p.id, w.id) AND cv.key IN ('cover_url', 'cover') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = COALESCE(gp.id, p.id, w.id) AND cv.key IN ('poster_url', 'poster') LIMIT 1)) AS TEXT) AS ArtworkUrl,
                   CAST(COALESCE(
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key IN ('episode_still_url', 'episode_still') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key IN ('episode_still_url', 'episode_still') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key IN ('background_url', 'background') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key IN ('background_url', 'background') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key IN ('hero_url', 'hero') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key IN ('hero_url', 'hero') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key IN ('banner_url', 'banner') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key IN ('banner_url', 'banner') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = COALESCE(gp.id, p.id, w.id) AND cv.key IN ('background_url', 'background') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = COALESCE(gp.id, p.id, w.id) AND cv.key IN ('hero_url', 'hero') LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = COALESCE(gp.id, p.id, w.id) AND cv.key IN ('banner_url', 'banner') LIMIT 1)) AS TEXT) AS BackgroundUrl,
                   CAST(COALESCE(
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key = 'cover_state' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'cover_state' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = COALESCE(gp.id, p.id, w.id) AND cv.key = 'cover_state' LIMIT 1)) AS TEXT) AS CoverState,
                   CAST(COALESCE(
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key = 'background_state' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'background_state' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key = 'hero_state' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'hero_state' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = ma.id AND cv.key = 'banner_state' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = w.id AND cv.key = 'banner_state' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = COALESCE(gp.id, p.id, w.id) AND cv.key = 'background_state' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = COALESCE(gp.id, p.id, w.id) AND cv.key = 'hero_state' LIMIT 1),
                       (SELECT NULLIF(CAST(cv.value AS TEXT), '') FROM canonical_values cv WHERE cv.entity_id = COALESCE(gp.id, p.id, w.id) AND cv.key = 'banner_state' LIMIT 1)) AS TEXT) AS BackgroundState,
                   MAX(us.progress_pct) AS ProgressPercent,
                   CASE WHEN MAX(ma.id) IS NULL THEN 0 ELSE 1 END AS HasAsset,
                   CAST(COALESCE(w.ownership, 'Owned') AS TEXT) AS Ownership,
                   COALESCE(w.is_catalog_only, 0) AS IsCatalogOnly
            FROM works w
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            LEFT JOIN collection_items ci ON ci.work_id = w.id AND ci.collection_id = @collectionId
            LEFT JOIN editions e ON e.work_id = w.id
            LEFT JOIN media_assets ma ON ma.edition_id = e.id
            LEFT JOIN user_states us ON us.asset_id = ma.id
                                    AND us.user_id = @defaultOwnerUserId
            WHERE w.collection_id = @collectionId
               OR ci.collection_id = @collectionId
               OR (
                   @rootWorkId IS NOT NULL
                   AND (
                       p.parent_work_id = @rootWorkId
                       OR (
                           w.parent_work_id = @rootWorkId
                           AND EXISTS (
                               SELECT 1
                               FROM canonical_values child_marker
                               WHERE child_marker.entity_id = w.id
                                 AND child_marker.key IN ('episode_number', 'track_number')
                                 AND NULLIF(CAST(child_marker.value AS TEXT), '') IS NOT NULL
                           )
                       )
                   )
               )
            GROUP BY w.id
            ORDER BY COALESCE(ci.sort_order, 9999), CAST(NULLIF(Season, '') AS INTEGER), CAST(NULLIF(Episode, '') AS INTEGER), CAST(NULLIF(TrackNumber, '') AS INTEGER), COALESCE(w.ordinal, 9999), Title;
            """,
            new
            {
                collectionId = GuidSql.ToBlob(collectionId),
                rootWorkId = rootWorkId.HasValue ? GuidSql.ToBlob(rootWorkId.Value) : null,
                defaultOwnerUserId = GuidSql.ToBlob(DefaultOwnerUserId),
            },
            cancellationToken: ct));
        return rawRows.Select(row => new CollectionWorkSummary(
            StringValue(row.Id) ?? string.Empty,
            StringValue(row.MediaType) ?? string.Empty,
            IntValue(row.Ordinal),
            FirstNonBlank(
                ResolveDisplayTitleOverride(
                    (string?)StringValue(row.WorkDisplayOverridesJson),
                    InferMediaItemEntityType(StringValue(row.MediaType) ?? string.Empty, StringValue(row.Episode))),
                StringValue(row.Title),
                "Untitled"),
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
            IsTruthy(StringValue(row.HasAsset)),
            StringValue(row.Ownership),
            IsTruthy(StringValue(row.IsCatalogOnly)),
            ResolveCollectionArtworkUrl(StringValue(row.ArtworkUrl), StringValue(row.AssetId), "cover", StringValue(row.CoverState)),
            ResolveCollectionArtworkUrl(StringValue(row.BackgroundUrl), StringValue(row.AssetId), "background", StringValue(row.BackgroundState)))).ToList();
    }

    private async Task<Dictionary<string, string>> LoadWorkDisplayOverridesAsync(Guid workId, CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var json = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT display_overrides_json FROM works WHERE id = @workId LIMIT 1;",
            new { workId },
            cancellationToken: ct));

        return ParseDisplayOverrides(json);
    }

    private static Dictionary<string, string> ParseDisplayOverrides(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return parsed is null
                ? new(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? ResolveDisplayTitleOverride(string? json, DetailEntityType entityType) =>
        ResolveDisplayTitleOverride(ParseDisplayOverrides(json), entityType);

    private static string? ResolveDisplayTitleOverride(IReadOnlyDictionary<string, string> overrides, DetailEntityType entityType)
    {
        if (overrides.Count == 0)
        {
            return null;
        }

        var keys = entityType switch
        {
            DetailEntityType.TvShow or DetailEntityType.TvSeason => ["show_name", "title", "display_title"],
            DetailEntityType.TvEpisode => ["episode_title", "title", "display_title"],
            DetailEntityType.ComicIssue => ["issue_title", "title", "display_title"],
            DetailEntityType.MusicAlbum => ["album", "title", "display_title"],
            DetailEntityType.MusicTrack => ["title", "display_title"],
            DetailEntityType.BookSeries or DetailEntityType.ComicSeries => ["series", "title", "display_title"],
            _ => new[] { "title", "display_title" },
        };

        foreach (var key in keys)
        {
            if (overrides.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private async Task<Dictionary<string, string>> LoadCanonicalMapAsync(Guid entityId, CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<CanonicalPair>(new CommandDefinition(
            "SELECT key AS Key, value AS Value FROM canonical_values WHERE entity_id = @entityId;",
            new { entityId = GuidSql.ToBlob(entityId) },
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
        var rootValue = await conn.ExecuteScalarAsync<object?>(new CommandDefinition(
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
                collectionId = GuidSql.ToBlob(collectionId),
                requireRootWithChildren = requireRootWithChildren ? 1 : 0,
            },
            cancellationToken: ct));

        var rootId = StringValue(rootValue);
        return Guid.TryParse(rootId, out var rootGuid) ? rootGuid : null;
    }

    private static Dictionary<string, string> MergeCanonicalMaps(
        IReadOnlyDictionary<string, string> primary,
        IReadOnlyDictionary<string, string> fallback)
    {
        var merged = new Dictionary<string, string>(fallback, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in primary)
        {
            merged[key] = value;
        }

        return merged;
    }

    private static DescriptionSelection ResolveLongDescription(
        LibraryItemDetail detail,
        IReadOnlyDictionary<string, string> canonicalValues,
        DetailEntityType entityType)
    {
        if (entityType == DetailEntityType.TvEpisode)
        {
            return FirstSelectedText(
                (MetadataFieldConstants.EpisodeDescription, GetValue(canonicalValues, MetadataFieldConstants.EpisodeDescription)),
                ("episode_overview", GetValue(canonicalValues, "episode_overview")),
                (MetadataFieldConstants.Description, detail.Description),
                (MetadataFieldConstants.Description, GetValue(canonicalValues, MetadataFieldConstants.Description)),
                ("overview", GetValue(canonicalValues, "overview")));
        }

        if (entityType == DetailEntityType.ComicIssue)
        {
            var issueDescription = FirstSelectedText(
                (MetadataFieldConstants.IssueDescription, GetValue(canonicalValues, MetadataFieldConstants.IssueDescription)),
                ("issue_overview", GetValue(canonicalValues, "issue_overview")));
            if (!string.IsNullOrWhiteSpace(issueDescription.Text))
            {
                return issueDescription;
            }

            return new DescriptionSelection(
                BuildComicIssueFallbackDescription(detail, canonicalValues),
                SourceKey: null,
                IsGeneratedFallback: true);
        }

        return FirstSelectedText(
            (MetadataFieldConstants.Description, GetValue(canonicalValues, MetadataFieldConstants.Description)),
            ("overview", GetValue(canonicalValues, "overview")),
            ("plot_summary", GetValue(canonicalValues, "plot_summary")),
            (MetadataFieldConstants.Description, detail.Description));
    }

    private static DescriptionSelection FirstSelectedText(params (string Key, string? Text)[] values)
    {
        foreach (var (key, text) in values)
        {
            var normalized = FirstText(text);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return new DescriptionSelection(normalized, key, IsGeneratedFallback: false);
            }
        }

        return new DescriptionSelection(null, null, IsGeneratedFallback: false);
    }

    private static string? BuildComicIssueFallbackDescription(
        LibraryItemDetail detail,
        IReadOnlyDictionary<string, string> values)
    {
        var issueNumber = FirstNonBlank(
            GetValue(values, MetadataFieldConstants.IssueNumber),
            detail.SeriesPosition,
            GetValue(values, MetadataFieldConstants.SeriesPosition));
        var series = FirstNonBlank(detail.Series, GetValue(values, MetadataFieldConstants.Series));

        if (!string.IsNullOrWhiteSpace(issueNumber) && !string.IsNullOrWhiteSpace(series))
        {
            return $"{FormatIssue(issueNumber)} in {series}";
        }

        return FirstNonBlank(FormatIssue(issueNumber), string.IsNullOrWhiteSpace(series) ? null : $"Issue in {series}");
    }

    private static string? BuildHeroSummary(IReadOnlyDictionary<string, string> canonicalValues)
        => NormalizeHeroSummary(GetValue(canonicalValues, "tldr"));

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
                AssetId,
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
            new { workId },
            cancellationToken: ct));

        if (row is null)
        {
            return new WorkArtworkFallback();
        }

        var assetIdValue = StringValue(row.AssetId);
        if (!Guid.TryParse(assetIdValue, out Guid assetId))
        {
            return new WorkArtworkFallback();
        }

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
        var backdropTop = !string.IsNullOrWhiteSpace(backdropUrl) ? GetValue(values, "background_primary_hex") : null;
        var backdropMiddle = !string.IsNullOrWhiteSpace(backdropUrl) ? GetValue(values, "background_secondary_hex") : null;
        var backdropBottom = !string.IsNullOrWhiteSpace(backdropUrl) ? GetValue(values, "background_accent_hex") : null;
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
            BackdropLeftTopColor = backdropTop,
            BackdropLeftMiddleColor = backdropMiddle,
            BackdropLeftBottomColor = backdropBottom,
            HeroArtwork = heroArtwork,
            PresentationMode = mode,
            Source = ResolveArtworkSource(artworkSource),
        };
    }

    private static DetailFactsViewModel BuildWorkFacts(
        LibraryItemDetail detail,
        DetailEntityType entityType,
        IReadOnlyDictionary<string, string> canonicalValues,
        IReadOnlyList<CreditGroupViewModel> contributorGroups)
    {
        var identifiers = BuildIdentifierFacts(canonicalValues, detail.BridgeIds, detail.WikidataQid);
        var artists = MergeNames(
            CreditNames(contributorGroups, CreditGroupType.PrimaryArtists),
            SplitMetadataValues(detail.Artist),
            SplitMetadataValues(GetValue(canonicalValues, MetadataFieldConstants.Artist)));
        var albumArtists = MergeNames(
            SplitMetadataValues(GetValue(canonicalValues, "album_artist")),
            SplitMetadataValues(GetValue(canonicalValues, MetadataFieldConstants.Author)));

        return new DetailFactsViewModel
        {
            MediaKind = FormatEntityType(entityType),
            Year = FirstNonBlank(detail.Year, GetValue(canonicalValues, MetadataFieldConstants.Year), ReleaseYear(GetValue(canonicalValues, "release_date"))),
            ReleaseDate = FirstNonBlank(detail.ReleaseDate, GetValue(canonicalValues, "release_date"), GetValue(canonicalValues, "first_air_date")),
            Rating = FirstNonBlank(FormatRating(detail.Rating), detail.Rating, GetValue(canonicalValues, MetadataFieldConstants.Rating)),
            ContentRating = FirstNonBlank(GetValue(canonicalValues, "content_rating"), GetValue(canonicalValues, "certification")),
            Runtime = FormatRuntime(detail.Runtime),
            Duration = FirstNonBlank(FormatRuntime(detail.Runtime), FormatRuntime(GetValue(canonicalValues, MetadataFieldConstants.DurationField)), GetValue(canonicalValues, MetadataFieldConstants.DurationField)),
            Language = FirstNonBlank(detail.Language, GetValue(canonicalValues, MetadataFieldConstants.Language), GetValue(canonicalValues, MetadataFieldConstants.OriginalLanguage)),
            Genres = SplitMetadataValues(FirstNonBlank(detail.Genre, GetValue(canonicalValues, MetadataFieldConstants.Genre))).ToList(),
            Identifiers = identifiers,

            Authors = MergeNames(CreditNames(contributorGroups, CreditGroupType.Authors), SplitMetadataValues(detail.Author)),
            Artists = artists,
            AlbumArtists = albumArtists,
            Actors = MergeNames(CreditNames(contributorGroups, CreditGroupType.Cast), SplitMetadataValues(detail.Cast)),
            Directors = MergeNames(CreditNames(contributorGroups, CreditGroupType.Directors), SplitMetadataValues(detail.Director)),
            Writers = MergeNames(CreditNames(contributorGroups, CreditGroupType.Writers), SplitMetadataValues(detail.Writer)),
            Composers = MergeNames(CreditNames(contributorGroups, CreditGroupType.MusicCredits), SplitMetadataValues(detail.Composer)),
            Narrators = MergeNames(CreditNames(contributorGroups, CreditGroupType.Narrators), SplitMetadataValues(detail.Narrator)),
            Illustrators = MergeNames(CreditNames(contributorGroups, CreditGroupType.Illustrators), SplitMetadataValues(detail.Illustrator)),
            Producers = MergeNames(CreditNames(contributorGroups, CreditGroupType.Producers), SplitMetadataValues(GetValue(canonicalValues, "producer"))),

            ShowName = FirstNonBlank(detail.ShowName, GetValue(canonicalValues, MetadataFieldConstants.ShowName), GetValue(canonicalValues, MetadataFieldConstants.Series)),
            SeasonNumber = FirstNonBlank(detail.SeasonNumber, GetValue(canonicalValues, MetadataFieldConstants.SeasonNumber), GetValue(canonicalValues, "season")),
            EpisodeNumber = FirstNonBlank(detail.EpisodeNumber, GetValue(canonicalValues, MetadataFieldConstants.EpisodeNumber), GetValue(canonicalValues, "episode")),
            EpisodeTitle = FirstNonBlank(detail.EpisodeTitle, GetValue(canonicalValues, MetadataFieldConstants.EpisodeTitle)),
            Network = FirstNonBlank(GetValue(canonicalValues, MetadataFieldConstants.Network), GetValue(canonicalValues, "broadcaster")),
            SeasonCount = GetValue(canonicalValues, MetadataFieldConstants.SeasonCount),
            EpisodeCount = GetValue(canonicalValues, MetadataFieldConstants.EpisodeCount),

            Album = FirstNonBlank(GetValue(canonicalValues, MetadataFieldConstants.Album), detail.Series),
            AlbumArtist = FirstNonBlank(albumArtists.FirstOrDefault(), detail.Artist),
            TrackNumber = FirstNonBlank(GetValue(canonicalValues, MetadataFieldConstants.TrackNumber), detail.SeriesPosition),
            TrackCount = GetValue(canonicalValues, MetadataFieldConstants.TrackCount),
            DiscNumber = GetValue(canonicalValues, MetadataFieldConstants.DiscNumber),
            DiscCount = GetValue(canonicalValues, MetadataFieldConstants.DiscCount),
            Isrc = GetValue(canonicalValues, "isrc"),
            Label = FirstNonBlank(GetValue(canonicalValues, "label"), GetValue(canonicalValues, "record_label")),
            IsExplicit = ParseNullableBool(GetValue(canonicalValues, "explicit"), GetValue(canonicalValues, "is_explicit")),

            Series = FirstNonBlank(detail.Series, GetValue(canonicalValues, MetadataFieldConstants.Series)),
            SeriesPosition = FirstNonBlank(detail.SeriesPosition, GetValue(canonicalValues, MetadataFieldConstants.SeriesPosition)),
            IssueNumber = FirstNonBlank(GetValue(canonicalValues, MetadataFieldConstants.IssueNumber), detail.SeriesPosition),
            IssueTitle = GetValue(canonicalValues, MetadataFieldConstants.IssueTitle),
            Publisher = GetValue(canonicalValues, MetadataFieldConstants.PublisherField),
            PageCount = GetValue(canonicalValues, MetadataFieldConstants.PageCount),
        };
    }

    private static DetailFactsViewModel BuildCollectionFacts(
        DetailEntityType entityType,
        IReadOnlyList<CollectionWorkSummary> works,
        IReadOnlyDictionary<string, string> canonicalValues,
        IReadOnlyList<CreditGroupViewModel> contributorGroups,
        string? wikidataQid)
    {
        var identifiers = BuildIdentifierFacts(canonicalValues, null, wikidataQid);
        var genres = SplitMetadataValues(GetValue(canonicalValues, MetadataFieldConstants.Genre)).ToList();
        var years = works.Select(work => work.Year).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        var artists = MergeNames(
            CreditNames(contributorGroups, CreditGroupType.PrimaryArtists),
            works.Select(work => work.Artist).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>(),
            SplitMetadataValues(GetValue(canonicalValues, MetadataFieldConstants.Artist)));
        var albumArtists = MergeNames(
            SplitMetadataValues(GetValue(canonicalValues, "album_artist")),
            SplitMetadataValues(GetValue(canonicalValues, MetadataFieldConstants.Author)),
            artists.Take(1));
        var seasonCount = FirstNonBlank(
            GetValue(canonicalValues, MetadataFieldConstants.SeasonCount),
            entityType is DetailEntityType.TvShow
                ? works.Select(work => work.Season).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(CultureInfo.InvariantCulture)
                : null);

        return new DetailFactsViewModel
        {
            MediaKind = FormatEntityType(entityType),
            Year = FirstNonBlank(GetValue(canonicalValues, MetadataFieldConstants.Year), GetValue(canonicalValues, "release_year"), years.FirstOrDefault()),
            ReleaseDate = FirstNonBlank(GetValue(canonicalValues, "release_date"), GetValue(canonicalValues, "first_air_date")),
            Rating = FirstNonBlank(FormatRating(GetValue(canonicalValues, MetadataFieldConstants.Rating)), GetValue(canonicalValues, MetadataFieldConstants.Rating)),
            ContentRating = FirstNonBlank(GetValue(canonicalValues, "content_rating"), GetValue(canonicalValues, "certification")),
            Runtime = FormatRuntime(GetValue(canonicalValues, MetadataFieldConstants.Runtime)),
            Duration = FirstNonBlank(FormatRuntime(GetValue(canonicalValues, MetadataFieldConstants.DurationField)), FormatCollectionDuration(works)),
            Language = FirstNonBlank(GetValue(canonicalValues, MetadataFieldConstants.Language), GetValue(canonicalValues, MetadataFieldConstants.OriginalLanguage)),
            Genres = genres,
            Identifiers = identifiers,

            Authors = MergeNames(CreditNames(contributorGroups, CreditGroupType.Authors), SplitMetadataValues(GetValue(canonicalValues, MetadataFieldConstants.Author))),
            Artists = artists,
            AlbumArtists = albumArtists,
            Actors = CreditNames(contributorGroups, CreditGroupType.Cast),
            Directors = CreditNames(contributorGroups, CreditGroupType.Directors),
            Writers = CreditNames(contributorGroups, CreditGroupType.Writers),
            Composers = CreditNames(contributorGroups, CreditGroupType.MusicCredits),
            Narrators = CreditNames(contributorGroups, CreditGroupType.Narrators),
            Illustrators = CreditNames(contributorGroups, CreditGroupType.Illustrators),
            Producers = MergeNames(CreditNames(contributorGroups, CreditGroupType.Producers), SplitMetadataValues(GetValue(canonicalValues, "producer"))),

            ShowName = FirstNonBlank(GetValue(canonicalValues, MetadataFieldConstants.ShowName), GetValue(canonicalValues, MetadataFieldConstants.Title)),
            Network = FirstNonBlank(GetValue(canonicalValues, MetadataFieldConstants.Network), GetValue(canonicalValues, "broadcaster")),
            SeasonCount = seasonCount,
            EpisodeCount = FirstNonBlank(GetValue(canonicalValues, MetadataFieldConstants.EpisodeCount), entityType is DetailEntityType.TvShow ? works.Count.ToString(CultureInfo.InvariantCulture) : null),

            Album = FirstNonBlank(GetValue(canonicalValues, MetadataFieldConstants.Album), GetValue(canonicalValues, MetadataFieldConstants.Title)),
            AlbumArtist = albumArtists.FirstOrDefault(),
            TrackCount = FirstNonBlank(GetValue(canonicalValues, MetadataFieldConstants.TrackCount), entityType is DetailEntityType.MusicAlbum ? works.Count.ToString(CultureInfo.InvariantCulture) : null),
            DiscCount = GetValue(canonicalValues, MetadataFieldConstants.DiscCount),
            Isrc = GetValue(canonicalValues, "isrc"),
            Label = FirstNonBlank(GetValue(canonicalValues, "label"), GetValue(canonicalValues, "record_label")),
            IsExplicit = ParseNullableBool(GetValue(canonicalValues, "explicit"), GetValue(canonicalValues, "is_explicit")),

            Series = FirstNonBlank(GetValue(canonicalValues, MetadataFieldConstants.Series), GetValue(canonicalValues, MetadataFieldConstants.Title)),
            SeriesPosition = GetValue(canonicalValues, MetadataFieldConstants.SeriesPosition),
            Publisher = GetValue(canonicalValues, MetadataFieldConstants.PublisherField),
            PageCount = GetValue(canonicalValues, MetadataFieldConstants.PageCount),
        };
    }

    private static DetailFactsViewModel BuildPersonFacts(Person person, IReadOnlyList<string> displayRoles)
        => new()
        {
            MediaKind = person.IsGroup ? "Group" : "Person",
            Identifiers = BuildIdentifierFacts(new Dictionary<string, string>(), null, person.WikidataQid),
            Artists = displayRoles.Any(role => role.Contains("Artist", StringComparison.OrdinalIgnoreCase) || role.Contains("Performer", StringComparison.OrdinalIgnoreCase))
                ? [person.Name]
                : [],
            Authors = displayRoles.Any(role => role.Contains("Author", StringComparison.OrdinalIgnoreCase)) ? [person.Name] : [],
            Actors = displayRoles.Any(role => role.Contains("Actor", StringComparison.OrdinalIgnoreCase)) ? [person.Name] : [],
            Directors = displayRoles.Any(role => role.Contains("Director", StringComparison.OrdinalIgnoreCase)) ? [person.Name] : [],
            Writers = displayRoles.Any(role => role.Contains("Writer", StringComparison.OrdinalIgnoreCase)) ? [person.Name] : [],
            Composers = displayRoles.Any(role => role.Contains("Composer", StringComparison.OrdinalIgnoreCase)) ? [person.Name] : [],
            Narrators = displayRoles.Any(role => role.Contains("Narrator", StringComparison.OrdinalIgnoreCase)) ? [person.Name] : [],
            Illustrators = displayRoles.Any(role => role.Contains("Illustrator", StringComparison.OrdinalIgnoreCase)) ? [person.Name] : [],
            Producers = displayRoles.Any(role => role.Contains("Producer", StringComparison.OrdinalIgnoreCase)) ? [person.Name] : [],
        };

    private static IReadOnlyDictionary<string, string> BuildIdentifierFacts(
        IReadOnlyDictionary<string, string> canonicalValues,
        IReadOnlyDictionary<string, string>? bridgeIds,
        string? wikidataQid)
    {
        var identifiers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddIdentifier(identifiers, BridgeIdKeys.WikidataQid, wikidataQid);
        foreach (var key in DetailIdentifierKeys)
        {
            if (bridgeIds is not null && bridgeIds.TryGetValue(key, out var bridgeValue))
            {
                AddIdentifier(identifiers, key, bridgeValue);
            }

            AddIdentifier(identifiers, key, GetValue(canonicalValues, key));
        }

        return identifiers;
    }

    private static readonly string[] DetailIdentifierKeys =
    [
        BridgeIdKeys.WikidataQid,
        BridgeIdKeys.TmdbId,
        BridgeIdKeys.ImdbId,
        BridgeIdKeys.AppleMusicId,
        BridgeIdKeys.AppleMusicCollectionId,
        BridgeIdKeys.AppleArtistId,
        BridgeIdKeys.MusicBrainzId,
        BridgeIdKeys.MusicBrainzRecordingId,
        BridgeIdKeys.MusicBrainzReleaseGroupId,
        "musicbrainz_release_id",
        "musicbrainz_artist_id",
        "isrc",
        BridgeIdKeys.Isbn,
        BridgeIdKeys.Asin,
        BridgeIdKeys.ComicVineId,
        BridgeIdKeys.ComicVineVolumeId,
    ];

    private static void AddIdentifier(IDictionary<string, string> identifiers, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        identifiers.TryAdd(key, value.Trim());
    }

    private static IReadOnlyList<string> CreditNames(
        IReadOnlyList<CreditGroupViewModel> groups,
        CreditGroupType type)
        => groups
            .Where(group => group.GroupType == type)
            .SelectMany(group => group.Credits)
            .Select(credit => credit.DisplayName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<string> MergeNames(params IEnumerable<string>[] sources)
        => sources
            .SelectMany(source => source)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool? ParseNullableBool(params string?[] values)
    {
        foreach (var value in values)
        {
            if (bool.TryParse(value, out var parsed))
            {
                return parsed;
            }

            if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "explicit", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "clean", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return null;
    }

    private static string? ReleaseYear(string? value)
        => string.IsNullOrWhiteSpace(value) || value.Length < 4 ? null : value[..4];

    private static string? FormatCollectionDuration(IReadOnlyList<CollectionWorkSummary> works)
    {
        var seconds = works
            .Select(work => ParseDurationSeconds(work.Duration))
            .Where(value => value.HasValue)
            .Sum(value => value!.Value);

        return seconds > 0 ? FormatSecondsDuration(seconds) : null;
    }

    private static double? ParseDurationSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return seconds;
        }

        return null;
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
        {
            AddPlain(pills, "CC", "subtitles");
        }

        AddPlain(pills, detail.Language, "audio");
        if (HasReadListenCompanion(entityType, formats))
        {
            AddPlain(pills, BuildReadListenAvailabilityLabel(entityType, formats), "sync");
        }

        return pills
            .Where(value => !string.IsNullOrWhiteSpace(value.Label))
            .DistinctBy(value => value.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ProgressViewModel? BuildFormatProgress(double? progressPct)
    {
        if (progressPct is not > 0)
        {
            return null;
        }

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
        if (!IsWatchEntity(entityType) && entityType is not DetailEntityType.Audiobook)
        {
            return null;
        }

        var progress = formats
            .Select(format => format.Progress)
            .Where(value => value?.Percent is > 0 and < 99.5)
            .OrderByDescending(value => value!.Percent)
            .FirstOrDefault();
        if (progress is null)
        {
            return null;
        }

        var percent = Math.Clamp(progress.Percent, 0, 100);
        var runtimeSource = FirstNonBlank(formats.Select(format => format.Runtime).Prepend(runtime).ToArray());
        return new ProgressViewModel
        {
            Percent = percent,
            Label = entityType is DetailEntityType.Audiobook
                ? BuildListenHeroProgressLabel(percent, runtimeSource)
                : BuildHeroProgressLabel(percent, runtimeSource),
        };
    }

    private static ProgressViewModel? BuildAudiobookHeroProgress(
        DetailEntityType entityType,
        string? runtime,
        IReadOnlyList<MediaGroupingViewModel> mediaGroups)
    {
        if (entityType is not DetailEntityType.Audiobook)
        {
            return null;
        }

        var chapterGroup = mediaGroups
            .FirstOrDefault(group => string.Equals(group.Key, "chapters", StringComparison.OrdinalIgnoreCase));
        var chapters = chapterGroup?.Items ?? [];
        if (chapters.Count == 0)
        {
            return null;
        }

        var current = chapters
            .Where(item => item.ResumePositionSeconds is > 0)
            .OrderByDescending(item => item.ResumePositionSeconds)
            .FirstOrDefault()
            ?? chapters
                .Where(item => item.ProgressPercent is > 0 and < 99.5)
                .OrderByDescending(item => item.ProgressPercent)
                .FirstOrDefault();
        if (current is null)
        {
            return null;
        }

        var totalSeconds = chapters
            .Select(item => item.EndSeconds ?? item.DurationSeconds ?? 0)
            .Where(seconds => seconds > 0)
            .DefaultIfEmpty()
            .Max();
        var percent = current.ResumePositionSeconds is > 0 && totalSeconds > 0
            ? current.ResumePositionSeconds.Value / totalSeconds * 100
            : current.ProgressPercent ?? 0;
        if (percent is <= 0 or >= 99.5)
        {
            return null;
        }

        var runtimeSource = FirstNonBlank(FormatSecondsDuration(totalSeconds > 0 ? totalSeconds : null), runtime);
        var clampedPercent = Math.Clamp(percent, 0, 100);
        var roundedPercent = Math.Clamp((int)Math.Round(clampedPercent, MidpointRounding.AwayFromZero), 1, 99);
        var timeLeft = FormatTimeLeft(runtimeSource, clampedPercent);
        var currentPosition = 0;
        for (var i = 0; i < chapters.Count; i++)
        {
            if (ReferenceEquals(chapters[i], current))
            {
                currentPosition = i + 1;
                break;
            }
        }

        var hasVisibleChapters = chapters.Count > 1
            && string.Equals(chapterGroup?.Title, "Chapters", StringComparison.OrdinalIgnoreCase)
            && currentPosition > 0;
        var chaptersRemaining = hasVisibleChapters
            ? Math.Max(0, chapters.Count - currentPosition)
            : 0;

        return new ProgressViewModel
        {
            Percent = clampedPercent,
            Label = BuildListenHeroProgressLabel(clampedPercent, runtimeSource),
            ContextLabel = hasVisibleChapters ? $"{current.Title} of {chapters.Count}" : null,
            PercentLabel = $"{roundedPercent}%",
            RemainingLabel = string.IsNullOrWhiteSpace(timeLeft) ? null : $"{timeLeft} left",
            SecondaryLabel = hasVisibleChapters
                ? chaptersRemaining == 1 ? "1 chapter remaining" : $"{chaptersRemaining} chapters remaining"
                : null,
        };
    }

    private static bool HasChapterGroup(IReadOnlyList<MediaGroupingViewModel> mediaGroups) =>
        mediaGroups.Any(group =>
            string.Equals(group.Key, "chapters", StringComparison.OrdinalIgnoreCase)
            && group.Items.Count > 0
            && string.Equals(group.Title, "Chapters", StringComparison.OrdinalIgnoreCase));

    private static ProgressViewModel? BuildCollectionHeroProgress(
        DetailEntityType entityType,
        IReadOnlyList<CollectionWorkSummary> works)
    {
        if (!IsWatchEntity(entityType))
        {
            return null;
        }

        var item = works
            .Where(work => work.ProgressPercent is > 0 and < 99.5)
            .OrderByDescending(work => work.ProgressPercent)
            .FirstOrDefault();
        if (item is null || item.ProgressPercent is null)
        {
            return null;
        }

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

    private static string BuildListenHeroProgressLabel(double percent, string? runtime)
    {
        var rounded = Math.Clamp((int)Math.Round(percent, MidpointRounding.AwayFromZero), 1, 99);
        var timeLeft = FormatTimeLeft(runtime, percent);
        return string.IsNullOrWhiteSpace(timeLeft)
            ? $"Continue listening - {rounded}% listened"
            : $"Continue listening - {rounded}% listened - {timeLeft} left";
    }

    private static bool IsWatchEntity(DetailEntityType entityType)
        => entityType is DetailEntityType.Movie or DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode;

    private static bool HasAudiobookProgress(IReadOnlyList<OwnedFormatViewModel> formats)
        => formats.Any(format =>
            format.FormatType == MediaFormatType.Audiobook
            && format.Progress?.Percent is > 0 and < 99.5);

    private static IReadOnlyList<DetailAction> BuildPrimaryActions(Guid id, DetailEntityType entityType, DetailPresentationContext context, IReadOnlyList<OwnedFormatViewModel> formats, ProgressViewModel? heroProgress)
    {
        return entityType switch
        {
            DetailEntityType.Movie => BuildWatchActions($"/watch/player/resolve?workId={id}", heroProgress),
            DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode => BuildWatchActions(null, heroProgress),
            DetailEntityType.Book or DetailEntityType.ComicIssue => [new DetailAction { Key = "read", Label = "Read", Icon = "menu_book", Route = $"/book/{id}", IsPrimary = true }],
            DetailEntityType.Audiobook => [new DetailAction { Key = "listen", Label = heroProgress is null ? "Listen" : "Continue", Icon = "headphones", IsPrimary = true }],
            DetailEntityType.Work when formats.Any(f => f.FormatType == MediaFormatType.Ebook) => [new DetailAction { Key = "read", Label = "Read", Icon = "menu_book", Route = $"/book/{id}", IsPrimary = true }],
            DetailEntityType.Work when formats.Any(f => f.FormatType == MediaFormatType.Audiobook) => [new DetailAction { Key = "listen", Label = HasAudiobookProgress(formats) ? "Continue" : "Listen", Icon = "headphones", Route = $"/listen/audiobook/{id}", IsPrimary = true }],
            DetailEntityType.MusicAlbum => [new DetailAction { Key = "play-album", Label = "Listen", Icon = "headphones", IsPrimary = true }],
            DetailEntityType.MusicArtist => [new DetailAction { Key = "play-artist", Label = "Listen", Icon = "headphones", IsPrimary = true }],
            _ => [new DetailAction { Key = "open", Label = "Open", Icon = "open_in_new", IsPrimary = true }],
        };
    }

    private async Task<IReadOnlySet<Guid>> LoadFavoriteWorkIdsAsync(Guid? profileId, CancellationToken ct)
    {
        if (!profileId.HasValue)
        {
            return new HashSet<Guid>();
        }

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<object>(new CommandDefinition(
            """
            SELECT ci.work_id
            FROM collection_items ci
            INNER JOIN collections c ON c.id = ci.collection_id
            WHERE c.scope = 'user'
              AND c.profile_id = @ProfileId
              AND c.collection_type = 'Playlist'
              AND c.resolution = 'materialized'
              AND c.display_name = 'Favorites'
              AND c.is_enabled = 1;
            """,
            new { ProfileId = GuidSql.ToBlob(profileId.Value) },
            cancellationToken: ct));

        return rows
            .Select(StringValue)
            .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
    }

    private static IReadOnlyList<DetailAction> BuildSecondaryActions(Guid id, DetailEntityType entityType, bool isFavorite, IReadOnlyList<OwnedFormatViewModel>? formats = null)
    {
        var actions = new List<DetailAction>();
        var hasReadListenCompanion = HasReadListenCompanion(entityType, formats ?? []);

        if (CanFavoriteEntity(entityType))
        {
            actions.Add(BuildMyListAction(isFavorite));
            actions.Add(BuildReactionAction());
            actions.Add(new DetailAction
            {
                Key = "add-to-collection",
                Label = "Add to Collection",
                Icon = "account_tree",
                Tooltip = "Add to collection",
                DisplayStyle = "icon",
            });
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
        }

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

        return actions;
    }

    private static DetailAction BuildMyListAction(bool isSelected)
        => new()
        {
            Key = "my-list",
            Label = isSelected ? "In My List" : "My List",
            Icon = isSelected ? "check_circle" : "add",
            Tooltip = isSelected ? "Remove from My List" : "Add to My List",
            DisplayStyle = "icon",
            IsSelected = isSelected,
        };

    private static DetailAction BuildReactionAction()
        => new()
        {
            Key = "reaction-menu",
            Label = "Rate",
            Icon = "thumb_up",
            Tooltip = "Rate this title",
            DisplayStyle = "icon",
            Children =
            [
                new DetailAction { Key = "reaction-dislike", Label = "Not for me", Icon = "thumb_down" },
                new DetailAction { Key = "reaction-like", Label = "I like this", Icon = "thumb_up" },
                new DetailAction { Key = "reaction-love", Label = "I love this", Icon = "favorite" },
            ],
        };

    private static IReadOnlyList<DetailAction> BuildWatchActions(string? route, ProgressViewModel? progress)
    {
        var watch = new DetailAction
        {
            Key = "watch",
            Label = progress is null ? "Watch" : "Resume",
            Icon = "play_arrow",
            Route = route,
            IsPrimary = true,
        };

        return progress is null
            ? [watch]
            :
            [
                watch,
                new DetailAction
                {
                    Key = "restart",
                    Label = "Restart",
                    Icon = "restart_alt",
                    Route = route is null ? null : $"{route}&restart=true",
                    IsPrimary = true,
                    DisplayStyle = "secondary",
                },
            ];
    }

    private static bool HasReadListenCompanion(DetailEntityType entityType, IReadOnlyList<OwnedFormatViewModel> formats)
        => entityType is DetailEntityType.Book or DetailEntityType.Audiobook or DetailEntityType.Work
           && formats.Any(f => f.FormatType == MediaFormatType.Ebook)
           && formats.Any(f => f.FormatType == MediaFormatType.Audiobook);

    private static bool IsReadableEntity(DetailEntityType entityType)
        => entityType is DetailEntityType.Book or DetailEntityType.ComicIssue or DetailEntityType.Audiobook or DetailEntityType.Work;

    private static bool CanFavoriteEntity(DetailEntityType entityType)
        => IsReadableEntity(entityType)
           || IsWatchEntity(entityType)
           || entityType is DetailEntityType.MusicAlbum
               or DetailEntityType.MusicArtist
               or DetailEntityType.MusicTrack
               or DetailEntityType.MovieSeries
               or DetailEntityType.TvShow
               or DetailEntityType.TvSeason
               or DetailEntityType.BookSeries
               or DetailEntityType.ComicSeries;

    private static string BuildReadListenAvailabilityLabel(DetailEntityType entityType, IReadOnlyList<OwnedFormatViewModel> formats)
    {
        if (entityType == DetailEntityType.Audiobook)
        {
            return "Ebook available";
        }

        var audiobook = formats.FirstOrDefault(f => f.FormatType == MediaFormatType.Audiobook);
        var runtime = FormatRuntime(audiobook?.Runtime);
        return string.IsNullOrWhiteSpace(runtime)
            ? "Audiobook available"
            : $"Audiobook available · {runtime}";
    }

    private static DetailEditorTarget BuildCollectionEditorTarget(
        Guid collectionId,
        DetailEntityType entityType,
        Guid? rootWorkId)
    {
        if (IsCanonicalContainerEntity(entityType) && rootWorkId.HasValue)
        {
            return new DetailEditorTarget
            {
                EntityId = rootWorkId.Value.ToString("D"),
                EntityKind = "Work",
                ContainerMode = "Canonical",
                InitialTab = entityType switch
                {
                    DetailEntityType.TvShow or DetailEntityType.TvSeason => "episodes",
                    DetailEntityType.MusicAlbum => "tracks",
                    _ => "details",
                },
            };
        }

        return new DetailEditorTarget
        {
            EntityId = collectionId.ToString("D"),
            EntityKind = "Collection",
            ContainerMode = "Curated",
            InitialTab = "media",
        };
    }

    private static bool IsCanonicalContainerEntity(DetailEntityType entityType) =>
        entityType is DetailEntityType.TvShow
            or DetailEntityType.TvSeason
            or DetailEntityType.MusicAlbum
            or DetailEntityType.BookSeries
            or DetailEntityType.ComicSeries
            or DetailEntityType.MovieSeries;

    private static IReadOnlyList<DetailAction> BuildOverflowActions(Guid id, DetailEntityType entityType, bool isAdminView)
    {
        var actions = new List<DetailAction>();

        if (isAdminView)
        {
            actions.Add(new DetailAction { Key = "manage-artwork", Label = "Manage Artwork", Icon = "image", IsAdminOnly = true });
            actions.Add(new DetailAction { Key = "refresh", Label = "Refresh Metadata", Icon = "sync", IsAdminOnly = true });
            actions.Add(new DetailAction { Key = "file-info", Label = "View File Info", Icon = "info", IsAdminOnly = true });
            actions.Add(new DetailAction { Key = "delete", Label = "Delete from Library", Icon = "delete", IsAdminOnly = true, IsDestructive = true });
        }

        return actions.Where(a => !a.IsAdminOnly || isAdminView).ToList();
    }

    private static IReadOnlyList<DetailTab> BuildTabs(
        DetailEntityType entityType,
        DetailPresentationContext context,
        bool isAdminView,
        bool hasSeries = false,
        bool hasUniverse = false,
        bool hasChapters = true)
    {
        string[] keys = entityType switch
        {
            DetailEntityType.TvShow => hasUniverse ? ["episodes", "overview", "cast", "universe", "details"] : ["episodes", "overview", "cast", "details"],
            DetailEntityType.TvSeason when hasUniverse => ["episodes", "overview", "cast", "universe", "details"],
            DetailEntityType.TvSeason => ["episodes", "overview", "cast", "details"],
            DetailEntityType.Movie when hasUniverse => ["overview", "cast", "universe", "details"],
            DetailEntityType.Movie => ["overview", "cast", "details"],
            DetailEntityType.MovieSeries when hasUniverse => ["overview", "media", "cast", "universe", "details"],
            DetailEntityType.MovieSeries => ["overview", "media", "cast", "details"],
            DetailEntityType.TvEpisode when hasUniverse => ["overview", "cast", "characters", "universe", "details"],
            DetailEntityType.TvEpisode => ["overview", "cast", "characters", "details"],
            DetailEntityType.Book when hasUniverse => ["overview", "credits", "universe", "details"],
            DetailEntityType.Book => ["overview", "credits", "details"],
            DetailEntityType.Audiobook when hasUniverse && hasChapters => ["overview", "chapters", "credits", "universe", "details"],
            DetailEntityType.Audiobook when hasUniverse => ["overview", "credits", "universe", "details"],
            DetailEntityType.Audiobook when hasChapters => ["overview", "chapters", "credits", "details"],
            DetailEntityType.Audiobook => ["overview", "credits", "details"],
            DetailEntityType.BookSeries when hasUniverse => ["overview", "works", "credits", "universe", "details"],
            DetailEntityType.BookSeries => ["overview", "works", "credits", "details"],
            DetailEntityType.Work when hasUniverse => ["overview", "credits", "formats", "universe", "details"],
            DetailEntityType.Work => ["overview", "credits", "formats", "details"],
            DetailEntityType.ComicIssue when hasUniverse => ["overview", "credits", "universe", "editions", "details"],
            DetailEntityType.ComicIssue => ["overview", "credits", "editions", "details"],
            DetailEntityType.ComicSeries when hasUniverse => ["overview", "issues", "credits", "universe", "details"],
            DetailEntityType.ComicSeries => ["overview", "issues", "credits", "details"],
            DetailEntityType.MusicAlbum => ["tracks", "overview", "credits", "related", "details"],
            DetailEntityType.MusicTrack => ["overview", "credits", "related", "details"],
            DetailEntityType.MusicArtist when context == DetailPresentationContext.Listen => ["overview", "albums", "tracks", "appears-on", "credits", "related", "details"],
            DetailEntityType.Person => ["details"],
            DetailEntityType.Character when hasUniverse => ["overview", "appearances", "portrayals", "relationships", "universe", "details"],
            DetailEntityType.Character => ["overview", "appearances", "portrayals", "relationships", "details"],
            DetailEntityType.Universe => ["overview", "timeline", "media", "characters", "people", "relationships", "details"],
            _ when hasSeries && hasUniverse => ["overview", "people", "characters", "universe", "related", "details"],
            _ when hasSeries => ["overview", "people", "characters", "related", "details"],
            _ when hasUniverse => ["overview", "people", "characters", "universe", "related", "details"],
            _ => ["overview", "people", "characters", "related", "details"],
        };

        if (hasSeries && SupportsSeriesTab(entityType))
        {
            keys = ["series", "overview", .. keys.Where(key => key is not "overview" and not "sequence" and not "series")];
        }

        var tabs = keys.Select(key => new DetailTab { Key = key, Label = ToTabLabel(key) }).ToList();
        if (isAdminView)
        {
            tabs.Add(new DetailTab { Key = "registry", Label = "Registry", IsAdminOnly = true });
        }

        return tabs;
    }

    private static bool SupportsSeriesTab(DetailEntityType entityType)
        => entityType is DetailEntityType.Work
            or DetailEntityType.Movie
            or DetailEntityType.TvEpisode
            or DetailEntityType.Book
            or DetailEntityType.Audiobook
            or DetailEntityType.ComicIssue
            or DetailEntityType.MusicAlbum;

    private static void AddPlain(List<MetadataPill> values, string? label, string kind)
    {
        if (!string.IsNullOrWhiteSpace(label))
        {
            values.Add(new MetadataPill { Label = label, Kind = kind });
        }
    }

    private static string? FormatCountLabel(string? value, string singular)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
        {
            return trimmed;
        }

        var label = count == 1 ? singular : singular + "s";
        return $"{count.ToString(CultureInfo.InvariantCulture)} {label}";
    }

    private static string? FormatRating(string? rating)
    {
        if (string.IsNullOrWhiteSpace(rating))
        {
            return null;
        }

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
        {
            return NormalizeWatchQualityLabel(explicitQuality);
        }

        return NormalizeWatchQualityLabel(playbackSummary?.VideoResolutionLabel);
    }

    private static string? NormalizeWatchQualityLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

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
        {
            return null;
        }

        var trimmed = runtime.Trim();
        if (!double.TryParse(trimmed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minutes))
        {
            return trimmed;
        }

        if (minutes <= 0)
        {
            return null;
        }

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
        {
            return null;
        }

        var remainingSeconds = totalSeconds.Value * (100d - Math.Clamp(progressPercent, 0, 100)) / 100d;
        if (remainingSeconds <= 60)
        {
            return null;
        }

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
        {
            return null;
        }

        var trimmed = duration.Trim();
        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            return TryParseClockDurationSeconds(trimmed);
        }

        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) || minutes <= 0)
        {
            return null;
        }

        return (int)Math.Round(minutes * 60d, MidpointRounding.AwayFromZero);
    }

    private static int? TryParseAudioDurationSeconds(string? durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(durationSeconds))
        {
            return null;
        }

        if (!double.TryParse(durationSeconds.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            || seconds <= 0)
        {
            return null;
        }

        return (int)Math.Round(seconds >= 60000 ? seconds / 1000d : seconds, MidpointRounding.AwayFromZero);
    }

    private static string? FormatTrackDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
        {
            return null;
        }

        var trimmed = duration.Trim();
        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            return trimmed;
        }

        return FormatRuntime(trimmed);
    }

    private static string? FormatSecondsDuration(double? seconds)
    {
        if (!seconds.HasValue || seconds.Value <= 0)
        {
            return null;
        }

        var totalSeconds = (int)Math.Round(seconds.Value, MidpointRounding.AwayFromZero);
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var remainingSeconds = totalSeconds % 60;

        return hours > 0
            ? $"{hours}:{minutes:D2}:{remainingSeconds:D2}"
            : $"{minutes}:{remainingSeconds:D2}";
    }

    private static double? CalculateChapterProgress(double? resumeSeconds, double startSeconds, double? endSeconds)
    {
        if (!resumeSeconds.HasValue || !endSeconds.HasValue || endSeconds.Value <= startSeconds)
        {
            return null;
        }

        if (resumeSeconds.Value >= endSeconds.Value)
        {
            return 100;
        }

        if (resumeSeconds.Value <= startSeconds)
        {
            return null;
        }

        return Math.Clamp((resumeSeconds.Value - startSeconds) / (endSeconds.Value - startSeconds) * 100d, 0d, 100d);
    }

    private static string? FormatAlbumDuration(IReadOnlyList<CollectionWorkSummary> works)
    {
        var seconds = works
            .Select(work => TryParseClockDurationSeconds(work.Duration))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        if (seconds.Count == 0)
        {
            return null;
        }

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
        {
            return null;
        }

        var parts = duration.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 3)
        {
            return null;
        }

        var total = 0;
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            total = (total * 60) + value;
        }

        return total;
    }

    private static bool IsTruthy(string? value)
        => value?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "explicit";

    private static IEnumerable<string> SplitMetadataValues(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        foreach (var part in value.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                yield return part;
            }
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
        {
            return null;
        }

        var ebook = formats.FirstOrDefault(f => f.FormatType == MediaFormatType.Ebook);
        var audio = formats.FirstOrDefault(f => f.FormatType == MediaFormatType.Audiobook);
        if (ebook is null || audio is null)
        {
            return new ReadingListeningSyncCapabilityViewModel
            {
                State = SyncCapabilityState.NotApplicable,
                Reason = "Read + Listen Sync only applies when both ebook and audiobook formats are owned.",
            };
        }

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
        {
            return DetailEntityType.TvEpisode;
        }

        if (mediaType.Contains("movie", StringComparison.OrdinalIgnoreCase))
        {
            return DetailEntityType.Movie;
        }

        if (mediaType.Contains("audio", StringComparison.OrdinalIgnoreCase))
        {
            return DetailEntityType.Audiobook;
        }

        if (mediaType.Contains("comic", StringComparison.OrdinalIgnoreCase) || mediaType.Equals("Cbz", StringComparison.OrdinalIgnoreCase))
        {
            return DetailEntityType.ComicIssue;
        }

        if (mediaType.Contains("music", StringComparison.OrdinalIgnoreCase))
        {
            return DetailEntityType.MusicTrack;
        }

        return DetailEntityType.Book;
    }

    private static DetailEntityType InferCollectionEntityType(IReadOnlyList<CollectionWorkSummary> works)
    {
        var mediaTypes = works.Select(w => w.MediaType).ToList();
        if (mediaTypes.Any(m => m.Contains("TV", StringComparison.OrdinalIgnoreCase)) || works.Any(w => !string.IsNullOrWhiteSpace(w.Season)))
        {
            return DetailEntityType.TvShow;
        }

        if (mediaTypes.Any(m => m.Contains("movie", StringComparison.OrdinalIgnoreCase)))
        {
            return DetailEntityType.MovieSeries;
        }

        if (mediaTypes.Any(m => m.Contains("music", StringComparison.OrdinalIgnoreCase)))
        {
            return DetailEntityType.MusicAlbum;
        }

        if (mediaTypes.Any(m => m.Contains("comic", StringComparison.OrdinalIgnoreCase)))
        {
            return DetailEntityType.ComicSeries;
        }

        return DetailEntityType.Collection;
    }

    private static MediaFormatType ToFormatType(string mediaType, string? formatLabel)
    {
        var value = $"{mediaType} {formatLabel}".ToLowerInvariant();
        if (value.Contains("audio"))
        {
            return MediaFormatType.Audiobook;
        }

        if (value.Contains("epub") || value.Contains("ebook") || value.Contains("book"))
        {
            return MediaFormatType.Ebook;
        }

        if (value.Contains("comic") || value.Contains("cbz"))
        {
            return MediaFormatType.ComicIssue;
        }

        if (value.Contains("movie") || value.Contains("video"))
        {
            return MediaFormatType.Movie;
        }

        if (value.Contains("music") || value.Contains("album"))
        {
            return MediaFormatType.MusicAlbum;
        }

        if (value.Contains("tv"))
        {
            return MediaFormatType.TvSeries;
        }

        return MediaFormatType.Ebook;
    }

    private static string ToFormatDisplay(string mediaType, string? formatLabel)
    {
        if (!string.IsNullOrWhiteSpace(formatLabel))
        {
            return formatLabel;
        }

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

    private static string ResolveWorkDisplayTitle(
        string? displayTitle,
        LibraryItemDetail detail,
        IReadOnlyDictionary<string, string> values,
        DetailEntityType entityType)
    {
        if (entityType == DetailEntityType.TvEpisode)
        {
            return FirstNonBlank(displayTitle, detail.EpisodeTitle, GetValue(values, MetadataFieldConstants.EpisodeTitle), detail.Title, detail.FileName, "Untitled");
        }

        if (entityType == DetailEntityType.ComicIssue)
        {
            var issueTitle = FirstNonBlank(GetValue(values, MetadataFieldConstants.IssueTitle), displayTitle);
            if (!string.IsNullOrWhiteSpace(issueTitle)
                && !IsGeneratedComicIssueTitle(issueTitle, detail, values))
            {
                return FirstNonBlank(issueTitle, detail.Title, detail.FileName, "Untitled");
            }

            if (!IsGeneratedComicIssueTitle(detail.Title, detail, values))
            {
                return FirstNonBlank(detail.Title, detail.FileName, "Untitled");
            }

            var issueNumber = FirstNonBlank(GetValue(values, MetadataFieldConstants.IssueNumber), detail.SeriesPosition, GetValue(values, MetadataFieldConstants.SeriesPosition));
            return FirstNonBlank(FormatIssue(issueNumber), detail.FileName, "Untitled");
        }

        return FirstNonBlank(displayTitle, detail.Title, detail.EpisodeTitle, detail.FileName, "Untitled");
    }

    private static bool IsGeneratedComicIssueTitle(string? title, LibraryItemDetail detail, IReadOnlyDictionary<string, string> values)
    {
        var series = FirstNonBlank(detail.Series, GetValue(values, MetadataFieldConstants.Series));
        var issueNumber = FirstNonBlank(GetValue(values, "issue_number"), detail.SeriesPosition, GetValue(values, MetadataFieldConstants.SeriesPosition));
        return IsGeneratedComicIssueTitle(title, series, issueNumber);
    }

    private static bool IsGeneratedComicIssueTitle(string? title, string? series, string? issueNumber)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(series) || string.IsNullOrWhiteSpace(issueNumber))
        {
            return false;
        }

        var normalizedTitle = NormalizeOrdinalTitle(title);
        var normalizedSeries = NormalizeOrdinalTitle(series);
        var normalizedIssue = NormalizeOrdinalTitle(issueNumber);
        return normalizedTitle == normalizedSeries
            || normalizedTitle == $"{normalizedSeries}{normalizedIssue}"
            || normalizedTitle == $"{normalizedSeries}issue{normalizedIssue}"
            || normalizedTitle == $"{normalizedSeries}no{normalizedIssue}"
            || (normalizedTitle.StartsWith(normalizedSeries, StringComparison.OrdinalIgnoreCase)
                && normalizedTitle.EndsWith(normalizedIssue, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveSequenceItemTitle(DetailEntityType entityType, string title, string containerTitle, string? positionLabel)
    {
        if (entityType == DetailEntityType.ComicIssue && IsGeneratedComicIssueTitle(title, containerTitle, positionLabel))
        {
            return FirstNonBlank(FormatIssue(positionLabel), title);
        }

        return title;
    }

    private static string NormalizeOrdinalTitle(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string? BuildSubtitle(
        LibraryItemDetail detail,
        DetailEntityType entityType,
        IReadOnlyDictionary<string, string> values,
        MultiFormatState state)
    {
        if (state == (MultiFormatState)(-1))
        {
            return "Book + Audiobook • Separate Progress";
        }

        return entityType switch
        {
            DetailEntityType.Book => detail.Author,
            DetailEntityType.Audiobook => FirstNonBlank(detail.Narrator, detail.Author),
            DetailEntityType.Movie => FirstNonBlank(detail.Director, GetValue(values, "studio"), detail.Year, "Movie"),
            DetailEntityType.MusicTrack => string.Join(" â€¢ ", new[] { detail.Artist, GetValue(values, "album") }.Where(s => !string.IsNullOrWhiteSpace(s))),
            DetailEntityType.ComicIssue => string.Join(" - ", new[] { detail.Series, FormatIssue(detail.SeriesPosition), FirstNonBlank(detail.Writer, detail.Illustrator, detail.Author) }.Where(s => !string.IsNullOrWhiteSpace(s))),
            DetailEntityType.TvEpisode => string.Join(" • ", new[] { detail.ShowName, FormatSeasonEpisode(detail.SeasonNumber, detail.EpisodeNumber) }.Where(s => !string.IsNullOrWhiteSpace(s))),
            _ => FormatEntityType(entityType),
        };
    }

    private static IReadOnlyList<RelationshipGroup> BuildRelationshipStrip(LibraryItemDetail detail, SequencePlacementViewModel? sequence)
    {
        var groups = new List<RelationshipGroup>();
        if (sequence is not null)
        {
            groups.Add(new RelationshipGroup
            {
                Title = sequence.ContainerLabel,
                Items = [new RelatedEntityChip
                {
                    Id = sequence.ContainerId,
                    EntityType = RelatedEntityType.Series,
                    Label = sequence.ContainerTitle,
                    Route = BuildSequenceContainerRoute(sequence),
                }],
            });
        }

        var universeQid = ExtractQid(detail.UniverseSummary?.UniverseQid);
        if (!string.IsNullOrWhiteSpace(detail.UniverseSummary?.UniverseName))
        {
            groups.Add(new RelationshipGroup
            {
                Title = "Universe",
                Items = [new RelatedEntityChip
                {
                    Id = universeQid ?? detail.UniverseSummary.UniverseName!,
                    EntityType = RelatedEntityType.Universe,
                    Label = detail.UniverseSummary.UniverseName!,
                    Route = BuildUniverseExploreRoute(universeQid),
                }],
            });
        }

        return groups;
    }

    private static string? BuildSequenceContainerRoute(SequencePlacementViewModel sequence)
    {
        if (!Guid.TryParse(sequence.ContainerId, out var id))
        {
            return null;
        }

        var entityType = sequence.CurrentItem.EntityType switch
        {
            DetailEntityType.Movie => DetailEntityType.MovieSeries,
            DetailEntityType.TvEpisode or DetailEntityType.TvSeason => DetailEntityType.TvShow,
            DetailEntityType.ComicIssue => DetailEntityType.ComicSeries,
            DetailEntityType.Book or DetailEntityType.Audiobook or DetailEntityType.Work => DetailEntityType.BookSeries,
            _ => (DetailEntityType?)null,
        };

        return entityType is null
            ? null
            : $"/details/{ToDetailRouteEntityType(entityType.Value)}/{id:D}?context={DetailContextKey(entityType.Value)}";
    }

    private static string? BuildUniverseExploreRoute(string? qid)
    {
        var normalizedQid = ExtractQid(qid);
        return IsWikidataQid(normalizedQid) ? $"/universe/{normalizedQid}/explore" : null;
    }

    private static string ToDetailRouteEntityType(DetailEntityType entityType)
        => entityType.ToString().Replace("Tv", "tv-", StringComparison.Ordinal).ToLowerInvariant();

    private static string DetailContextKey(DetailEntityType entityType) => entityType switch
    {
        DetailEntityType.Movie or DetailEntityType.MovieSeries or DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode => "watch",
        DetailEntityType.MusicAlbum or DetailEntityType.MusicArtist or DetailEntityType.MusicTrack or DetailEntityType.Audiobook => "listen",
        DetailEntityType.Book or DetailEntityType.BookSeries or DetailEntityType.ComicIssue or DetailEntityType.ComicSeries or DetailEntityType.Work => "read",
        _ => "default",
    };

    private static bool HasUniverseRelationship(IReadOnlyList<RelationshipGroup> relationships) =>
        relationships.Any(group =>
            string.Equals(group.Title, "Universe", StringComparison.OrdinalIgnoreCase) ||
            group.Items.Any(item => item.EntityType == RelatedEntityType.Universe));

    private static HeroBrandViewModel? BuildHeroBrand(DetailEntityType entityType, string? label, string? imageUrl)
    {
        if (entityType is not (DetailEntityType.TvShow or DetailEntityType.TvSeason or DetailEntityType.TvEpisode))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(label) && string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        return new HeroBrandViewModel
        {
            Label = string.IsNullOrWhiteSpace(label) ? null : label,
            ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl,
        };
    }

    private static IReadOnlyList<CollectionWorkSummary> MergeCollectionManifestPlaceholders(
        DetailEntityType entityType,
        IReadOnlyList<CollectionWorkSummary> works,
        SeriesManifestViewDto? manifest)
    {
        if (manifest?.Items.Count is not > 0 || entityType is not (DetailEntityType.BookSeries or DetailEntityType.ComicSeries or DetailEntityType.MovieSeries))
        {
            return works;
        }

        var byId = works
            .Where(work => Guid.TryParse(work.Id, out _))
            .ToDictionary(work => work.Id, StringComparer.OrdinalIgnoreCase);
        var byTitle = works
            .GroupBy(work => NormalizeSeriesTitle(work.Title), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(group => group.Key!, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<CollectionWorkSummary>();

        foreach (var item in manifest.Items.OrderBy(ManifestItemSortOrder).ThenBy(item => item.ItemLabel ?? item.ItemQid, StringComparer.OrdinalIgnoreCase))
        {
            var linkedId = item.LinkedWorkId?.ToString("D");
            if (!string.IsNullOrWhiteSpace(linkedId) && byId.TryGetValue(linkedId, out var linkedWork))
            {
                if (consumed.Add(linkedWork.Id))
                {
                    result.Add(linkedWork);
                }

                continue;
            }

            var titleKey = NormalizeSeriesTitle(item.ItemLabel);
            if (!string.IsNullOrWhiteSpace(titleKey) && byTitle.TryGetValue(titleKey, out var titledWork) && consumed.Add(titledWork.Id))
            {
                result.Add(titledWork);
                continue;
            }

            result.Add(CreateMissingManifestWork(entityType, item));
        }

        result.AddRange(works.Where(work => consumed.Add(work.Id)));
        return result;
    }

    private static double ManifestItemSortOrder(SeriesManifestItemDto item) =>
        item.SortOrder ?? item.ParsedOrdinal ?? double.MaxValue;

    private static CollectionWorkSummary CreateMissingManifestWork(DetailEntityType entityType, SeriesManifestItemDto item)
    {
        var qid = NormalizeQid(item.ItemQid) ?? item.ItemQid;
        return new CollectionWorkSummary(
            $"missing-{qid}",
            ManifestPlaceholderMediaType(entityType, item.MediaType),
            item.SortOrder is { } sortOrder ? (int)Math.Round(sortOrder, MidpointRounding.AwayFromZero) : null,
            FirstNonBlank(item.ItemLabel, item.ItemQid, "Missing from library"),
            item.ItemDescription,
            null,
            null,
            null,
            null,
            PublicationYear(item.PublicationDate),
            null,
            false,
            null,
            null,
            false,
            "Missing",
            true,
            null,
            null);
    }

    private static string ManifestPlaceholderMediaType(DetailEntityType entityType, string? mediaType) =>
        FirstNonBlank(mediaType, entityType switch
        {
            DetailEntityType.ComicSeries => "Comic",
            DetailEntityType.MovieSeries => "Movie",
            DetailEntityType.BookSeries => "Book",
            _ => "Unknown",
        });

    private static string? PublicationYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var year = value.Trim();
        return year.Length >= 4 && year.Take(4).All(char.IsDigit) ? year[..4] : year;
    }

    private static SequencePlacementViewModel? BuildCollectionSequencePlacement(
        Guid collectionId,
        DetailEntityType entityType,
        string containerTitle,
        string? sourceContainerId,
        string? containerDescription,
        IReadOnlyList<CollectionWorkSummary> works,
        int? expectedTotal)
    {
        if (entityType is not (DetailEntityType.TvShow
            or DetailEntityType.MovieSeries
            or DetailEntityType.BookSeries
            or DetailEntityType.ComicSeries))
        {
            return null;
        }

        var orderedWorks = entityType == DetailEntityType.TvShow
            ? DeduplicateTvEpisodeSummaries(works)
                .Where(work => work.IsOwned || !string.IsNullOrWhiteSpace(work.Season))
                .ToList()
            : works
                .OrderBy(work => work.Ordinal ?? int.MaxValue)
                .ThenBy(work => work.Year, StringComparer.OrdinalIgnoreCase)
                .ThenBy(work => work.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        if (orderedWorks.Count == 0)
        {
            return null;
        }

        var labels = ResolveSequenceLabels(entityType);
        var items = orderedWorks.Select((work, index) =>
        {
            var itemType = InferMediaItemEntityType(work);
            var positionLabel = entityType == DetailEntityType.TvShow
                ? FirstNonBlank(work.Episode, work.Ordinal?.ToString(CultureInfo.InvariantCulture))
                : work.Ordinal?.ToString(CultureInfo.InvariantCulture);
            var positionNumber = TryParseInt(positionLabel);
            var season = entityType == DetailEntityType.TvShow
                ? FirstNonBlank(NormalizeEpisodeKey(work.Season), "1")
                : null;
            var groupKey = season is null ? null : $"season-{season}";
            var groupTitle = season is null ? null : SeasonDisplayTitle(season);

            return new SequenceItemViewModel
            {
                Id = work.Id,
                EntityType = itemType,
                Title = work.Title,
                Description = work.Description,
                Duration = FormatTrackDuration(work.Duration),
                ArtworkUrl = FirstNonBlank(work.BackgroundUrl, work.ArtworkUrl),
                Route = work.IsOwned ? BuildWorkRoute(work) : null,
                PublicationDate = work.Year,
                PositionNumber = positionNumber,
                PositionSort = positionNumber ?? work.Ordinal ?? index + 1,
                PositionLabel = positionLabel,
                PositionText = entityType == DetailEntityType.TvShow && !string.IsNullOrWhiteSpace(positionLabel)
                    ? $"E{NormalizeSequenceOrdinal(positionLabel)}"
                    : positionLabel,
                GroupKey = groupKey,
                GroupTitle = groupTitle,
                MembershipScope = SeriesMembershipScopeNames.MainSequence,
                IsOwned = work.IsOwned,
                ProgressState = work.IsOwned ? LibraryProgressState.Unstarted : LibraryProgressState.Missing,
            };
        }).ToList();

        var groups = entityType == DetailEntityType.TvShow
            ? items
                .GroupBy(item => item.GroupKey ?? "season-1")
                .OrderBy(group => SeasonSortOrder(group.Key))
                .Select(group => new SequenceGroupViewModel
                {
                    Key = group.Key,
                    Title = group.First().GroupTitle ?? "Season 1",
                    TotalKnownItems = group.Count(),
                    Items = group.OrderBy(item => item.PositionSort ?? double.MaxValue).ToList(),
                })
                .ToList()
            : [];
        var initialGroup = groups.FirstOrDefault(group => !string.Equals(group.Key, "season-0", StringComparison.OrdinalIgnoreCase))
            ?? groups.FirstOrDefault();
        var representative = initialGroup?.Items.FirstOrDefault(item => item.IsOwned)
            ?? initialGroup?.Items.FirstOrDefault()
            ?? items.FirstOrDefault(item => item.IsOwned)
            ?? items[0];
        var normalizedSourceId = NormalizeSequenceContainerId(sourceContainerId);
        var containerId = collectionId.ToString("D");
        var totalKnownItems = Math.Max(items.Count, expectedTotal ?? 0);

        return new SequencePlacementViewModel
        {
            ContainerId = containerId,
            SourceContainerId = normalizedSourceId,
            ContainerTitle = containerTitle,
            ContainerDescription = containerDescription,
            SelectedContainerId = containerId,
            AvailableContainers =
            [
                new SequenceContainerOptionViewModel
                {
                    ContainerId = containerId,
                    SourceContainerId = normalizedSourceId,
                    ContainerTitle = containerTitle,
                    IsSelected = true,
                    IsDefault = true,
                    MediaScope = SeriesMediaFilter(entityType, entityType == DetailEntityType.TvShow ? "TV" : entityType.ToString()),
                    EquivalentContainerIds = string.IsNullOrWhiteSpace(normalizedSourceId) ? [] : [normalizedSourceId],
                }
            ],
            ContainerLabel = labels.ContainerLabel,
            ItemLabel = labels.ItemLabel,
            ItemPluralLabel = labels.ItemPluralLabel,
            GroupLabel = labels.GroupLabel,
            CurrentGroupKey = initialGroup?.Key,
            TotalKnownItems = totalKnownItems,
            HasAuthoritativeTotal = expectedTotal is > 0,
            OrderingType = entityType switch
            {
                DetailEntityType.TvShow => SequenceOrderingType.EpisodeNumber,
                DetailEntityType.ComicSeries => SequenceOrderingType.IssueNumber,
                DetailEntityType.MovieSeries => SequenceOrderingType.ReleaseOrder,
                _ => SequenceOrderingType.PublicationOrder,
            },
            CurrentItem = representative,
            OrderedItems = items,
            Groups = groups,
        };
    }

    private static string SeasonDisplayTitle(string season)
        => string.Equals(season, "0", StringComparison.OrdinalIgnoreCase) ? "Specials" : $"Season {season}";

    private static int SeasonSortOrder(string groupKey)
    {
        var season = TryParseInt(groupKey.Replace("season-", string.Empty, StringComparison.OrdinalIgnoreCase));
        return season == 0 ? int.MaxValue : season ?? int.MaxValue - 1;
    }

    private static IReadOnlyList<MediaGroupingViewModel> BuildCollectionMediaGroups(
        DetailEntityType entityType,
        IReadOnlyList<CollectionWorkSummary> works,
        IReadOnlySet<Guid> favoriteWorkIds,
        int? expectedTotal)
    {
        return
        [
            ApplyMediaGroupCompletion(new MediaGroupingViewModel
            {
                Key = entityType switch
                {
                    DetailEntityType.MusicAlbum => "tracks",
                    DetailEntityType.MovieSeries => "films",
                    DetailEntityType.BookSeries => "books",
                    DetailEntityType.ComicSeries => "issues",
                    _ => "items",
                },
                Title = entityType switch
                {
                    DetailEntityType.MusicAlbum => "Tracks",
                    DetailEntityType.MovieSeries => "Films",
                    DetailEntityType.BookSeries => "Books",
                    DetailEntityType.ComicSeries => "Issues",
                    _ => "Items",
                },
                Items = works.Select(work => ToMediaItem(work, favoriteWorkIds)).ToList(),
                TotalCount = expectedTotal is > 0 ? expectedTotal.Value : 0,
            })
        ];
    }

    private static MediaGroupingViewModel ApplyMediaGroupCompletion(MediaGroupingViewModel group)
    {
        var total = Math.Max(group.Items.Count, group.TotalCount);
        var owned = group.Items.Count(item => item.IsOwned);
        var missing = Math.Max(0, total - owned);
        return new MediaGroupingViewModel
        {
            Key = group.Key,
            Title = group.Title,
            Items = group.Items,
            OwnedCount = owned,
            TotalCount = total,
            MissingCount = missing,
            CompletionPercent = total == 0 ? 0 : owned * 100.0 / total,
            InitiallyCollapsed = total > 0 && owned == 0,
        };
    }

    private static IReadOnlyList<CollectionWorkSummary> DeduplicateTvEpisodeSummaries(IReadOnlyList<CollectionWorkSummary> works)
    {
        return works
            .GroupBy(BuildTvEpisodeDeduplicationKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(work => work.IsOwned)
                .ThenByDescending(work => !string.IsNullOrWhiteSpace(work.BackgroundUrl))
                .ThenByDescending(work => !string.IsNullOrWhiteSpace(work.ArtworkUrl))
                .ThenBy(work => work.Ordinal ?? int.MaxValue)
                .First())
            .OrderBy(work => TryParseInt(work.Season) ?? int.MaxValue)
            .ThenBy(work => TryParseInt(work.Episode) ?? work.Ordinal ?? int.MaxValue)
            .ThenBy(work => work.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildTvEpisodeDeduplicationKey(CollectionWorkSummary work)
    {
        var season = NormalizeEpisodeKey(work.Season);
        var episode = NormalizeEpisodeKey(work.Episode);

        if (!string.IsNullOrWhiteSpace(season) || !string.IsNullOrWhiteSpace(episode))
        {
            return $"{season}:{episode}";
        }

        return NormalizeTextKey(work.Title);
    }

    private static string NormalizeEpisodeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().TrimStart('0');
        return normalized.Length == 0 ? "0" : normalized;
    }

    private static string NormalizeTextKey(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static MediaGroupingItemViewModel ToMediaItem(CollectionWorkSummary work, IReadOnlySet<Guid> favoriteWorkIds)
    {
        var entityType = InferMediaItemEntityType(work);
        return new MediaGroupingItemViewModel
        {
            Id = work.Id,
            EntityType = entityType,
            Title = work.Title,
            Subtitle = entityType == DetailEntityType.MusicTrack
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
            Actions = work.IsOwned
                ? [new DetailAction
                {
                    Key = entityType == DetailEntityType.TvEpisode ? "play" : "open",
                    Label = entityType == DetailEntityType.TvEpisode ? "Play" : "Open",
                    Icon = entityType == DetailEntityType.TvEpisode ? "play_arrow" : "open_in_new",
                    Route = BuildWorkRoute(work),
                }]
                : [],
            IsOwned = work.IsOwned,
            IsFavorite = Guid.TryParse(work.Id, out var workId) && favoriteWorkIds.Contains(workId),
            ProgressState = work.IsOwned ? LibraryProgressState.Unstarted : LibraryProgressState.Missing,
        };
    }

    private static IReadOnlyList<MetadataPill> BuildEpisodeMetadata(string? duration, string? year)
    {
        var values = new List<MetadataPill>();
        if (!string.IsNullOrWhiteSpace(duration))
        {
            values.Add(new MetadataPill { Label = duration, Kind = "duration" });
        }

        if (!string.IsNullOrWhiteSpace(year))
        {
            values.Add(new MetadataPill { Label = year, Kind = "year" });
        }

        return values;
    }

    private static DetailEntityType InferMediaItemEntityType(CollectionWorkSummary work)
    {
        return InferMediaItemEntityType(work.MediaType, work.Episode);
    }

    private static DetailEntityType InferMediaItemEntityType(string mediaType, string? episode)
    {
        if (!string.IsNullOrWhiteSpace(episode) || mediaType.Contains("TV", StringComparison.OrdinalIgnoreCase))
        {
            return DetailEntityType.TvEpisode;
        }

        if (mediaType.Contains("movie", StringComparison.OrdinalIgnoreCase))
        {
            return DetailEntityType.Movie;
        }

        if (mediaType.Contains("music", StringComparison.OrdinalIgnoreCase))
        {
            return DetailEntityType.MusicTrack;
        }

        if (mediaType.Contains("audio", StringComparison.OrdinalIgnoreCase))
        {
            return DetailEntityType.Audiobook;
        }

        if (mediaType.Contains("comic", StringComparison.OrdinalIgnoreCase))
        {
            return DetailEntityType.ComicIssue;
        }

        return DetailEntityType.Book;
    }

    private static string BuildWorkRoute(CollectionWorkSummary work) => InferMediaItemEntityType(work) switch
    {
        DetailEntityType.Movie => $"/watch/movie/{work.Id}",
        DetailEntityType.TvEpisode => $"/watch/player/resolve?workId={work.Id}",
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

        return [new MetadataPill { Label = FormatEntityType(entityType), Kind = "type" }, new MetadataPill { Label = OwnedCollectionCountLabel(entityType, works), Kind = "count" }];
    }

    private static IReadOnlyList<DetailAction> BuildCollectionActions(Guid id, DetailEntityType entityType, DetailPresentationContext context, ProgressViewModel? heroProgress)
        => entityType switch
        {
            DetailEntityType.TvShow => BuildWatchActions(null, heroProgress),
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
        {
            return await BuildCollectionTextCreditsAsync(collectionId, entityType, canonicalValues, ct);
        }

        rootWorkId ??= works
            .Select(work => Guid.TryParse(work.Id, out var parsed) ? parsed : (Guid?)null)
            .FirstOrDefault(id => id.HasValue);

        if (!rootWorkId.HasValue)
        {
            return [];
        }

        var cast = await _personCredits.BuildForWorkAsync(rootWorkId.Value, ct);
        if (cast.Count == 0)
        {
            return [];
        }

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
            {
                return;
            }

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
                        GetValue(canonicalValues, $"{canonicalArrayKey}_profile_url"),
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
        {
            return entries;
        }

        entries = await LoadContributorEntriesFromClaimsAsync(collectionId, canonicalArrayKey, ct);
        if (entries.Count > 0)
        {
            return entries;
        }

        if (string.IsNullOrWhiteSpace(fallbackValue))
        {
            return [];
        }

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
        {
            return [];
        }

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
        {
            return [];
        }

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
        {
            return [];
        }

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
            {
                yield return credit;
            }
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
        {
            return FirstNonBlank(GetValue(values, "album_artist"), GetValue(values, "artist"), works.Select(w => w.Artist).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)), "Album")!;
        }

        if (entityType == DetailEntityType.ComicSeries)
        {
            return $"Comic Volume - {OwnedCollectionCountLabel(entityType, works)}";
        }

        var types = works.Select(w => FormatEntityType(InferMediaItemEntityType(w))).Distinct(StringComparer.OrdinalIgnoreCase).Take(3);
        return $"{FormatEntityType(entityType)} • {OwnedCollectionCountLabel(entityType, works)} • {string.Join(", ", types)}";
    }

    private static string OwnedCollectionCountLabel(DetailEntityType entityType, IReadOnlyList<CollectionWorkSummary> works)
    {
        var ownedCount = works.Count(work => work.IsOwned);
        var totalCount = works.Count;
        var noun = CollectionItemNoun(entityType, totalCount);

        return totalCount > ownedCount
            ? $"{ownedCount} of {totalCount} {noun} owned"
            : $"{ownedCount} owned {noun}";
    }

    private static string CollectionItemNoun(DetailEntityType entityType, int count) =>
        entityType switch
        {
            DetailEntityType.ComicSeries => count == 1 ? "issue" : "issues",
            DetailEntityType.MusicAlbum => count == 1 ? "track" : "tracks",
            _ => count == 1 ? "item" : "items",
        };

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
                    Subtitle = BuildPersonMediaCreditSubtitle(c),
                    ArtworkUrl = c.CoverUrl,
                    Actions = [new DetailAction { Key = "open", Label = "Open", Icon = "open_in_new", Route = BuildCreditRoute(c) }],
                    IsOwned = true,
                }).ToList(),
            }).ToList();

    private static string BuildPersonMediaCreditSubtitle(MediaEngine.Api.Models.PersonLibraryCreditDto credit)
    {
        var characterSummary = credit.Characters.Count switch
        {
            0 => null,
            1 => credit.Characters[0].CharacterName,
            _ => string.Join(", ", credit.Characters
                .Select(character => character.CharacterName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Take(2)) + (credit.Characters.Count > 2 ? $" +{credit.Characters.Count - 2}" : string.Empty),
        };

        return string.Join(" • ", new[] { FirstNonBlank(characterSummary, credit.Role), credit.Year }.Where(v => !string.IsNullOrWhiteSpace(v)));
    }

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
        {
            return mediaRoles;
        }

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
        {
            return null;
        }

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
            new { personId },
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
            new { personId },
            cancellationToken: ct));

        if (!string.IsNullOrWhiteSpace(description))
        {
            return description;
        }

        if (string.IsNullOrWhiteSpace(qid))
        {
            return null;
        }

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
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 220
            && !trimmed.Contains('\n')
            && !trimmed.Contains(". ", StringComparison.Ordinal);
    }

    private static DescriptionAttributionViewModel? BuildWikipediaDescriptionAttribution(
        string? description,
        string? wikipediaUrl,
        DateTimeOffset? retrievedAt = null,
        bool isModifiedOrSummarized = false)
    {
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(wikipediaUrl))
        {
            return null;
        }

        return new DescriptionAttributionViewModel
        {
            SourceName = "Wikipedia",
            SourceTitle = "article text",
            SourceUrl = wikipediaUrl,
            LicenseName = "CC BY-SA 4.0",
            LicenseUrl = "https://creativecommons.org/licenses/by-sa/4.0/",
            RetrievedAt = retrievedAt,
            IsModifiedOrSummarized = isModifiedOrSummarized,
            Notice = "Text from Wikipedia is available under the Creative Commons Attribution-ShareAlike 4.0 License; additional terms may apply.",
        };
    }

    private static DescriptionAttributionViewModel? BuildDescriptionAttribution(
        DescriptionSelection selection,
        LibraryItemDetail detail,
        IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrWhiteSpace(selection.Text) || selection.IsGeneratedFallback)
        {
            return null;
        }

        if (string.Equals(selection.SourceKey, MetadataFieldConstants.IssueDescription, StringComparison.OrdinalIgnoreCase))
        {
            var winningProviderId = GetCanonicalProviderId(detail, MetadataFieldConstants.IssueDescription);
            var isComicVine = Guid.TryParse(winningProviderId, out var providerId)
                && providerId == WellKnownProviders.ComicVine;
            if (!isComicVine)
            {
                return null;
            }

            var sourceUrl = ResolveComicVineIssueUrl(values);
            return new DescriptionAttributionViewModel
            {
                SourceName = "Comic Vine",
                SourceTitle = "issue synopsis",
                SourceUrl = sourceUrl,
                LicenseName = "Comic Vine API Terms",
                LicenseUrl = "https://comicvine.gamespot.com/api/",
                RetrievedAt = GetCanonicalLastScoredAt(detail, MetadataFieldConstants.IssueDescription),
                IsModifiedOrSummarized = false,
                Notice = "Issue synopsis from Comic Vine; use is governed by Comic Vine API terms.",
            };
        }

        return BuildWikipediaDescriptionAttribution(
            selection.Text,
            GetValue(values, "wikipedia_url"),
            GetCanonicalLastScoredAt(detail, selection.SourceKey ?? MetadataFieldConstants.Description));
    }

    private static IReadOnlyList<ExternalSourceLinkViewModel> BuildExternalSourceLinks(
        string? wikidataQid,
        string? wikipediaUrl,
        SequencePlacementViewModel? sequence,
        IReadOnlyDictionary<string, string>? values = null)
    {
        var links = new List<ExternalSourceLinkViewModel>();
        AddExternalSourceLink(
            links,
            "wikipedia",
            "Wikipedia",
            wikipediaUrl,
            "Wikipedia",
            "Description source");

        var qid = ExtractQid(wikidataQid);
        var qidScope = values is not null
            ? GetValue(values, MetadataFieldConstants.WikidataQidScope)
            : null;
        var qidIsSeriesScoped = string.Equals(qidScope, "series", StringComparison.OrdinalIgnoreCase)
            || string.Equals(qidScope, "run", StringComparison.OrdinalIgnoreCase);
        AddExternalSourceLink(
            links,
            "wikidata",
            qidIsSeriesScoped ? "Series on Wikidata" : "Wikidata",
            BuildWikidataEntityUrl(qid),
            "Wikidata",
            qidIsSeriesScoped ? "Series/run identity source" : "Canonical identity source");

        var seriesQid = ExtractQid(FirstText(sequence?.SourceContainerId, sequence?.ContainerId));
        if (!string.IsNullOrWhiteSpace(seriesQid)
            && !string.Equals(seriesQid, qid, StringComparison.OrdinalIgnoreCase))
        {
            AddExternalSourceLink(
                links,
                "wikidata-series",
                "Series on Wikidata",
                BuildWikidataEntityUrl(seriesQid),
                "Wikidata",
                $"Sequence source for {sequence?.ContainerTitle ?? "this series"}");
        }

        AddExternalSourceLink(
            links,
            "comicvine-issue",
            "Comic Vine",
            ResolveComicVineIssueUrl(values),
            "Comic Vine",
            "Comic issue metadata source");

        AddExternalSourceLink(
            links,
            "tmdb",
            "TMDB",
            BuildTmdbSourceUrl(values),
            "TMDB",
            "Movie or TV metadata source");

        AddExternalSourceLink(
            links,
            "apple-music-album",
            "Apple Music",
            BuildAppleMusicAlbumUrl(GetOptionalValue(values, BridgeIdKeys.AppleMusicCollectionId)),
            "Apple Music",
            "Album metadata source");

        AddExternalSourceLink(
            links,
            "apple-music-track",
            "Apple Music Track",
            BuildAppleMusicTrackUrl(GetOptionalValue(values, BridgeIdKeys.AppleMusicId)),
            "Apple Music",
            "Track metadata source");

        AddExternalSourceLink(
            links,
            "musicbrainz-release-group",
            "MusicBrainz",
            BuildMusicBrainzUrl("release-group", GetOptionalValue(values, BridgeIdKeys.MusicBrainzReleaseGroupId)),
            "MusicBrainz",
            "Music release-group identity source");

        AddExternalSourceLink(
            links,
            "musicbrainz-recording",
            "MusicBrainz Recording",
            BuildMusicBrainzUrl("recording", GetOptionalValue(values, BridgeIdKeys.MusicBrainzRecordingId)),
            "MusicBrainz",
            "Track recording identity source");

        AddExternalSourceLink(
            links,
            "musicbrainz-release",
            "MusicBrainz Release",
            BuildMusicBrainzUrl("release", FirstNonBlank(GetOptionalValue(values, "musicbrainz_release_id"), GetOptionalValue(values, BridgeIdKeys.MusicBrainzId))),
            "MusicBrainz",
            "Music release identity source");

        return links;
    }

    private static void AddExternalSourceLink(
        List<ExternalSourceLinkViewModel> links,
        string key,
        string label,
        string? url,
        string sourceName,
        string? tooltip)
    {
        if (string.IsNullOrWhiteSpace(url)
            || links.Any(link => string.Equals(link.Url, url, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        links.Add(new ExternalSourceLinkViewModel
        {
            Key = key,
            Label = label,
            Url = url,
            SourceName = sourceName,
            Tooltip = tooltip,
        });
    }

    private static string? BuildWikidataEntityUrl(string? qid)
        => IsWikidataQid(qid) ? $"https://www.wikidata.org/wiki/{NormalizeSequenceContainerId(qid)}" : null;

    private static string? GetOptionalValue(IReadOnlyDictionary<string, string>? values, string key)
        => values is null ? null : GetValue(values, key);

    private static string? BuildTmdbSourceUrl(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null)
        {
            return null;
        }

        var tvId = FirstNonBlank(GetValue(values, "tmdb_tv_id"), !string.IsNullOrWhiteSpace(GetValue(values, MetadataFieldConstants.ShowName)) ? GetValue(values, BridgeIdKeys.TmdbId) : null);
        if (!string.IsNullOrWhiteSpace(tvId))
        {
            return $"https://www.themoviedb.org/tv/{Uri.EscapeDataString(tvId)}";
        }

        var movieId = FirstNonBlank(GetValue(values, "tmdb_movie_id"), GetValue(values, BridgeIdKeys.TmdbId));
        return string.IsNullOrWhiteSpace(movieId)
            ? null
            : $"https://www.themoviedb.org/movie/{Uri.EscapeDataString(movieId)}";
    }

    private static string? BuildAppleMusicAlbumUrl(string? id)
        => string.IsNullOrWhiteSpace(id)
            ? null
            : $"https://music.apple.com/us/album/{Uri.EscapeDataString(id)}";

    private static string? BuildAppleMusicTrackUrl(string? id)
        => string.IsNullOrWhiteSpace(id)
            ? null
            : $"https://music.apple.com/us/song/{Uri.EscapeDataString(id)}";

    private static string? BuildMusicBrainzUrl(string entityType, string? id)
        => string.IsNullOrWhiteSpace(id)
            ? null
            : $"https://musicbrainz.org/{entityType}/{Uri.EscapeDataString(id)}";

    private static string? ResolveComicVineIssueUrl(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null)
        {
            return null;
        }

        return FirstText(
            NormalizeExternalUrl(GetValue(values, MetadataFieldConstants.IssueSourceUrl)),
            BuildComicVineIssueUrl(GetValue(values, BridgeIdKeys.ComicVineId)));
    }

    private static string? NormalizeExternalUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : null;
    }

    private static string? BuildComicVineIssueUrl(string? comicVineId)
    {
        if (string.IsNullOrWhiteSpace(comicVineId))
        {
            return null;
        }

        var id = comicVineId.Trim();
        var delimiter = id.IndexOf("::", StringComparison.Ordinal);
        if (delimiter >= 0)
        {
            id = id[..delimiter].Trim();
        }

        return id.All(char.IsDigit)
            ? $"https://comicvine.gamespot.com/issue/4000-{Uri.EscapeDataString(id)}/"
            : null;
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
            WikidataUrl = BuildWikidataEntityUrl(person.WikidataQid),
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
        {
            return;
        }

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
        {
            return null;
        }

        var value = rawValue.Trim();
        var isUrl = value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (isUrl)
        {
            return value;
        }

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
        {
            return ArtworkSource.Generated;
        }

        if (source.Contains("user", StringComparison.OrdinalIgnoreCase) || source.Contains("manual", StringComparison.OrdinalIgnoreCase))
        {
            return ArtworkSource.User;
        }

        return ArtworkSource.Provider;
    }

    private static CanonicalIdentityStatus ResolveIdentityStatus(string? qid, string? status, double? confidence)
    {
        if (!string.IsNullOrWhiteSpace(qid))
        {
            return CanonicalIdentityStatus.WikidataLinked;
        }

        if (status?.Contains("review", StringComparison.OrdinalIgnoreCase) == true || confidence is < 0.7)
        {
            return CanonicalIdentityStatus.NeedsReview;
        }

        return CanonicalIdentityStatus.ProviderMatched;
    }

    private static SequenceLabels ResolveSequenceLabels(DetailEntityType type) => type switch
    {
        DetailEntityType.TvEpisode or DetailEntityType.TvSeason or DetailEntityType.TvShow =>
            new("Show", "Episode", "Episodes", "Season"),
        DetailEntityType.ComicIssue or DetailEntityType.ComicSeries =>
            new("Volume", "Issue", "Issues", null),
        DetailEntityType.Movie or DetailEntityType.MovieSeries =>
            new("Movie Series", "Movie", "Movies", null),
        DetailEntityType.Audiobook =>
            new("Series", "Audiobook", "Audiobooks", null),
        _ => new("Series", "Book", "Books", null),
    };

    private static string? ResolveSequencePositionLabel(DetailEntityType type, string? positionLabel, string? episodeLabel)
        => type == DetailEntityType.TvEpisode
            ? FirstText(episodeLabel, positionLabel)
            : positionLabel;

    private static (string? Key, string? Title) ResolveSequenceGroup(DetailEntityType type, string? rawGroup)
    {
        if (type is not (DetailEntityType.TvEpisode or DetailEntityType.TvSeason or DetailEntityType.TvShow))
        {
            return (null, null);
        }

        var value = FirstText(rawGroup, "1")!;
        var normalized = NormalizeSequenceOrdinal(value);
        return ($"season-{normalized}", $"Season {normalized}");
    }

    private static string? FormatSequencePositionText(DetailEntityType type, string? rawPosition, int? position)
    {
        var value = position?.ToString(CultureInfo.InvariantCulture) ?? NormalizeSequenceOrdinal(rawPosition);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return type == DetailEntityType.TvEpisode ? $"E{value}" : value;
    }

    private static string NormalizeSequenceOrdinal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return int.TryParse(trimmed.TrimStart('0'), out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : trimmed;
    }

    private static List<SequenceItemViewModel> NormalizeSequenceItems(IEnumerable<SequenceItemViewModel> items, DetailEntityType entityType)
        => items.Select(item =>
        {
            var positionText = item.PositionText ?? FormatSequencePositionText(entityType, item.PositionLabel, item.PositionNumber);
            var group = string.IsNullOrWhiteSpace(item.GroupKey) && string.IsNullOrWhiteSpace(item.GroupTitle)
                ? ResolveSequenceGroup(entityType, null)
                : (item.GroupKey, item.GroupTitle);
            return new SequenceItemViewModel
            {
                Id = item.Id,
                EntityType = item.EntityType,
                Title = item.Title,
                ArtworkUrl = item.ArtworkUrl,
                PublicationDate = item.PublicationDate,
                PositionNumber = item.PositionNumber,
                PositionSort = item.PositionSort,
                PositionLabel = item.PositionLabel,
                PositionText = positionText,
                GroupKey = group.Item1,
                GroupTitle = group.Item2,
                MembershipScope = item.MembershipScope,
                IsCurrent = item.IsCurrent,
                IsOwned = item.IsOwned,
                ProgressState = item.ProgressState,
            };
        }).ToList();

    private static IReadOnlyList<SequenceGroupViewModel> BuildSequenceGroups(
        IReadOnlyList<SequenceItemViewModel> items,
        string fallbackTitle,
        int? mainSequenceExpectedTotal = null)
    {
        var grouped = items
            .GroupBy(item => item.GroupKey ?? "all", StringComparer.OrdinalIgnoreCase)
            .Select(group => new SequenceGroupViewModel
            {
                Key = group.Key,
                Title = group.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.GroupTitle))?.GroupTitle ?? fallbackTitle,
                TotalKnownItems = string.Equals(group.Key, "main-sequence", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(group.Key, "all", StringComparison.OrdinalIgnoreCase)
                    ? Math.Max(group.Count(), mainSequenceExpectedTotal ?? 0)
                    : group.Count(),
                Items = group.ToList(),
            })
            .OrderBy(group => SequenceGroupSort(group.Key))
            .ThenBy(group => TryParseSeriesPosition(group.Key.Replace("season-", string.Empty, StringComparison.OrdinalIgnoreCase)) ?? int.MaxValue)
            .ThenBy(group => group.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return grouped.Count == 0
            ? [new SequenceGroupViewModel
            {
                Key = "all",
                Title = fallbackTitle,
                TotalKnownItems = Math.Max(items.Count, mainSequenceExpectedTotal ?? 0),
                Items = items,
            }]
            : grouped;
    }

    private static int SequenceGroupSort(string key)
        => key switch
        {
            "main-sequence" => 0,
            "supplementary" => 1,
            "collected-content" => 2,
            "broader-context" => 3,
            "unpositioned" => 1,
            _ => 4,
        };

    private static string? BuildSequencePositionSummary(
        DetailEntityType type,
        SequenceItemViewModel current,
        string containerTitle,
        SequenceLabels labels)
    {
        if (type == DetailEntityType.TvEpisode)
        {
            return SeriesDisplayFormatter.FormatEpisodePosition(
                current.GroupTitle?.Replace("Season ", string.Empty, StringComparison.OrdinalIgnoreCase),
                FirstText(current.PositionText, current.PositionLabel, current.PositionNumber?.ToString(CultureInfo.InvariantCulture)),
                containerTitle);
        }

        var position = FirstText(current.PositionText, current.PositionLabel, current.PositionNumber?.ToString(CultureInfo.InvariantCulture));
        return SeriesDisplayFormatter.FormatPosition(labels.ItemLabel, position, containerTitle);
    }

    private static string? FormatSeasonEpisode(string? season, string? episode)
    {
        if (string.IsNullOrWhiteSpace(season) && string.IsNullOrWhiteSpace(episode))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(season))
        {
            return $"Episode {episode}";
        }

        if (string.IsNullOrWhiteSpace(episode))
        {
            return $"Season {season}";
        }

        return $"S{season} E{episode}";
    }

    private static string? FormatIssue(string? position)
        => string.IsNullOrWhiteSpace(position) ? null : $"Issue #{position}";

    private static DetailEntityType MapMediaTypeToEntityType(string? mediaType)
    {
        if (mediaType?.Contains("movie", StringComparison.OrdinalIgnoreCase) == true)
        {
            return DetailEntityType.Movie;
        }

        if (mediaType?.Contains("tv", StringComparison.OrdinalIgnoreCase) == true)
        {
            return DetailEntityType.TvEpisode;
        }

        if (mediaType?.Contains("music", StringComparison.OrdinalIgnoreCase) == true)
        {
            return DetailEntityType.MusicTrack;
        }

        if (mediaType?.Contains("audio", StringComparison.OrdinalIgnoreCase) == true)
        {
            return DetailEntityType.Audiobook;
        }

        if (mediaType?.Contains("comic", StringComparison.OrdinalIgnoreCase) == true)
        {
            return DetailEntityType.ComicIssue;
        }

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
        {
            return "Music";
        }

        if (context == DetailPresentationContext.Watch && (mediaType?.Contains("movie", StringComparison.OrdinalIgnoreCase) == true || mediaType?.Contains("tv", StringComparison.OrdinalIgnoreCase) == true))
        {
            return "Movies & TV";
        }

        if (mediaType?.Contains("audio", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Audiobooks";
        }

        if (mediaType?.Contains("book", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Books";
        }

        if (mediaType?.Contains("music", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Music";
        }

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
        DetailEntityType.ComicSeries => "Comic Volume",
        DetailEntityType.MusicAlbum => "Album",
        DetailEntityType.MusicArtist => "Artist",
        _ => entityType.ToString(),
    };

    private static string ToTabLabel(string key) => key switch
    {
            "people" => "Cast",
            "media" => "Media in Library",
            "sequence" => "Order",
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
        {
            return null;
        }

        var normalized = string.Join(
            ' ',
            value.Replace("\r", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return normalized.Length <= 260 ? normalized : normalized[..260].TrimEnd() + "...";
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

        if (entityType is DetailEntityType.BookSeries or DetailEntityType.ComicSeries or DetailEntityType.MovieSeries)
        {
            var structuralTitle = FirstNonBlank(
                GetValue(rootValues, MetadataFieldConstants.Series),
                GetValue(values, MetadataFieldConstants.Series),
                displayName,
                GetValue(values, MetadataFieldConstants.Title),
                FormatEntityType(entityType));
            return SeriesDisplayFormatter.NormalizeContainerTitle(structuralTitle, isStructuralSeries: true)
                ?? structuralTitle;
        }

        return SeriesDisplayFormatter.NormalizeContainerTitle(
                FirstNonBlank(displayName, GetValue(values, MetadataFieldConstants.Title), "Collection"),
                isStructuralSeries: false)
            ?? "Collection";
    }

    private static string? StripUniverseSuffix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

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
            {
                continue;
            }

            var key = NormalizeQid(entry.Qid) ?? entry.Name.Trim();
            if (seen.Add(key))
            {
                result.Add(entry with { Name = entry.Name.Trim(), Qid = NormalizeQid(entry.Qid), SortOrder = result.Count });
            }
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
        {
            return null;
        }

        var parsed = SplitCanonicalSegments(raw)
            .Select(ParseQidLabel)
            .Where(value => !string.IsNullOrWhiteSpace(value.Qid))
            .ToList();

        var byName = parsed.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value.Label)
            && value.Label.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(byName.Qid))
        {
            return byName.Qid;
        }

        return index >= 0 && index < parsed.Count ? parsed[index].Qid : null;
    }

    private static IReadOnlyList<string> SplitCanonicalSegments(string value)
    {
        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static (string? Qid, string? Label) ParseQidLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        var trimmed = value.Trim();
        var delimiter = trimmed.IndexOf("::", StringComparison.Ordinal);
        if (delimiter > 0)
        {
            return (NormalizeQid(trimmed[..delimiter]), FirstNonBlank(trimmed[(delimiter + 2)..], null));
        }

        return (NormalizeQid(trimmed), null);
    }

    private static string? NormalizeQid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var delimiter = trimmed.IndexOf("::", StringComparison.Ordinal);
        if (delimiter > 0)
        {
            trimmed = trimmed[..delimiter].Trim();
        }

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
        var parsed = TryParseSeriesPositionSort(value);
        return ToDisplayPositionNumber(parsed);
    }

    private static double? TryParseSeriesPositionSort(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
        {
            return parsedInt;
        }

        var numericText = new string(trimmed
            .SkipWhile(c => !char.IsDigit(c))
            .TakeWhile(c => char.IsDigit(c) || c is '.' or ',')
            .Select(c => c == ',' ? '.' : c)
            .ToArray());

        if (double.TryParse(numericText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedDouble))
        {
            return parsedDouble;
        }

        return null;
    }

    private static int? ToDisplayPositionNumber(double? value)
        => value.HasValue && IsWholeNumber(value.Value)
            ? Convert.ToInt32(Math.Round(value.Value, MidpointRounding.AwayFromZero))
            : null;

    private static bool IsWholeNumber(double value)
        => Math.Abs(value - Math.Round(value, MidpointRounding.AwayFromZero)) < 0.0001d;

    private static string? FormatSequenceSort(double? value)
        => value.HasValue
            ? value.Value.ToString(IsWholeNumber(value.Value) ? "0" : "0.###", CultureInfo.InvariantCulture)
            : null;

    private static string? SequencePositionKey(double? value)
        => value.HasValue
            ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
            : null;

    private static string? ExtractQid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var qid = value.Trim();
        if (qid.Contains("::", StringComparison.Ordinal))
        {
            qid = qid.Split("::", 2, StringSplitOptions.TrimEntries)[0];
        }

        if (qid.Contains('/', StringComparison.Ordinal))
        {
            qid = qid[(qid.LastIndexOf('/') + 1)..];
        }

        return string.IsNullOrWhiteSpace(qid) ? null : qid;
    }

    private static bool IsWikidataQid(string? value)
    {
        var qid = ExtractQid(value);
        return qid is { Length: > 1 }
            && qid[0] == 'Q'
            && qid.Skip(1).All(char.IsDigit);
    }

    private static string? NormalizeSequenceContainerId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return ExtractQid(value) ?? value.Trim();
    }

    private static bool SequenceContainerIdEquals(string? left, string? right)
    {
        var normalizedLeft = NormalizeSequenceContainerId(left);
        var normalizedRight = NormalizeSequenceContainerId(right);
        return !string.IsNullOrWhiteSpace(normalizedLeft)
            && !string.IsNullOrWhiteSpace(normalizedRight)
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FormatSequenceContainerTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var textInfo = CultureInfo.CurrentCulture.TextInfo;
        return string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Length == 0 || word.All(char.IsUpper)
                ? word
                : textInfo.ToTitleCase(word)));
    }

    private static string? StringValue(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        if (value is byte[] bytes)
        {
            return bytes.Length == 16
                ? GuidSql.FromDb(bytes).ToString("D")
                : Encoding.UTF8.GetString(bytes);
        }

        var text = Convert.ToString(value);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? ResolveCollectionArtworkUrl(string? value, string? assetIdValue, string kind, string? state)
    {
        if (!Guid.TryParse(assetIdValue, out var assetId))
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        // Collection and TV-show detail pages are composed from representative
        // child works. Their downloaded artwork is stored on the child asset, so
        // route the same local image stream URLs used by work/movie detail pages.
        return DisplayArtworkUrlResolver.Resolve(value, assetId, kind, state);
    }

    private static int? IntValue(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

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
        {
            return null;
        }

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
    private sealed record DescriptionSelection(string? Text, string? SourceKey, bool IsGeneratedFallback);
    private sealed record OwnedFormatRow(Guid EditionId, string? FormatLabel, Guid AssetId, string FilePathRoot, string? AssetCoverUrl, string? EditionCoverUrl, string? Runtime, string? PageCount, string? Narrator, double? ProgressPct);
    private sealed record CollectionDetailRow(Guid Id, string? DisplayName, string? WikidataQid, string? Description, string? Tagline, string? CoverUrl, string? BackgroundUrl, string? BannerUrl, string? LogoUrl, string? HeroBrandLabel, string? HeroBrandImageUrl);
    private sealed record SequenceLabels(string ContainerLabel, string ItemLabel, string ItemPluralLabel, string? GroupLabel);
    private sealed class SequenceContainerMetadataDbRow
    {
        public object? Description { get; init; }
        public object? WikipediaUrl { get; init; }
    }

    private sealed record SequenceContainerMetadataRow(string? Description, string? WikipediaUrl);

    private sealed class SequenceRow
    {
        public Guid WorkId { get; init; }
        public string Title { get; init; } = "Untitled";
        public string? MediaType { get; init; }
        public string? PositionLabel { get; init; }
        public double? PositionSort { get; init; }
        public string? SeasonLabel { get; init; }
        public string? EpisodeLabel { get; init; }
        public string? ArtworkUrl { get; init; }
        public string? PublicationDate { get; init; }
    }

    private sealed class SeriesManifestItemRow
    {
        public Guid Id { get; set; }
        public Guid CollectionId { get; set; }
        public string SeriesQid { get; set; } = string.Empty;
        public string ItemQid { get; set; } = string.Empty;
        public string? ItemLabel { get; set; }
        public string? ItemDescription { get; set; }
        public string? MediaType { get; set; }
        public string? MediaKind { get; set; }
        public string InstanceOfQidsJson { get; set; } = "[]";
        public string? RawOrdinal { get; set; }
        public double? ParsedOrdinal { get; set; }
        public string? OrdinalScopeQid { get; set; }
        public double? SortOrder { get; set; }
        public string? PublicationDate { get; set; }
        public string? PreviousQid { get; set; }
        public string? NextQid { get; set; }
        public string? ParentCollectionQid { get; set; }
        public string? ParentCollectionLabel { get; set; }
        public int IsCollection { get; set; }
        public int IsExpandedFromCollection { get; set; }
        public string MembershipScope { get; set; } = SeriesMembershipScopeNames.MainSequence;
        public string SourcePropertiesJson { get; set; } = "[]";
        public string RelationshipsJson { get; set; } = "[]";
        public string OrderSource { get; set; } = "Unknown";
        public string OwnershipState { get; set; } = "Missing";
        public Guid? LinkedWorkId { get; set; }
        public string LastHydratedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
        public string CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
        public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");

        public SeriesManifestItemRecord ToEntity() => new()
        {
            Id = Id,
            CollectionId = CollectionId,
            SeriesQid = SeriesQid,
            ItemQid = ItemQid,
            ItemLabel = ItemLabel,
            ItemDescription = ItemDescription,
            MediaType = MediaType,
            MediaKind = MediaKind,
            InstanceOfQidsJson = InstanceOfQidsJson,
            RawOrdinal = RawOrdinal,
            ParsedOrdinal = ParsedOrdinal,
            OrdinalScopeQid = OrdinalScopeQid,
            SortOrder = SortOrder,
            PublicationDate = PublicationDate,
            PreviousQid = PreviousQid,
            NextQid = NextQid,
            ParentCollectionQid = ParentCollectionQid,
            ParentCollectionLabel = ParentCollectionLabel,
            IsCollection = IsCollection == 1,
            IsExpandedFromCollection = IsExpandedFromCollection == 1,
            MembershipScope = MembershipScope,
            SourcePropertiesJson = SourcePropertiesJson,
            RelationshipsJson = RelationshipsJson,
            OrderSource = OrderSource,
            OwnershipState = OwnershipState,
            LinkedWorkId = LinkedWorkId,
            LastHydratedAt = DateTimeOffset.Parse(LastHydratedAt, CultureInfo.InvariantCulture),
            CreatedAt = DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(UpdatedAt, CultureInfo.InvariantCulture),
        };
    }

    private sealed record CollectionWorkSummary(string Id, string MediaType, int? Ordinal, string Title, string? Description, string? Season, string? Episode, string? TrackNumber, string? Duration, string? Year, string? Artist, bool IsExplicit, string? Quality, double? ProgressPercent, bool HasAsset, string? Ownership, bool IsCatalogOnly, string? ArtworkUrl, string? BackgroundUrl)
    {
        public bool IsOwned =>
            HasAsset
            && !IsCatalogOnly
            && !string.Equals(Ownership, "Unowned", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Ownership, "Missing", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record AudiobookAssetRow
    {
        public Guid WorkId { get; init; }
        public Guid AssetId { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? Author { get; init; }
        public string? Narrator { get; init; }
        public string? DurationSecondsValue { get; init; }
        public string? Duration { get; init; }
    }

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
    private sealed class AudiobookResumeRow
    {
        public int SourceRank { get; init; }
        public double? PositionSeconds { get; init; }
        public double? DurationSeconds { get; init; }
        public double? ProgressPct { get; init; }
        public string? LastAccessed { get; init; }
        public string? ExtendedProperties { get; init; }
    }

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
