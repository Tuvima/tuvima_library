using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using MediaEngine.Api.Endpoints;
using MediaEngine.Api.Models;
using MediaEngine.Api.Services.Display;
using MediaEngine.Api.Services.Playback;
using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Contracts.Collections;
using SeriesManifestViewDto = MediaEngine.Domain.Models.SeriesManifestViewDto;
using SeriesManifestItemDto = MediaEngine.Domain.Models.SeriesManifestItemDto;
using MediaEngine.Contracts.Details;
using MediaEngine.Contracts.Persons;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;
using static MediaEngine.Api.Services.Details.Internals.DetailPresentationPolicy;
using static MediaEngine.Api.Services.Details.Internals.DetailViewModelBuilder;

namespace MediaEngine.Api.Services.Details.Internals;

internal sealed partial class DetailCompositionOrchestrator
{
    private async Task<DetailPageViewModel?> BuildCollectionAsync(
        Guid collectionId,
        DetailEntityType entityType,
        DetailPresentationContext context,
        bool isAdminView,
        IReadOnlySet<Guid> favoriteWorkIds,
        CancellationToken ct,
        Guid? currentWorkId = null,
        Guid? profileId = null)
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
        var musicAlbumGroup = !hasCollectionRow && entityType == DetailEntityType.MusicAlbum
            ? await LoadMusicAlbumSystemViewGroupAsync(collectionId, ct)
            : null;
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
                : musicAlbumGroup is not null
                    ? new CollectionDetailRow(
                        collectionId,
                        musicAlbumGroup.DisplayName,
                        musicAlbumGroup.WikidataQid,
                        musicAlbumGroup.Description,
                        musicAlbumGroup.Tagline,
                        musicAlbumGroup.CoverUrl,
                        null,
                        null,
                        null,
                        null,
                        null)
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
            : entityType is DetailEntityType.TvShow or DetailEntityType.MusicAlbum
                ? collectionId
                : null;
        IReadOnlyList<Guid> resolvedCollectionWorkIds = [];
        if (hasCollectionRow
            && entityType == DetailEntityType.Collection
            && _collectionCatalog is not null)
        {
            Profile? activeProfile = profileId.HasValue && _profiles is not null
                ? await _profiles.GetByIdAsync(profileId.Value, ct)
                : null;
            var resolvedItems = await _collectionCatalog.GetItemsAsync(
                collectionId,
                activeProfile,
                int.MaxValue,
                ct);
            resolvedCollectionWorkIds = resolvedItems.Found && !resolvedItems.Forbidden
                ? resolvedItems.Items.Select(item => item.WorkId).Distinct().ToList()
                : [];
        }

        var ownedWorks = musicAlbumGroup is not null
            ? await LoadMusicAlbumSystemViewWorksAsync(musicAlbumGroup, ct)
            : await LoadCollectionWorksAsync(collectionId, rootWorkId, ct, resolvedCollectionWorkIds);
        if (!hasCollectionRow
            && entityType is (DetailEntityType.TvShow or DetailEntityType.MusicAlbum)
            && ownedWorks.Count == 0)
        {
            return null;
        }
        var rootValues = rootWorkId.HasValue
            ? await LoadCanonicalMapAsync(rootWorkId.Value, ct)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var values = MergeCanonicalMaps(collectionValues, rootValues);
        var works = entityType == DetailEntityType.MusicAlbum
            ? MergeMusicAlbumManifestTracks(ownedWorks, values, row.CoverUrl)
            : ownedWorks;

        var tvInProgressEpisode = entityType == DetailEntityType.TvShow
            ? SelectInProgressTvEpisode(works)
            : null;
        var tvPlaybackEpisode = tvInProgressEpisode ?? (entityType == DetailEntityType.TvShow
            ? SelectFirstOwnedTvEpisode(works)
            : null);
        var tvPlaybackEpisodeId = tvPlaybackEpisode is not null && Guid.TryParse(tvPlaybackEpisode.Id, out var parsedEpisodeId)
            ? parsedEpisodeId
            : (Guid?)null;
        var tvPlaybackValues = tvPlaybackEpisodeId.HasValue
            ? await LoadWorkAndAssetCanonicalMapAsync(tvPlaybackEpisodeId.Value, ct)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var relatedArt = works
            .SelectMany(w => new[] { w.BackgroundUrl, w.ArtworkUrl })
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        var longDescription = entityType == DetailEntityType.TvShow
            ? FirstText(
                GetValue(values, "wikipedia_extract"),
                GetValue(values, MetadataFieldConstants.Description),
                GetValue(values, "overview"),
                GetValue(values, "plot_summary"),
                row.Description)
            : FirstText(
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
        var collectionBackdrop = IsStructuralContainer(entityType)
            ? null
            : StringHelpers.FirstNonBlankOr(string.Empty,
                tvInProgressEpisode?.BackgroundUrl,
                row.BackgroundUrl,
                GetValue(values, "background_url"),
                GetValue(values, "background"),
                GetValue(values, "hero_url"),
                GetValue(values, "hero"),
                fallbackBackdrop);
        var collectionBanner = StringHelpers.FirstNonBlankOr(string.Empty,
            row.BannerUrl,
            GetValue(values, "banner_url"),
            GetValue(values, "banner"));
        var collectionCover = IsStructuralContainer(entityType)
            ? null
            : StringHelpers.FirstNonBlankOr(string.Empty,
                row.CoverUrl,
                GetValue(values, "cover_url"),
                GetValue(values, "cover"),
                GetValue(values, "poster_url"),
                GetValue(values, "poster"),
                fallbackCover);
        var collectionLogo = StringHelpers.FirstNonBlankOr(string.Empty, row.LogoUrl, GetValue(values, "logo_url"), GetValue(values, "logo"));
        IReadOnlyList<CreditGroupViewModel> contributorGroups = entityType == DetailEntityType.Collection
            ? []
            : await BuildCollectionCreditsAsync(collectionId, rootWorkId, works, entityType, values, ct);
        var musicAlbumCompanion = entityType == DetailEntityType.MusicAlbum
            ? await BuildMusicAlbumCompanionAsync(
                rootWorkId ?? collectionId,
                contributorGroups,
                ct)
            : null;
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
            entityType == DetailEntityType.TvShow ? heroSummary : longDescription,
            displayWorks,
            expectedTotal,
            manifest?.AuthoritativeTotalsByContainer,
            currentWorkId ?? tvPlaybackEpisodeId);
        var mediaGroups = entityType == DetailEntityType.TvShow
            ? []
            : BuildCollectionMediaGroups(entityType, displayWorks, favoriteWorkIds, expectedTotal);

        return new DetailPageViewModel
        {
            Id = collectionId.ToString("D"),
            EntityType = entityType,
            PresentationContext = context,
            EditorTarget = BuildCollectionEditorTarget(collectionId, entityType, rootWorkId),
            Title = collectionTitle,
            Subtitle = BuildCollectionSubtitle(entityType, displayWorks, values),
            Tagline = entityType == DetailEntityType.TvShow
                ? tvPlaybackEpisode?.Description
                : heroSummary,
            Description = longDescription,
            DescriptionAttribution = BuildWikipediaDescriptionAttribution(longDescription, GetValue(values, "wikipedia_url")),
            SourceLinks = BuildExternalSourceLinks(row.WikidataQid, GetValue(values, "wikipedia_url"), null, values),
            Facts = BuildCollectionFacts(entityType, displayWorks, values, contributorGroups, row.WikidataQid),
            Artwork = artwork,
            HeroBrand = BuildHeroBrand(
                entityType,
                StringHelpers.FirstNonBlankOr(string.Empty, row.HeroBrandLabel, GetValue(values, "network"), GetValue(values, "studio"), GetValue(values, "broadcaster")),
                StringHelpers.FirstNonBlankOr(string.Empty, row.HeroBrandImageUrl, GetValue(values, "network_logo_url"), GetValue(values, "network_logo"), GetValue(values, "studio_logo_url"), GetValue(values, "broadcaster_logo_url"))),
            Progress = heroProgress,
            Metadata = BuildCollectionMetadata(entityType, displayWorks, values, tvPlaybackEpisode, tvPlaybackValues),
            PrimaryActions = BuildCollectionActions(collectionId, entityType, context, heroProgress, displayWorks),
            SecondaryActions = BuildSecondaryActions(rootWorkId ?? collectionId, entityType, rootWorkId.HasValue && favoriteWorkIds.Contains(rootWorkId.Value)),
            OverflowActions = BuildOverflowActions(collectionId, entityType, isAdminView),
            SequencePlacement = sequencePlacement,
            ContributorGroups = contributorGroups,
            PreviewContributors = BuildPreviewContributors(entityType, contributorGroups),
            CharacterGroups = characterGroups,
            PreviewCharacters = characterGroups.SelectMany(g => g.Characters).Take(12).ToList(),
            RelationshipStrip = relationships,
            Tabs = BuildTabs(entityType, context, isAdminView, hasUniverse: HasUniverseRelationship(relationships)),
            MediaGroups = mediaGroups,
            PrimaryModule = BuildPrimaryModule(entityType, sequencePlacement, mediaGroups),
            MusicAlbumCompanion = musicAlbumCompanion,
            IdentityStatus = ResolveIdentityStatus(row.WikidataQid, null, null),
            LibraryStatus = LibraryStatus.Owned,
            IsAdminView = isAdminView,
        };
    }

    private async Task<ContentGroupDto?> LoadMusicAlbumSystemViewGroupAsync(
        Guid rootWorkId,
        CancellationToken ct)
    {
        if (_collectionBrowse is null)
        {
            return null;
        }

        var groups = await _collectionBrowse
            .GetSystemViewGroupsAsync("Music", "album", ct)
            .ConfigureAwait(false);
        return groups.FirstOrDefault(group => group.RootWorkId == rootWorkId);
    }

    private async Task<IReadOnlyList<CollectionWorkSummary>> LoadMusicAlbumSystemViewWorksAsync(
        ContentGroupDto group,
        CancellationToken ct)
    {
        if (_collectionBrowse is null)
        {
            return [];
        }

        var rows = await _collectionBrowse.GetSystemViewDetailWorksAsync(
            "album",
            group.DisplayName,
            "Music",
            group.Creator,
            ct);
        return SortMusicAlbumTracks(rows.Select((row, index) =>
        {
            var durationSource = StringHelpers.FirstNonBlankOr(string.Empty, row.DurationSecondsValue, row.Duration, row.Runtime);
            var duration = FormatSecondsDuration(ParseDurationSeconds(durationSource))
                ?? FormatTrackDuration(durationSource);
            var assetId = row.AssetId?.ToString("D");
            return new CollectionWorkSummary(
                row.WorkId.ToString("D"),
                "Music",
                ResolveOwnedTrackOrdinal(row.TrackNumber, index),
                StringHelpers.FirstNonBlankOr(string.Empty, row.EpisodeTitle, row.Title, $"Track {index + 1}"),
                null,
                null,
                null,
                row.TrackNumber,
                TryParseInt(row.DiscNumber),
                duration,
                StringHelpers.FirstNonBlankOr(string.Empty, row.ReleaseYear, row.YearValue),
                FormatContributorList(StringHelpers.FirstNonBlankOr(string.Empty, row.Artist, row.Author)),
                false,
                null,
                null,
                row.AssetId.HasValue,
                "Owned",
                false,
                row.AssetId.HasValue ? $"/stream/{row.AssetId.Value:D}/cover" : row.Cover,
                row.Background,
                assetId);
        }));
    }

    private async Task<MusicAlbumCompanionViewModel> BuildMusicAlbumCompanionAsync(
        Guid currentAlbumRootWorkId,
        IReadOnlyList<CreditGroupViewModel> contributorGroups,
        CancellationToken ct)
    {
        var primaryArtist = contributorGroups
            .Where(group => group.GroupType == CreditGroupType.PrimaryArtists)
            .SelectMany(group => group.Credits)
            .OrderByDescending(credit => credit.IsPrimary)
            .ThenBy(credit => credit.SortOrder)
            .FirstOrDefault();

        var primaryArtistId = Guid.Empty;
        var hasResolvedArtist = primaryArtist is not null
            && Guid.TryParse(primaryArtist.EntityId, out primaryArtistId);
        var companion = new MusicAlbumCompanionViewModel
        {
            PrimaryArtistId = hasResolvedArtist ? primaryArtistId.ToString("D") : null,
            PrimaryArtistName = primaryArtist?.DisplayName,
            PrimaryArtistRoute = hasResolvedArtist
                ? $"/details/person/{primaryArtistId:D}?context=listen"
                : null,
        };

        if (!hasResolvedArtist || _collectionBrowse is null)
        {
            return companion;
        }

        var ownedAlbumRootIds = await LoadOwnedMusicAlbumRootIdsForArtistAsync(primaryArtistId, ct);
        ownedAlbumRootIds.Remove(currentAlbumRootWorkId);
        if (ownedAlbumRootIds.Count == 0)
        {
            return companion;
        }

        var albumGroups = await _collectionBrowse
            .GetSystemViewGroupsAsync("Music", "album", ct)
            .ConfigureAwait(false);
        var moreByAlbums = albumGroups
            .Where(group => group.RootWorkId.HasValue
                && ownedAlbumRootIds.Contains(group.RootWorkId.Value))
            .GroupBy(group => group.RootWorkId!.Value)
            .Select(group => group
                .OrderByDescending(item => !string.IsNullOrWhiteSpace(item.CoverUrl))
                .ThenByDescending(item => item.WorkCount)
                .First())
            .OrderByDescending(group => TryParseInt(StringHelpers.FirstNonBlankOr(string.Empty,
                group.Year,
                group.LatestYear?.ToString(CultureInfo.InvariantCulture))))
            .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var rootWorkId = group.RootWorkId!.Value;
                var artworkUrl = StringHelpers.FirstNonBlankOr(string.Empty,
                    group.CoverUrl,
                    group.PreviewItems.FirstOrDefault()?.ImageUrl);
                var year = StringHelpers.FirstNonBlankOr(string.Empty,
                    group.Year,
                    group.EarliestYear?.ToString(CultureInfo.InvariantCulture));
                return new MusicAlbumPreviewViewModel
                {
                    Id = rootWorkId.ToString("D"),
                    Title = group.DisplayName,
                    Year = string.IsNullOrWhiteSpace(year) ? null : year,
                    ArtworkUrl = string.IsNullOrWhiteSpace(artworkUrl) ? null : artworkUrl,
                    Route = $"/details/musicalbum/{rootWorkId:D}?context=listen",
                };
            })
            .ToList();

        return new MusicAlbumCompanionViewModel
        {
            PrimaryArtistId = companion.PrimaryArtistId,
            PrimaryArtistName = companion.PrimaryArtistName,
            PrimaryArtistRoute = companion.PrimaryArtistRoute,
            MoreByAlbums = moreByAlbums,
        };
    }

    private async Task<HashSet<Guid>> LoadOwnedMusicAlbumRootIdsForArtistAsync(
        Guid personId,
        CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT DISTINCT COALESCE(p.id, w.id)
            FROM primary_person_media_credits credit
            INNER JOIN media_assets ma ON ma.id = credit.media_asset_id
            INNER JOIN editions e ON e.id = ma.edition_id
            INNER JOIN works w ON w.id = e.work_id
            LEFT JOIN works p ON p.id = w.parent_work_id
            WHERE credit.person_id = @personId
              AND credit.credit_key = 'artist'
              AND LOWER(w.media_type) = 'music';
            """,
            new { personId = GuidSql.ToBlob(personId) },
            cancellationToken: ct));

        return rows.ToHashSet();
    }

    private static int ResolveOwnedTrackOrdinal(string? trackNumber, int zeroBasedIndex)
    {
        var parsedTrackNumber = TryParseInt(trackNumber);
        return parsedTrackNumber.HasValue ? parsedTrackNumber.Value : zeroBasedIndex + 1;
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

}
