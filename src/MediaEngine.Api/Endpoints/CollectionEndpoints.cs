using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services;
using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Services;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Api.Endpoints;

public static class CollectionEndpoints
{
    public static IEndpointRouteBuilder MapCollectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/collections")
                       .WithTags("Collections");

        group.MapGet("/{collectionId:guid}/series-manifest", async (
            Guid collectionId,
            ISeriesManifestRepository manifestRepo,
            CancellationToken ct) =>
        {
            var manifest = await manifestRepo.GetViewByCollectionIdAsync(collectionId, ct);
            return manifest is null ? Results.NotFound() : Results.Ok(manifest);
        })
        .WithName("GetCollectionSeriesManifest")
        .WithSummary("Returns a Wikidata-backed ordered series manifest with owned and missing item states.")
        .Produces<SeriesManifestViewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/", async (
            ICollectionBrowseReadService browseReadService,
            CancellationToken ct) =>
        {
            var collections = await browseReadService.GetAllAsync(ct);
            return Results.Ok(collections);
        })
        .WithName("GetAllCollections")
        .WithSummary("List all media collections with their works and canonical metadata values.")
        .Produces<List<CollectionDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapGet("/search", async (
            string? q,
            ICollectionSearchReadService searchReadService,
            CancellationToken ct) =>
        {
            var results = await searchReadService.SearchAsync(q, ct);
            return Results.Ok(results);
        })
        .WithName("SearchCollections")
        .WithSummary("Full-text search across all works. Returns up to 20 matching results.")
        .Produces<List<SearchResultDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();


        // GET /collections/{id}/related?limit= — cascading related collections: series → author → genre → explore.
        // GET /collections/parents — list all Parent Collections for top-level franchise navigation.
        // IMPORTANT: registered before /{id:guid} routes to avoid route conflicts.
        group.MapGet("/parents", async (ICollectionRepository collectionRepo, CancellationToken ct) =>
        {
            var allCollections = await collectionRepo.GetAllAsync(ct);

            var parentIds = allCollections
                .Where(h => h.ParentCollectionId.HasValue)
                .Select(h => h.ParentCollectionId!.Value)
                .Distinct()
                .ToHashSet();

            var parents = allCollections
                .Where(h => parentIds.Contains(h.Id))
                .Select(h =>
                {
                    var children = allCollections.Where(c => c.ParentCollectionId == h.Id).ToList();
                    // Aggregate media types across all works in child collections
                    var mediaTypes = children
                        .SelectMany(c => c.Works)
                        .Select(w => w.MediaType.ToString())
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(t => t)
                        .ToList();

                    return new ParentCollectionDto
                    {
                        Id = h.Id,
                        UniverseId = h.UniverseId,
                        DisplayName = h.DisplayName,
                        Description = h.Description,
                        WikidataQid = h.WikidataQid,
                        ParentCollectionId = null,
                        UniverseStatus = h.UniverseStatus,
                        CreatedAt = h.CreatedAt,
                        ChildCollectionCount = children.Count,
                        MediaTypes = string.Join(", ", mediaTypes),
                        TotalWorks = children.Sum(c => c.Works.Count),
                    };
                })
                .OrderBy(h => h.DisplayName)
                .ToList();

            return Results.Ok(parents);
        })
        .WithName("GetParentCollections")
        .WithSummary("Returns all Parent Collections (franchise-level groupings).")
        .Produces<List<ParentCollectionDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // GET /collections/{id}/children — returns child Collections of a given parent.
        group.MapGet("/{id:guid}/children", async (Guid id, ICollectionRepository collectionRepo, CancellationToken ct) =>
        {
            var children = await collectionRepo.GetChildCollectionsAsync(id, ct);
            var result = children.Select(h => new
            {
                id = h.Id,
                displayName = h.DisplayName,
                parentCollectionId = h.ParentCollectionId,
                createdAt = h.CreatedAt,
                universeStatus = h.UniverseStatus,
            }).ToList();

            return Results.Ok(result);
        })
        .WithName("GetCollectionChildren")
        .WithSummary("Returns child Collections of the given Parent Collection.")
        .RequireAnyRole();

        // GET /collections/{id}/parent — returns the parent Collection of a given Collection (if any).
        group.MapGet("/{id:guid}/parent", async (Guid id, ICollectionRepository collectionRepo, CancellationToken ct) =>
        {
            var collection = await collectionRepo.GetByIdAsync(id, ct);
            if (collection is null)
            {
                return Results.NotFound();
            }

            if (!collection.ParentCollectionId.HasValue)
            {
                return Results.Ok(new { parentCollection = (object?)null });
            }

            var parent = await collectionRepo.GetByIdAsync(collection.ParentCollectionId.Value, ct);
            if (parent is null)
            {
                return Results.Ok(new { parentCollection = (object?)null });
            }

            return Results.Ok(new
            {
                parentCollection = new
                {
                    id = parent.Id,
                    displayName = parent.DisplayName,
                    createdAt = parent.CreatedAt,
                    universeStatus = parent.UniverseStatus,
                }
            });
        })
        .WithName("GetCollectionParent")
        .WithSummary("Returns the Parent Collection of the given Collection, if any.")
        .RequireAnyRole();

        group.MapGet("/{id:guid}/related", async (
            Guid id,
            int? limit,
            ICollectionRepository collectionRepo,
            CancellationToken ct) =>
        {
            var allCollections = await collectionRepo.GetAllAsync(ct);
            var dtos = allCollections.Select(CollectionDto.FromDomain).ToList();

            var target = dtos.FirstOrDefault(h => h.Id == id);
            if (target is null)
            {
                return Results.NotFound($"Collection '{id}' not found.");
            }

            int take = limit is > 0 ? limit.Value : 20;

            var targetSeries = GetCanonical(target.Works.FirstOrDefault(), "series");
            var targetAuthor = GetCanonical(target.Works.FirstOrDefault(), "author");
            var targetGenre = GetCanonical(target.Works.FirstOrDefault(), "genre");

            var result = new List<CollectionDto>();
            var seen = new HashSet<Guid> { id };
            string reason = string.Empty;
            string title = string.Empty;

            // Stage 1: same series
            if (!string.IsNullOrWhiteSpace(targetSeries))
            {
                var matches = dtos
                    .Where(h => !seen.Contains(h.Id) &&
                           string.Equals(GetCanonical(h.Works.FirstOrDefault(), "series"),
                               targetSeries, StringComparison.OrdinalIgnoreCase))
                    .Take(take)
                    .ToList();
                if (matches.Count > 0)
                {
                    result.AddRange(matches);
                    matches.ForEach(h => seen.Add(h.Id));
                    reason = "Same Series";
                    title = $"More in {targetSeries}";
                }
            }

            // Stage 2: same author
            if (result.Count < take && !string.IsNullOrWhiteSpace(targetAuthor))
            {
                var matches = dtos
                    .Where(h => !seen.Contains(h.Id) &&
                           string.Equals(GetCanonical(h.Works.FirstOrDefault(), "author"),
                               targetAuthor, StringComparison.OrdinalIgnoreCase))
                    .Take(take - result.Count)
                    .ToList();
                if (matches.Count > 0)
                {
                    if (result.Count == 0) { reason = "Same Creator"; title = $"More by {targetAuthor}"; }
                    result.AddRange(matches);
                    matches.ForEach(h => seen.Add(h.Id));
                }
            }

            // Stage 3: same genre
            if (result.Count < take && !string.IsNullOrWhiteSpace(targetGenre))
            {
                var targetGenreFirst = targetGenre.Split(',', ';')[0].Trim();
                var matches = dtos
                    .Where(h => !seen.Contains(h.Id) &&
                           (GetCanonical(h.Works.FirstOrDefault(), "genre") ?? string.Empty)
                               .Contains(targetGenreFirst, StringComparison.OrdinalIgnoreCase))
                    .Take(take - result.Count)
                    .ToList();
                if (matches.Count > 0)
                {
                    if (result.Count == 0) { reason = "Shared Metadata"; title = $"More {targetGenreFirst}"; }
                    result.AddRange(matches);
                    matches.ForEach(h => seen.Add(h.Id));
                }
            }

            if (result.Count == 0)
            {
                title = "Related media";
            }

            return Results.Ok(new RelatedCollectionsResponse
            {
                SectionTitle = title,
                Reason = reason,
                Collections = result,
            });
        })
        .WithName("GetRelatedCollections")
        .WithSummary("Related collections via cascade: series → author → genre → explore.")
        .Produces<RelatedCollectionsResponse>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // ── Group Detail ─────────────────────────────────────────────────────────

        // GET /collections/{collectionId}/group-detail — collection header + child works for sub-page rendering.
        group.MapGet("/{collectionId:guid}/group-detail", async (
            Guid collectionId,
            ICollectionRepository collectionRepo,
            ICanonicalValueRepository canonicalRepo,
            ICanonicalValueArrayRepository canonicalArrayRepo,
            IPersonRepository personRepo,
            IPersonCreditReadService personCreditReadService,
            AppleRetailClient appleRetailClient,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var collection = await collectionRepo.GetCollectionWithWorksAsync(collectionId, ct);
            if (collection is null)
            {
                return Results.NotFound();
            }

            // Determine primary media type from the works.
            var primaryMediaType = collection.Works
                .GroupBy(w => w.MediaType.ToString())
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key;

            bool isTv = string.Equals(primaryMediaType, "TV", StringComparison.OrdinalIgnoreCase);
            bool isMusic = string.Equals(primaryMediaType, "Music", StringComparison.OrdinalIgnoreCase);

            // Phase 4 — resolve the topmost Work id for the collection by walking the
            // parent_work_id chain from any of the collection's works (they all share
            // the same root parent in a ContentGroup collection). Parent-scope canonical
            // values (author, cover, genre, network, year) live on this row.
            Guid? rootParentWorkId = null;
            IReadOnlyList<CanonicalValue> parentCvs = [];
            if (collection.Works.Count > 0)
            {
                using var conn = db.CreateConnection();
                using var rootCmd = conn.CreateCommand();
                rootCmd.CommandText = """
                    SELECT COALESCE(gp.id, p.id, w.id)
                    FROM works w
                    LEFT JOIN works p  ON p.id  = w.parent_work_id
                    LEFT JOIN works gp ON gp.id = p.parent_work_id
                    WHERE w.id = @id
                    LIMIT 1
                    """;
                var idParam = rootCmd.CreateParameter();
                idParam.ParameterName = "@id";
                idParam.Value = GuidSql.ToBlob(collection.Works[0].Id);
                rootCmd.Parameters.Add(idParam);

                var rootIdObj = await rootCmd.ExecuteScalarAsync(ct);
                var rid = GuidSql.FromDbNullable(rootIdObj);
                if (rid.HasValue)
                {
                    rootParentWorkId = rid.Value;
                    parentCvs = await canonicalRepo.GetByEntityAsync(rid.Value, ct);
                }
            }

            string? ParentCv(string key) =>
                parentCvs.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

            var rootWorkQid = collection.WikidataQid ?? ParentCv(BridgeIdKeys.WikidataQid);
            var primaryAssetIds = await LoadPrimaryAssetIdsAsync(collection.Works.Select(w => w.Id), db, ct);

            // Build per-work DTOs.
            var workDtos = collection.Works
                .OrderBy(w => w.Ordinal ?? int.MaxValue)
                .ThenBy(w => w.Id)
                .Select(w =>
                {
                    var workDto = WorkDto.FromDomain(w);
                    string? title = (isTv ? GetCanonical(workDto, "episode_title") : null)
                                         ?? GetCanonical(workDto, "title")
                                         ?? $"Work {w.Id.ToString("N")[..8]}";
                    string? year = GetCanonical(workDto, "release_year")
                                         ?? GetCanonical(workDto, "year");
                    string? duration = GetCanonical(workDto, "duration_seconds")
                                         ?? GetCanonical(workDto, "duration_sec")
                                         ?? GetCanonical(workDto, "duration")
                                         ?? GetCanonical(workDto, "runtime");
                    var durationSeconds = isMusic ? NormalizeAudioDurationSeconds(duration) : null;
                    var displayDuration = isMusic ? FormatAudioDuration(durationSeconds, duration) : duration;
                    string? coverUrl = BuildCoverStreamUrl(w);
                    string? backgroundUrl = BuildBackgroundStreamUrl(w);
                    string? bannerUrl = BuildBannerStreamUrl(w);
                    string? season = GetCanonical(workDto, "season_number");
                    string? episode = GetCanonical(workDto, "episode_number");
                    string? trackNumber = GetCanonical(workDto, "track_number");
                    string? discNumber = GetCanonical(workDto, "disc_number");
                    string? appleMusicId = GetCanonical(workDto, BridgeIdKeys.AppleMusicId);
                    string? description = GetCanonical(workDto, "description");
                    string? director = GetCanonical(workDto, "director");
                    string? writer = GetCanonical(workDto, "writer");
                    string? releaseDate = NormalizeReleaseDate(
                        GetCanonical(workDto, "release_date")
                        ?? GetCanonical(workDto, "date")
                        ?? GetCanonical(workDto, "year"));

                    // Derive a display status from wikidata_status / match_level.
                    string status = w.WikidataStatus switch
                    {
                        "confirmed" => "Verified",
                        "skipped" => "Unlinked",
                        _ => "Provisional",
                    };

                    // Pipeline stage stubs — state is derived from match/wikidata status.
                    var stage1 = new LibraryPipelineStageDto
                    {
                        State = w.MatchLevel is "retail_only" or "work" or "edition" ? "done" : "pending",
                        Label = "Retail",
                    };
                    var stage2 = new LibraryPipelineStageDto
                    {
                        State = w.WikidataStatus == "confirmed" ? "done" : "pending",
                        Label = "Wikidata",
                    };
                    var stage3 = new LibraryPipelineStageDto
                    {
                        State = "pending",
                        Label = "Universe",
                    };

                    return new CollectionGroupWorkDto
                    {
                        WorkId = w.Id,
                        AssetId = primaryAssetIds.GetValueOrDefault(w.Id),
                        Title = title,
                        Ordinal = w.Ordinal,
                        Year = year,
                        Duration = displayDuration,
                        DurationSeconds = durationSeconds,
                        CoverUrl = coverUrl,
                        BackgroundUrl = backgroundUrl,
                        BannerUrl = bannerUrl,
                        HeroUrl = null,
                        WikidataQid = w.WikidataQid,
                        Season = season,
                        Episode = episode,
                        TrackNumber = trackNumber,
                        DiscNumber = ParseNullableInt(discNumber),
                        AppleMusicId = appleMusicId,
                        Status = status,
                        Description = description,
                        Director = director,
                        Writer = writer,
                        ReleaseDate = releaseDate,
                        PlaybackSummary = BuildPlaybackSummaryFromWork(workDto),
                        Stage1 = stage1,
                        Stage2 = stage2,
                        Stage3 = stage3,
                    };
                })
                .ToList();

            // Collection-level header canonical values come from the topmost Work row.
            // Phase 4 — parent-scoped fields (author, director, artist, genre, cover,
            // network) live on the root parent Work, not on individual child works.
            string? collectionCreator = ParentCv("author") ?? ParentCv("artist");
            string? collectionDirector = isTv ? null : ParentCv("director");
            string? collectionWriter = ParentCv("writer");
            string? collectionGenre = ParentCv("genre");
            string? collectionNetwork = isTv ? ParentCv("network") : null;
            string? collectionDescription = ParentCv("description");
            string? collectionTagline = ParentCv("tagline");
            string? collectionReleaseDate = NormalizeReleaseDate(
                ParentCv("release_date")
                ?? ParentCv("date")
                ?? ParentCv("year"));
            var collectionPalette = ResolvePalette(rootParentWorkId, parentCvs, db);

            // Resolve cover URL as a /stream/ endpoint. Cover art is downloaded
            // to disk by CoverArtWorker and served via StreamEndpoints. We need
            // the root parent work's asset_id to build the URL.
            string? collectionCover = null;
            string? collectionBackground = null;
            string? collectionBanner = null;
            if (rootParentWorkId.HasValue)
            {
                using var coverConn = db.CreateConnection();
                using var coverCmd = coverConn.CreateCommand();
                coverCmd.CommandText = """
                    SELECT MIN(ma.id)
                    FROM editions e
                    INNER JOIN media_assets ma ON ma.edition_id = e.id
                    WHERE e.work_id = @wid
                    """;
                var widParam = coverCmd.CreateParameter();
                widParam.ParameterName = "@wid";
                widParam.Value = GuidSql.ToBlob(rootParentWorkId.Value);
                coverCmd.Parameters.Add(widParam);
                var rootAssetObj = await coverCmd.ExecuteScalarAsync(ct);
                if (GuidSql.FromDbNullable(rootAssetObj) is { } rootAssetId)
                {
                    var rootAssetStr = rootAssetId.ToString("D");
                    collectionCover = $"/stream/{rootAssetStr}/cover";
                    collectionBackground = $"/stream/{rootAssetStr}/background";
                    collectionBanner = $"/stream/{rootAssetStr}/banner";
                }
            }

            // Year range from all works.
            var years = workDtos
                .Where(w => !string.IsNullOrWhiteSpace(w.Year))
                .Select(w => w.Year!)
                .Distinct()
                .OrderBy(y => y)
                .ToList();
            string? yearRange = years.Count switch
            {
                0 => null,
                1 => years[0],
                _ => $"{years[0]}–{years[^1]}",
            };

            // Build the response — TV uses seasons grouping, Music uses album grouping, others use flat works list.
            List<CollectionGroupSeasonDto> seasons = [];
            List<CollectionGroupWorkDto> flatWorks = [];
            var collectionChildJson = ParentCv(MetadataFieldConstants.ChildEntitiesJson);

            if (isTv)
            {
                seasons = workDtos
                    .GroupBy(w => int.TryParse(w.Season, out var sn) ? sn : 0)
                    .OrderBy(g => g.Key)
                    .Select(g => new CollectionGroupSeasonDto
                    {
                        SeasonNumber = g.Key,
                        SeasonLabel = $"Season {g.Key}",
                        Episodes = g.OrderBy(e => int.TryParse(e.Episode, out var en) ? en : e.Ordinal ?? int.MaxValue).ToList(),
                    })
                    .ToList();
            }
            else if (isMusic)
            {
                collectionChildJson = await EnsureAppleAlbumTrackManifestAsync(
                    rootParentWorkId,
                    collectionCreator,
                    FirstNonBlank(ParentCv(MetadataFieldConstants.Album), ParentCv(MetadataFieldConstants.Title), collection.DisplayName),
                    collectionChildJson,
                    parentCvs,
                    canonicalRepo,
                    appleRetailClient,
                    ct);

                // Music: tracks are already within one album collection, show as flat list with track ordering
                var ownedTracks = workDtos
                    .OrderBy(w => int.TryParse(w.TrackNumber, out var tn) ? tn : w.Ordinal ?? int.MaxValue)
                    .ToList();
                flatWorks = MergeUnownedMusicTracks(ownedTracks, collectionChildJson, collectionCover);
            }
            else
            {
                flatWorks = workDtos;
            }

            // Top billed cast for TV and Movies — read the Parent-scoped
            // cast_member array (P161) and resolve each entry to a Person
            // record so the Dashboard can open the people drawer on click.
            // Capped at 10 entries to match the design.
            var topCast = new List<CastCreditDto>();
            bool hasCast = (isTv || string.Equals(primaryMediaType, "Movies", StringComparison.OrdinalIgnoreCase))
                           && rootParentWorkId.HasValue;
            if (hasCast)
            {
                topCast = await personCreditReadService.BuildForCollectionRootAsync(
                    rootParentWorkId!.Value,
                    rootWorkQid,
                    ct);
            }

            var response = new CollectionGroupDetailDto
            {
                CollectionId = collection.Id,
                DisplayName = collection.DisplayName ?? $"Collection {collection.Id.ToString("N")[..8]}",
                RootWorkId = rootParentWorkId,
                WikidataQid = rootWorkQid,
                PrimaryMediaType = primaryMediaType,
                CoverUrl = collectionCover,
                BackgroundUrl = collectionBackground,
                BannerUrl = collectionBanner,
                DominantColors = collectionPalette.DominantColors,
                PrimaryColor = collectionPalette.PrimaryColor,
                SecondaryColor = collectionPalette.SecondaryColor,
                AccentColor = collectionPalette.AccentColor,
                Description = collectionDescription,
                Tagline = collectionTagline,
                Creator = collectionCreator,
                Director = collectionDirector,
                Writer = collectionWriter,
                ReleaseDate = collectionReleaseDate,
                YearRange = yearRange,
                Genre = collectionGenre,
                Network = collectionNetwork,
                SeasonCount = isTv ? seasons.Count : null,
                TopCast = topCast,
                TotalItems = isMusic ? flatWorks.Count : collection.Works.Count,
                Seasons = seasons,
                Works = flatWorks,
            };

            return Results.Ok(response);
        })
        .WithName("GetCollectionGroupDetail")
        .WithSummary("Returns collection header metadata and child works sorted by sequence for sub-page rendering. TV works are grouped by season.")
        .Produces<CollectionGroupDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        // GET /collections/artist-group-detail?collection_ids=id1,id2,... — combined multi-collection detail for artist-level drill-down.
        group.MapGet("/artist-group-detail", async (
            [Microsoft.AspNetCore.Mvc.FromQuery(Name = "collection_ids")] string collectionIdsParam,
            ICollectionRepository collectionRepo,
            IPersonRepository personRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(collectionIdsParam))
            {
                return Results.BadRequest("collection_ids parameter is required");
            }

            var collectionIds = collectionIdsParam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Guid.TryParse(s, out var id) ? id : (Guid?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            if (collectionIds.Count == 0)
            {
                return Results.BadRequest("No valid collection IDs provided");
            }

            // Load all collections and build album-based seasons
            var allSeasons = new List<CollectionGroupSeasonDto>();
            string? combinedCreator = null;
            string? combinedGenre = null;
            int totalItems = 0;
            var allYears = new List<string>();

            int albumIndex = 0;
            foreach (var collectionId in collectionIds)
            {
                var collection = await collectionRepo.GetCollectionWithWorksAsync(collectionId, ct);
                if (collection is null)
                {
                    continue;
                }

                // Build owned track DTOs from collection.Works.
                var primaryAssetIds = await LoadPrimaryAssetIdsAsync(collection.Works.Select(w => w.Id), db, ct);
                var ownedTracks = collection.Works
                    .OrderBy(w => w.Ordinal ?? int.MaxValue)
                    .ThenBy(w => w.Id)
                    .Select(w =>
                    {
                        var wDto = WorkDto.FromDomain(w);
                        var duration = GetCanonical(wDto, "duration_seconds")
                            ?? GetCanonical(wDto, "duration_sec")
                            ?? GetCanonical(wDto, "duration")
                            ?? GetCanonical(wDto, "runtime");
                        var durationSeconds = NormalizeAudioDurationSeconds(duration);
                        return new CollectionGroupWorkDto
                        {
                            WorkId = w.Id,
                            AssetId = primaryAssetIds.GetValueOrDefault(w.Id),
                            Title = GetCanonical(wDto, "title") ?? $"Track {w.Id.ToString("N")[..8]}",
                            Ordinal = w.Ordinal,
                            Year = GetCanonical(wDto, "release_year") ?? GetCanonical(wDto, "year"),
                            Duration = FormatAudioDuration(durationSeconds, duration),
                            DurationSeconds = durationSeconds,
                            CoverUrl = BuildCoverStreamUrl(w),
                            WikidataQid = w.WikidataQid,
                            TrackNumber = GetCanonical(wDto, "track_number"),
                            DiscNumber = ParseNullableInt(GetCanonical(wDto, "disc_number")),
                            AppleMusicId = GetCanonical(wDto, BridgeIdKeys.AppleMusicId),
                            Status = w.WikidataStatus switch
                            {
                                "confirmed" => "Verified",
                                "skipped" => "Unlinked",
                                _ => "Provisional",
                            },
                            IsOwned = true,
                        };
                    })
                    .ToList();

                // Per-album cover, year, and child_entities_json from this collection's first work.
                string? albumCover = null;
                string? albumYear = null;
                string? childJson = null;
                if (collection.Works.Count > 0)
                {
                    var firstWorkDto = WorkDto.FromDomain(collection.Works[0]);
                    combinedCreator ??= GetCanonical(firstWorkDto, "artist")
                                       ?? GetCanonical(firstWorkDto, "author");
                    combinedGenre ??= GetCanonical(firstWorkDto, "genre");
                    albumCover = BuildCoverStreamUrl(collection.Works[0]);
                    albumYear = GetCanonical(firstWorkDto, "release_year") ?? GetCanonical(firstWorkDto, "year");

                    // child_entities_json may be on any track in the album (album-level claim attached
                    // to whichever track was being processed when Stage 2 ran). Try each in order.
                    foreach (var w in collection.Works)
                    {
                        var dto = WorkDto.FromDomain(w);
                        childJson = GetCanonical(dto, MetadataFieldConstants.ChildEntitiesJson);
                        if (!string.IsNullOrWhiteSpace(childJson))
                        {
                            break;
                        }
                    }
                }

                // Merge unowned tracks from child_entities_json.
                var mergedTracks = MergeUnownedMusicTracks(ownedTracks, childJson, albumCover);

                if (mergedTracks.Any(t => t.IsOwned && !string.IsNullOrWhiteSpace(t.Year)))
                {
                    allYears.AddRange(mergedTracks.Where(t => t.IsOwned && !string.IsNullOrWhiteSpace(t.Year)).Select(t => t.Year!));
                }

                allSeasons.Add(new CollectionGroupSeasonDto
                {
                    SeasonNumber = albumIndex,
                    SeasonLabel = collection.DisplayName ?? $"Album {albumIndex + 1}",
                    CoverUrl = albumCover,
                    AlbumCollectionId = collection.Id,
                    Year = albumYear,
                    Episodes = mergedTracks,
                });

                totalItems += mergedTracks.Count(t => t.IsOwned);
                albumIndex++;
            }

            var years = allYears.Distinct().OrderBy(y => y).ToList();
            string? yearRange = years.Count switch
            {
                0 => null,
                1 => years[0],
                _ => $"{years[0]}–{years[^1]}",
            };

            // Resolve artist photo via the persons table.
            string? artistPhotoUrl = null;
            Guid? artistPersonId = null;
            if (!string.IsNullOrWhiteSpace(combinedCreator))
            {
                try
                {
                    var person = await personRepo.FindByNameAsync(combinedCreator, ct);
                    if (person is not null)
                    {
                        artistPersonId = person.Id;
                        if (!string.IsNullOrEmpty(person.LocalHeadshotPath) || !string.IsNullOrEmpty(person.HeadshotUrl))
                        {
                            artistPhotoUrl = $"/persons/{person.Id}/headshot";
                        }
                    }
                }
                catch { /* best-effort lookup */ }
            }

            var response = new CollectionGroupDetailDto
            {
                CollectionId = collectionIds[0],
                DisplayName = combinedCreator ?? "Unknown Artist",
                PrimaryMediaType = "Music",
                CoverUrl = null, // artist view header uses ArtistPhotoUrl, not an album cover
                Creator = combinedCreator,
                YearRange = yearRange,
                Genre = combinedGenre,
                TotalItems = totalItems,
                Seasons = allSeasons,
                Works = [],
                ArtistPhotoUrl = artistPhotoUrl,
                ArtistPersonId = artistPersonId,
            };

            return Results.Ok(response);
        })
        .WithName("GetArtistGroupDetail")
        .WithSummary("Returns combined multi-collection detail for artist-level drill-down in the Music tab. Each collection becomes an album 'season'.")
        .Produces<CollectionGroupDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAnyRole();

        // GET /collections/artist-detail-by-name?artistName=X — Artist drill-down for system-view mode.
        // Queries works directly from canonical_values, grouped by album, returning the same CollectionGroupDetailDto shape.
        group.MapGet("/artist-detail-by-name", async (
            [Microsoft.AspNetCore.Mvc.FromQuery(Name = "artistName")] string? artistName,
            IDatabaseConnection db,
            IPersonRepository personRepo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(artistName))
            {
                return Results.BadRequest("artistName parameter is required");
            }

            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();

            // Find all works whose artist canonical value matches, grouped by album
            cmd.CommandText = """
                WITH artist_works AS (
                    SELECT DISTINCT e.work_id
                    FROM canonical_values cv
                    INNER JOIN media_assets ma ON ma.id = cv.entity_id
                    INNER JOIN editions e ON e.id = ma.edition_id
                    WHERE cv.key = 'artist' AND cv.value = @ArtistName COLLATE NOCASE
                ),
                work_data AS (
                    SELECT
                        aw.work_id,
                        MIN(ma.id) AS asset_id,
                        MAX(CASE WHEN cv.key = 'title' THEN cv.value END) AS title,
                        MAX(CASE WHEN cv.key = 'album' THEN cv.value END) AS album,
                        MAX(CASE WHEN cv.key = 'artist' THEN cv.value END) AS artist,
                        MAX(CASE WHEN cv.key = 'track_number' THEN cv.value END) AS track_number,
                        MAX(CASE WHEN cv.key = 'disc_number' THEN cv.value END) AS disc_number,
                        MAX(CASE WHEN cv.key = 'apple_music_id' THEN cv.value END) AS apple_music_id,
                        MAX(CASE WHEN cv.key = 'release_year' THEN cv.value END) AS release_year,
                        MAX(CASE WHEN cv.key = 'year' THEN cv.value END) AS year_val,
                        MAX(CASE WHEN cv.key IN ('duration_seconds', 'duration_sec') THEN cv.value END) AS duration_seconds_value,
                        MAX(CASE WHEN cv.key = 'duration' THEN cv.value END) AS duration,
                        MAX(CASE WHEN cv.key = 'runtime' THEN cv.value END) AS runtime,
                        '/stream/' || MIN(ma.id) || '/cover' AS cover,
                        MAX(CASE WHEN cv.key = 'genre' THEN cv.value END) AS genre,
                        MAX(CASE WHEN cv.key = 'child_entities_json' THEN cv.value END) AS child_entities_json,
                        MAX(CASE WHEN cv.key = 'series_qid' THEN cv.value END) AS series_qid,
                        MAX(CASE WHEN cv.key = 'album_qid' THEN cv.value END) AS album_qid
                    FROM artist_works aw
                    INNER JOIN editions e ON e.work_id = aw.work_id
                    INNER JOIN media_assets ma ON ma.edition_id = e.id
                    INNER JOIN canonical_values cv ON cv.entity_id = ma.id
                    GROUP BY aw.work_id
                )
                SELECT * FROM work_data ORDER BY album, CAST(track_number AS INTEGER), title
                """;

            var ap = cmd.CreateParameter();
            ap.ParameterName = "@ArtistName";
            ap.Value = artistName;
            cmd.Parameters.Add(ap);

            using var reader = cmd.ExecuteReader();
            var albumMap = new Dictionary<string, List<CollectionGroupWorkDto>>(StringComparer.OrdinalIgnoreCase);
            var albumCovers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var albumYears = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var albumChildJson = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            string? combinedCreator = null;
            string? combinedGenre = null;
            var allYears = new List<string>();

            while (reader.Read())
            {
                var workId = GuidSql.FromDb(reader.GetValue(reader.GetOrdinal("work_id")));
                var assetId = reader.IsDBNull(reader.GetOrdinal("asset_id"))
                    ? (Guid?)null
                    : GuidSql.FromDb(reader.GetValue(reader.GetOrdinal("asset_id")));
                var title = reader.IsDBNull(reader.GetOrdinal("title")) ? null : reader.GetString(reader.GetOrdinal("title"));
                var album = reader.IsDBNull(reader.GetOrdinal("album")) ? null : reader.GetString(reader.GetOrdinal("album"));
                var trackNum = reader.IsDBNull(reader.GetOrdinal("track_number")) ? null : reader.GetString(reader.GetOrdinal("track_number"));
                var discNum = reader.IsDBNull(reader.GetOrdinal("disc_number")) ? null : reader.GetString(reader.GetOrdinal("disc_number"));
                var appleMusicId = reader.IsDBNull(reader.GetOrdinal("apple_music_id")) ? null : reader.GetString(reader.GetOrdinal("apple_music_id"));
                var releaseYear = reader.IsDBNull(reader.GetOrdinal("release_year")) ? null : reader.GetString(reader.GetOrdinal("release_year"));
                var yearVal = reader.IsDBNull(reader.GetOrdinal("year_val")) ? null : reader.GetString(reader.GetOrdinal("year_val"));
                var durationSecondsValue = reader.IsDBNull(reader.GetOrdinal("duration_seconds_value")) ? null : reader.GetString(reader.GetOrdinal("duration_seconds_value"));
                var duration = reader.IsDBNull(reader.GetOrdinal("duration")) ? null : reader.GetString(reader.GetOrdinal("duration"));
                var runtime = reader.IsDBNull(reader.GetOrdinal("runtime")) ? null : reader.GetString(reader.GetOrdinal("runtime"));
                var rawDuration = durationSecondsValue ?? duration ?? runtime;
                var durationSeconds = NormalizeAudioDurationSeconds(rawDuration);
                var displayDuration = FormatAudioDuration(durationSeconds, rawDuration);
                var cover = reader.IsDBNull(reader.GetOrdinal("cover")) ? null : reader.GetString(reader.GetOrdinal("cover"));
                var genre = reader.IsDBNull(reader.GetOrdinal("genre")) ? null : reader.GetString(reader.GetOrdinal("genre"));
                var artistVal = reader.IsDBNull(reader.GetOrdinal("artist")) ? null : reader.GetString(reader.GetOrdinal("artist"));
                var childJson = reader.IsDBNull(reader.GetOrdinal("child_entities_json")) ? null : reader.GetString(reader.GetOrdinal("child_entities_json"));

                combinedCreator ??= artistVal;
                combinedGenre ??= genre;

                var year = releaseYear ?? yearVal;
                if (!string.IsNullOrWhiteSpace(year))
                {
                    allYears.Add(year);
                }

                var albumKey = album ?? "Unknown Album";
                if (!albumMap.TryGetValue(albumKey, out var tracks))
                {
                    tracks = [];
                    albumMap[albumKey] = tracks;
                }
                if (!albumCovers.ContainsKey(albumKey))
                {
                    albumCovers[albumKey] = cover;
                }

                if (!albumYears.ContainsKey(albumKey) || string.IsNullOrWhiteSpace(albumYears[albumKey]))
                {
                    albumYears[albumKey] = year;
                }

                if (!albumChildJson.ContainsKey(albumKey) || string.IsNullOrWhiteSpace(albumChildJson[albumKey]))
                {
                    albumChildJson[albumKey] = childJson;
                }

                tracks.Add(new CollectionGroupWorkDto
                {
                    WorkId = workId,
                    AssetId = assetId,
                    Title = title ?? $"Track {workId.ToString("N")[..8]}",
                    Year = year,
                    Duration = displayDuration,
                    DurationSeconds = durationSeconds,
                    CoverUrl = cover,
                    TrackNumber = trackNum,
                    DiscNumber = ParseNullableInt(discNum),
                    AppleMusicId = appleMusicId,
                    Status = "Provisional",
                    IsOwned = true,
                });
            }

            var years = allYears.Distinct().OrderBy(y => y).ToList();
            string? yearRange = years.Count switch
            {
                0 => null,
                1 => years[0],
                _ => $"{years[0]}–{years[^1]}",
            };

            int totalItems = 0;
            var seasons = albumMap.Select((kvp, idx) =>
            {
                var albumKey = kvp.Key;
                var albumCover = albumCovers.TryGetValue(albumKey, out var c) ? c : null;
                var albumYear = albumYears.TryGetValue(albumKey, out var y) ? y : null;
                var childJson = albumChildJson.TryGetValue(albumKey, out var j) ? j : null;
                var merged = MergeUnownedMusicTracks(kvp.Value, childJson, albumCover);
                totalItems += merged.Count(t => t.IsOwned);
                return new CollectionGroupSeasonDto
                {
                    SeasonNumber = idx,
                    SeasonLabel = albumKey,
                    CoverUrl = albumCover,
                    Year = albumYear,
                    AlbumCollectionId = null, // by-name lookup has no concrete collection id
                    Episodes = merged,
                };
            }).ToList();

            // Resolve artist photo via the persons table.
            string? artistPhotoUrl = null;
            Guid? artistPersonId = null;
            if (!string.IsNullOrWhiteSpace(combinedCreator))
            {
                try
                {
                    var person = await personRepo.FindByNameAsync(combinedCreator, ct);
                    if (person is not null)
                    {
                        artistPersonId = person.Id;
                        if (!string.IsNullOrEmpty(person.LocalHeadshotPath) || !string.IsNullOrEmpty(person.HeadshotUrl))
                        {
                            artistPhotoUrl = $"/persons/{person.Id}/headshot";
                        }
                    }
                }
                catch { /* best-effort lookup */ }
            }

            var response = new CollectionGroupDetailDto
            {
                CollectionId = Guid.Empty,
                DisplayName = artistName,
                PrimaryMediaType = "Music",
                CoverUrl = null,
                Creator = combinedCreator,
                YearRange = yearRange,
                Genre = combinedGenre,
                TotalItems = totalItems,
                Seasons = seasons,
                Works = [],
                ArtistPhotoUrl = artistPhotoUrl,
                ArtistPersonId = artistPersonId,
            };

            return Results.Ok(response);
        })
        .WithName("GetArtistDetailByName")
        .WithSummary("Returns artist drill-down detail by artist name, querying directly from canonical values. Used when system-view collections are active and ContentGroup collections are unavailable.")
        .Produces<CollectionGroupDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAnyRole();

        // GET /collections/system-view-detail?groupField=album&groupValue=The%20Record&mediaType=Music
        // Generic grouped detail endpoint for non-routed system views such as music albums/artists.
        // TV shows use /watch/tv/show/{collectionId} and the unified detail composer instead of this endpoint.
        group.MapGet("/system-view-detail", async (
            [Microsoft.AspNetCore.Mvc.FromQuery(Name = "groupField")] string? groupField,
            [Microsoft.AspNetCore.Mvc.FromQuery(Name = "groupValue")] string? groupValue,
            [Microsoft.AspNetCore.Mvc.FromQuery(Name = "mediaType")] string? mediaType,
            [Microsoft.AspNetCore.Mvc.FromQuery(Name = "artistName")] string? artistName,
            ICanonicalValueRepository canonicalRepo,
            AppleRetailClient appleRetailClient,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(groupField) || string.IsNullOrWhiteSpace(groupValue))
            {
                return Results.BadRequest("groupField and groupValue parameters are required");
            }

            // Determine the secondary grouping field and sort fields based on the primary group
            var (secondaryGroup, sortFields) = groupField.ToLowerInvariant() switch
            {
                "show_name" => ("season_number", "season_number, episode_number, title"),
                "artist" => ("album", "album, CAST(track_number AS INTEGER), title"),
                "album" => ((string?)null, "CAST(track_number AS INTEGER), title"),
                "series" => ((string?)null, "CAST(series_index AS INTEGER), title"),
                _ => ((string?)null, "title"),
            };

            // Label for secondary groups
            var secondaryLabelPrefix = groupField.ToLowerInvariant() switch
            {
                "show_name" => "Season ",
                "artist" => (string?)null, // use album name directly
                _ => null,
            };

            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();

            // Build the query: find all works matching groupField=groupValue, optionally filtered by media_type.
            // Music album drill-down is intentionally track-derived: albums exist here only because
            // owned tracks point at an album root. Match the root album/title first, with asset album
            // as a fallback for tracks that have not received parent-scoped provider metadata yet.
            var mediaTypeFilter = !string.IsNullOrWhiteSpace(mediaType)
                ? "INNER JOIN works w ON w.id = e.work_id AND w.media_type = @MediaType"
                : "INNER JOIN works w ON w.id = e.work_id";

            cmd.CommandText = $"""
                WITH matched_works AS (
                    SELECT DISTINCT e.work_id
                    FROM editions e
                    INNER JOIN media_assets ma ON ma.edition_id = e.id
                    {mediaTypeFilter}
                    LEFT JOIN works p ON p.id = w.parent_work_id
                    LEFT JOIN works gp ON gp.id = p.parent_work_id
                    WHERE (
                        (
                            @IsMusicAlbumGroup = 1
                            AND COALESCE(
                                (
                                    SELECT cv_parent_album.value
                                    FROM canonical_values cv_parent_album
                                    WHERE cv_parent_album.entity_id = COALESCE(gp.id, p.id, w.id)
                                      AND cv_parent_album.key = 'album'
                                    LIMIT 1
                                ),
                                (
                                    SELECT cv_parent_title.value
                                    FROM canonical_values cv_parent_title
                                    WHERE cv_parent_title.entity_id = COALESCE(gp.id, p.id, w.id)
                                      AND cv_parent_title.key = 'title'
                                    LIMIT 1
                                ),
                                (
                                    SELECT cv_asset_album.value
                                    FROM canonical_values cv_asset_album
                                    WHERE cv_asset_album.entity_id = ma.id
                                      AND cv_asset_album.key = 'album'
                                    LIMIT 1
                                )
                            ) = @GroupValue COLLATE NOCASE
                        )
                        OR (
                            @IsMusicAlbumGroup = 0
                            AND EXISTS (
                                SELECT 1
                                FROM canonical_values cv
                                WHERE cv.key = @GroupField
                                  AND cv.value = @GroupValue COLLATE NOCASE
                                  AND (
                                      cv.entity_id = ma.id
                                      OR cv.entity_id = w.id
                                      OR cv.entity_id = p.id
                                      OR cv.entity_id = gp.id
                                  )
                            )
                        )
                    )
                      AND (
                          @ArtistName IS NULL
                          OR EXISTS (
                              SELECT 1
                              FROM canonical_values cv_artist
                              WHERE cv_artist.key IN ('artist', 'author')
                                AND cv_artist.value = @ArtistName COLLATE NOCASE
                                AND (
                                    cv_artist.entity_id = ma.id
                                    OR cv_artist.entity_id = w.id
                                    OR cv_artist.entity_id = p.id
                                    OR cv_artist.entity_id = gp.id
                                )
                          )
                      )
                ),
                work_data AS (
                    SELECT
                        mw.work_id,
                        MIN(ma.id) AS asset_id,
                        COALESCE(gp.id, p.id, w.id) AS root_work_id,
                        MAX(CASE WHEN cv.key = 'title'               THEN cv.value END) AS title,
                        MAX(CASE WHEN cv.key = 'episode_title'       THEN cv.value END) AS episode_title,
                        MAX(CASE WHEN cv.key = 'show_name'           THEN cv.value END) AS show_name,
                        MAX(CASE WHEN cv.key = 'season_number'       THEN cv.value END) AS season_number,
                        MAX(CASE WHEN cv.key = 'episode_number'      THEN cv.value END) AS episode_number,
                        MAX(CASE WHEN cv.key = 'series'              THEN cv.value END) AS series,
                        MAX(CASE WHEN cv.key = 'series_index'        THEN cv.value END) AS series_index,
                        MAX(CASE WHEN cv.key = 'album'               THEN cv.value END) AS album,
                        MAX(CASE WHEN cv.key = 'artist'              THEN cv.value END) AS artist,
                        MAX(CASE WHEN cv.key = 'author'              THEN cv.value END) AS author,
                        MAX(CASE WHEN cv.key = 'director'            THEN cv.value END) AS director,
                        MAX(CASE WHEN cv.key = 'track_number'        THEN cv.value END) AS track_number,
                        MAX(CASE WHEN cv.key = 'disc_number'         THEN cv.value END) AS disc_number,
                        MAX(CASE WHEN cv.key = 'apple_music_id'      THEN cv.value END) AS apple_music_id,
                        MAX(CASE WHEN cv.key = 'release_year'        THEN cv.value END) AS release_year,
                        MAX(CASE WHEN cv.key = 'year'                THEN cv.value END) AS year_val,
                        MAX(CASE WHEN cv.key IN ('duration_seconds', 'duration_sec') THEN cv.value END) AS duration_seconds_value,
                        MAX(CASE WHEN cv.key = 'duration'            THEN cv.value END) AS duration,
                        MAX(CASE WHEN cv.key = 'runtime'             THEN cv.value END) AS runtime,
                        COALESCE(
                            (
                                SELECT NULLIF(cv_group_cover.value, '')
                                FROM canonical_values cv_group_cover
                                WHERE cv_group_cover.entity_id = COALESCE(gp.id, p.id, w.id)
                                  AND cv_group_cover.key IN ('cover_url', 'cover', 'poster_url', 'poster')
                                LIMIT 1
                            ),
                            MAX(CASE WHEN cv.key IN ('cover_url', 'cover', 'poster_url', 'poster') THEN NULLIF(cv.value, '') END),
                            '/stream/' || MIN(ma.id) || '/cover'
                        ) AS cover,
                        COALESCE(
                            (
                                SELECT NULLIF(cv_group_background.value, '')
                                FROM canonical_values cv_group_background
                                WHERE cv_group_background.entity_id = COALESCE(gp.id, p.id, w.id)
                                  AND cv_group_background.key IN ('background_url', 'background')
                                LIMIT 1
                            ),
                            MAX(CASE WHEN cv.key IN ('background_url', 'background') THEN NULLIF(cv.value, '') END)
                        ) AS background,
                        COALESCE(
                            (
                                SELECT NULLIF(cv_group_banner.value, '')
                                FROM canonical_values cv_group_banner
                                WHERE cv_group_banner.entity_id = COALESCE(gp.id, p.id, w.id)
                                  AND cv_group_banner.key IN ('banner_url', 'banner')
                                LIMIT 1
                            ),
                            MAX(CASE WHEN cv.key IN ('banner_url', 'banner') THEN NULLIF(cv.value, '') END)
                        ) AS banner,
                        COALESCE(
                            (
                                SELECT NULLIF(cv_group_hero.value, '')
                                FROM canonical_values cv_group_hero
                                WHERE cv_group_hero.entity_id = COALESCE(gp.id, p.id, w.id)
                                  AND cv_group_hero.key IN ('hero_url', 'hero')
                                LIMIT 1
                            ),
                            MAX(CASE WHEN cv.key IN ('hero_url', 'hero') THEN NULLIF(cv.value, '') END)
                        ) AS hero,
                        COALESCE(
                            (
                                SELECT NULLIF(cv_group_logo.value, '')
                                FROM canonical_values cv_group_logo
                                WHERE cv_group_logo.entity_id = COALESCE(gp.id, p.id, w.id)
                                  AND cv_group_logo.key IN ('clear_logo_url', 'clear_logo', 'logo_url', 'logo')
                                LIMIT 1
                            ),
                            MAX(CASE WHEN cv.key IN ('clear_logo_url', 'clear_logo', 'logo_url', 'logo') THEN NULLIF(cv.value, '') END)
                        ) AS logo,
                        COALESCE(
                            (
                                SELECT NULLIF(cv_group_primary.value, '')
                                FROM canonical_values cv_group_primary
                                WHERE cv_group_primary.entity_id = COALESCE(gp.id, p.id, w.id)
                                  AND cv_group_primary.key IN ('artwork_primary_hex', 'cover_primary_hex', 'primary_color')
                                LIMIT 1
                            ),
                            MAX(CASE WHEN cv.key IN ('artwork_primary_hex', 'cover_primary_hex', 'primary_color') THEN NULLIF(cv.value, '') END)
                        ) AS primary_color,
                        COALESCE(
                            (
                                SELECT NULLIF(cv_group_secondary.value, '')
                                FROM canonical_values cv_group_secondary
                                WHERE cv_group_secondary.entity_id = COALESCE(gp.id, p.id, w.id)
                                  AND cv_group_secondary.key IN ('artwork_secondary_hex', 'cover_secondary_hex', 'secondary_color')
                                LIMIT 1
                            ),
                            MAX(CASE WHEN cv.key IN ('artwork_secondary_hex', 'cover_secondary_hex', 'secondary_color') THEN NULLIF(cv.value, '') END)
                        ) AS secondary_color,
                        COALESCE(
                            (
                                SELECT NULLIF(cv_group_accent.value, '')
                                FROM canonical_values cv_group_accent
                                WHERE cv_group_accent.entity_id = COALESCE(gp.id, p.id, w.id)
                                  AND cv_group_accent.key IN ('artwork_accent_hex', 'cover_accent_hex', 'accent_color', 'dominant_color')
                                LIMIT 1
                            ),
                            MAX(CASE WHEN cv.key IN ('artwork_accent_hex', 'cover_accent_hex', 'accent_color', 'dominant_color') THEN NULLIF(cv.value, '') END)
                        ) AS accent_color,
                        MAX(CASE WHEN cv.key = 'genre'               THEN cv.value END) AS genre,
                        MAX(CASE WHEN cv.key = 'network'             THEN cv.value END) AS network,
                        MAX(CASE WHEN cv.key = 'child_entities_json' THEN cv.value END) AS child_entities_json
                    FROM matched_works mw
                    INNER JOIN works w ON w.id = mw.work_id
                    LEFT JOIN works p ON p.id = w.parent_work_id
                    LEFT JOIN works gp ON gp.id = p.parent_work_id
                    INNER JOIN editions e ON e.work_id = mw.work_id
                    INNER JOIN media_assets ma ON ma.edition_id = e.id
                    INNER JOIN canonical_values cv ON cv.entity_id = ma.id
                    GROUP BY mw.work_id
                )
                SELECT * FROM work_data ORDER BY {sortFields}
                """;

            var pField = cmd.CreateParameter();
            pField.ParameterName = "@GroupField";
            pField.Value = groupField;
            cmd.Parameters.Add(pField);

            var pValue = cmd.CreateParameter();
            pValue.ParameterName = "@GroupValue";
            pValue.Value = groupValue;
            cmd.Parameters.Add(pValue);

            var pArtist = cmd.CreateParameter();
            pArtist.ParameterName = "@ArtistName";
            pArtist.Value = string.IsNullOrWhiteSpace(artistName) ? DBNull.Value : artistName;
            cmd.Parameters.Add(pArtist);

            var pIsMusicAlbumGroup = cmd.CreateParameter();
            pIsMusicAlbumGroup.ParameterName = "@IsMusicAlbumGroup";
            pIsMusicAlbumGroup.Value = string.Equals(mediaType, "Music", StringComparison.OrdinalIgnoreCase)
                && string.Equals(groupField, "album", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
            cmd.Parameters.Add(pIsMusicAlbumGroup);

            if (!string.IsNullOrWhiteSpace(mediaType))
            {
                var pMedia = cmd.CreateParameter();
                pMedia.ParameterName = "@MediaType";
                pMedia.Value = mediaType;
                cmd.Parameters.Add(pMedia);
            }

            using var reader = cmd.ExecuteReader();
            // sectionKey → owned CollectionGroupWorkDtos. Unowned items are merged after
            // the reader loop using child_entities_json from the parent.
            var sectionMap = new Dictionary<string, List<CollectionGroupWorkDto>>(StringComparer.OrdinalIgnoreCase);
            string? combinedCreator = null;
            string? combinedCover = null;
            string? combinedBackground = null;
            string? combinedBanner = null;
            string? combinedHero = null;
            string? combinedLogo = null;
            string? combinedPrimaryColor = null;
            string? combinedSecondaryColor = null;
            string? combinedAccentColor = null;
            string? combinedGenre = null;
            string? combinedNetwork = null;
            Guid? combinedRootWorkId = null;
            var allYears = new List<string>();
            int totalItems = 0;
            // Collect child_entities_json from any owned work that carries it.
            string? collectedChildJson = null;
            var isMusicAlbumGroup = string.Equals(mediaType, "Music", StringComparison.OrdinalIgnoreCase)
                && string.Equals(groupField, "album", StringComparison.OrdinalIgnoreCase);

            while (reader.Read())
            {
                var workId = GuidSql.FromDb(reader.GetValue(reader.GetOrdinal("work_id")));
                var assetId = reader.IsDBNull(reader.GetOrdinal("asset_id"))
                    ? (Guid?)null
                    : GuidSql.FromDb(reader.GetValue(reader.GetOrdinal("asset_id")));
                var rootWorkId = reader.IsDBNull(reader.GetOrdinal("root_work_id"))
                    ? (Guid?)null
                    : GuidSql.FromDb(reader.GetValue(reader.GetOrdinal("root_work_id")));
                var title = reader.IsDBNull(reader.GetOrdinal("title")) ? null : reader.GetString(reader.GetOrdinal("title"));
                var episodeTitle = reader.IsDBNull(reader.GetOrdinal("episode_title")) ? null : reader.GetString(reader.GetOrdinal("episode_title"));
                var cover = reader.IsDBNull(reader.GetOrdinal("cover")) ? null : reader.GetString(reader.GetOrdinal("cover"));
                var background = reader.IsDBNull(reader.GetOrdinal("background")) ? null : reader.GetString(reader.GetOrdinal("background"));
                var banner = reader.IsDBNull(reader.GetOrdinal("banner")) ? null : reader.GetString(reader.GetOrdinal("banner"));
                var hero = reader.IsDBNull(reader.GetOrdinal("hero")) ? null : reader.GetString(reader.GetOrdinal("hero"));
                var logo = reader.IsDBNull(reader.GetOrdinal("logo")) ? null : reader.GetString(reader.GetOrdinal("logo"));
                var primaryColor = reader.IsDBNull(reader.GetOrdinal("primary_color")) ? null : reader.GetString(reader.GetOrdinal("primary_color"));
                var secondaryColor = reader.IsDBNull(reader.GetOrdinal("secondary_color")) ? null : reader.GetString(reader.GetOrdinal("secondary_color"));
                var accentColor = reader.IsDBNull(reader.GetOrdinal("accent_color")) ? null : reader.GetString(reader.GetOrdinal("accent_color"));
                var genre = reader.IsDBNull(reader.GetOrdinal("genre")) ? null : reader.GetString(reader.GetOrdinal("genre"));
                var durationSecondsValue = reader.IsDBNull(reader.GetOrdinal("duration_seconds_value")) ? null : reader.GetString(reader.GetOrdinal("duration_seconds_value"));
                var duration = reader.IsDBNull(reader.GetOrdinal("duration")) ? null : reader.GetString(reader.GetOrdinal("duration"));
                var runtime = reader.IsDBNull(reader.GetOrdinal("runtime")) ? null : reader.GetString(reader.GetOrdinal("runtime"));
                var rawDuration = durationSecondsValue ?? duration ?? runtime;
                var durationSeconds = isMusicAlbumGroup ? NormalizeAudioDurationSeconds(rawDuration) : null;
                var displayDuration = isMusicAlbumGroup ? FormatAudioDuration(durationSeconds, rawDuration) : rawDuration;
                var releaseYear = reader.IsDBNull(reader.GetOrdinal("release_year")) ? null : reader.GetString(reader.GetOrdinal("release_year"));
                var yearVal = reader.IsDBNull(reader.GetOrdinal("year_val")) ? null : reader.GetString(reader.GetOrdinal("year_val"));
                var episodeNum = reader.IsDBNull(reader.GetOrdinal("episode_number")) ? null : reader.GetString(reader.GetOrdinal("episode_number"));
                var trackNum = reader.IsDBNull(reader.GetOrdinal("track_number")) ? null : reader.GetString(reader.GetOrdinal("track_number"));
                var discNum = reader.IsDBNull(reader.GetOrdinal("disc_number")) ? null : reader.GetString(reader.GetOrdinal("disc_number"));
                var appleMusicId = reader.IsDBNull(reader.GetOrdinal("apple_music_id")) ? null : reader.GetString(reader.GetOrdinal("apple_music_id"));
                var seqIndex = reader.IsDBNull(reader.GetOrdinal("series_index")) ? null : reader.GetString(reader.GetOrdinal("series_index"));
                var childJson = reader.IsDBNull(reader.GetOrdinal("child_entities_json")) ? null : reader.GetString(reader.GetOrdinal("child_entities_json"));

                // Accumulate the first non-null child_entities_json we encounter —
                // it may appear on any owned sibling in the same group.
                collectedChildJson ??= string.IsNullOrWhiteSpace(childJson) ? null : childJson;

                // Determine creator (author, director, artist, or network for TV)
                var creator = reader.IsDBNull(reader.GetOrdinal("author")) ? null : reader.GetString(reader.GetOrdinal("author"));
                var directorVal = reader.IsDBNull(reader.GetOrdinal("director")) ? null : reader.GetString(reader.GetOrdinal("director"));
                var artistVal = reader.IsDBNull(reader.GetOrdinal("artist")) ? null : reader.GetString(reader.GetOrdinal("artist"));
                var networkVal = reader.IsDBNull(reader.GetOrdinal("network")) ? null : reader.GetString(reader.GetOrdinal("network"));
                // For TV, prefer network over director as the header creator
                if (mediaType == "TV")
                {
                    creator ??= networkVal ?? directorVal ?? artistVal;
                }
                else
                {
                    creator ??= directorVal ?? artistVal;
                }

                combinedCreator ??= creator;
                combinedCover ??= cover;
                combinedBackground ??= background;
                combinedBanner ??= banner;
                combinedHero ??= hero;
                combinedLogo ??= logo;
                combinedPrimaryColor ??= primaryColor;
                combinedSecondaryColor ??= secondaryColor;
                combinedAccentColor ??= accentColor;
                combinedGenre ??= genre;
                combinedRootWorkId ??= rootWorkId;

                combinedNetwork ??= networkVal;

                var year = releaseYear ?? yearVal;
                if (!string.IsNullOrWhiteSpace(year))
                {
                    allYears.Add(year);
                }

                // Build group key for sections
                string sectionKey;
                if (secondaryGroup is not null)
                {
                    var secVal = reader.IsDBNull(reader.GetOrdinal(secondaryGroup)) ? null : reader.GetString(reader.GetOrdinal(secondaryGroup));
                    sectionKey = secVal ?? "Unknown";
                }
                else
                {
                    sectionKey = "_flat";
                }

                if (!sectionMap.TryGetValue(sectionKey, out var items))
                {
                    items = [];
                    sectionMap[sectionKey] = items;
                }

                items.Add(new CollectionGroupWorkDto
                {
                    WorkId = workId,
                    AssetId = assetId,
                    Title = episodeTitle ?? title ?? $"Item {workId.ToString("N")[..8]}",
                    Year = year,
                    Duration = displayDuration,
                    DurationSeconds = durationSeconds,
                    CoverUrl = cover,
                    BackgroundUrl = background,
                    BannerUrl = banner,
                    HeroUrl = hero,
                    Episode = episodeNum,
                    TrackNumber = trackNum,
                    DiscNumber = ParseNullableInt(discNum),
                    AppleMusicId = appleMusicId,
                    Ordinal = int.TryParse(seqIndex, out var si) ? si : null,
                    Status = "Provisional",
                    IsOwned = true,
                });

                totalItems++;
            }

            // M-083: Merge unowned items from child_entities_json.
            // For TV shows the JSON has an "episodes" array grouped by season;
            // for music it has "tracks"; for comics "issues". We use the same
            // child-entity parsing used by MergeUnownedMusicTracks.
            IReadOnlyList<CanonicalValue> rootCanonicals = [];
            if (isMusicAlbumGroup && combinedRootWorkId.HasValue)
            {
                rootCanonicals = await canonicalRepo.GetByEntityAsync(combinedRootWorkId.Value, ct);
                collectedChildJson ??= FirstCanonicalValue(rootCanonicals, MetadataFieldConstants.ChildEntitiesJson);
                collectedChildJson = await EnsureAppleAlbumTrackManifestAsync(
                    combinedRootWorkId,
                    combinedCreator,
                    groupValue,
                    collectedChildJson,
                    rootCanonicals,
                    canonicalRepo,
                    appleRetailClient,
                    ct);
            }

            if (!string.IsNullOrWhiteSpace(collectedChildJson))
            {
                MergeUnownedChildEntities(
                    sectionMap,
                    collectedChildJson,
                    groupField,
                    secondaryGroup,
                    combinedCover);
            }

            var palette = ResolvePalette(combinedRootWorkId, rootCanonicals, db);
            combinedPrimaryColor ??= palette.PrimaryColor;
            combinedSecondaryColor ??= palette.SecondaryColor;
            combinedAccentColor ??= palette.AccentColor;
            var rootWikidataQid = FirstCanonicalValue(rootCanonicals, BridgeIdKeys.WikidataQid);
            var rootDescription = FirstCanonicalValue(rootCanonicals, MetadataFieldConstants.Description);
            var rootTagline = FirstCanonicalValue(rootCanonicals, MetadataFieldConstants.Tagline);
            var rootReleaseDate = NormalizeReleaseDate(
                FirstCanonicalValue(rootCanonicals, "release_date", "date", "year"));

            var years = allYears.Distinct().OrderBy(y => y).ToList();
            string? yearRange = years.Count switch
            {
                0 => null,
                1 => years[0],
                _ => $"{years[0]}–{years[^1]}",
            };

            // Build seasons/sections if we have a secondary group
            List<CollectionGroupSeasonDto> seasons;
            List<CollectionGroupWorkDto> flatWorks;

            // Recalculate totalItems to include unowned rows added during merge.
            totalItems = sectionMap.Values.Sum(v => v.Count);

            if (secondaryGroup is not null && sectionMap.Count > 0 && !sectionMap.ContainsKey("_flat"))
            {
                seasons = sectionMap
                    .OrderBy(kvp => int.TryParse(kvp.Key, out var n) ? n : int.MaxValue)
                    .ThenBy(kvp => kvp.Key)
                    .Select((kvp, idx) => new CollectionGroupSeasonDto
                    {
                        SeasonNumber = int.TryParse(kvp.Key, out var sn) ? sn : idx,
                        SeasonLabel = secondaryLabelPrefix is not null
                            ? $"{secondaryLabelPrefix}{kvp.Key}"
                            : kvp.Key,
                        Episodes = kvp.Value,
                    })
                    .ToList();
                flatWorks = [];
            }
            else
            {
                seasons = [];
                flatWorks = sectionMap.Values.SelectMany(v => v).ToList();
                if (isMusicAlbumGroup)
                {
                    flatWorks = flatWorks
                        .OrderBy(item => int.TryParse(item.TrackNumber, out var trackNumber) ? trackNumber : item.Ordinal ?? int.MaxValue)
                        .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }

            var response = new CollectionGroupDetailDto
            {
                CollectionId = Guid.Empty,
                DisplayName = groupValue,
                RootWorkId = combinedRootWorkId,
                WikidataQid = rootWikidataQid,
                PrimaryMediaType = mediaType ?? "Unknown",
                CoverUrl = combinedCover,
                BackgroundUrl = combinedBackground,
                BannerUrl = combinedBanner,
                HeroUrl = combinedHero,
                LogoUrl = combinedLogo,
                DominantColors = palette.DominantColors,
                PrimaryColor = combinedPrimaryColor,
                SecondaryColor = combinedSecondaryColor,
                AccentColor = combinedAccentColor,
                Description = rootDescription,
                Tagline = rootTagline,
                Creator = combinedCreator,
                ReleaseDate = rootReleaseDate,
                YearRange = yearRange,
                Genre = combinedGenre,
                Network = combinedNetwork,
                TotalItems = totalItems,
                Seasons = seasons,
                Works = flatWorks,
            };

            return Results.Ok(response);
        })
        .WithName("GetSystemViewGroupDetail")
        .WithSummary("Generic system-view drill-down. Returns works grouped by a secondary field for any group field (show_name, series, album, artist).")
        .Produces<CollectionGroupDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAnyRole();

        // ── Content Groups ───────────────────────────────────────────────────────

        // GET /collections/content-groups — Universe collections that have child works (albums, TV series, book series, movie series).
        group.MapGet("/content-groups", async (ICollectionRepository collectionRepo, CancellationToken ct) =>
        {
            var collections = await collectionRepo.GetContentGroupsAsync(ct);

            var dtos = collections.Select(h =>
            {
                // Primary media type is whichever appears most among this collection's works.
                var primaryMediaType = h.Works
                    .GroupBy(w => w.MediaType.ToString())
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key ?? "Unknown";

                // Cover from the first work that has a media asset.
                string? cover = null;
                string? background = null;
                string? banner = null;
                string? logo = null;
                foreach (var w in h.Works)
                {
                    cover = BuildCoverStreamUrl(w);
                    background = BuildBackgroundStreamUrl(w);
                    banner = BuildBannerStreamUrl(w);
                    logo = BuildLogoStreamUrl(w);
                    if (cover is not null || background is not null || banner is not null || logo is not null)
                    {
                        break;
                    }
                }

                // Creator from first work.
                var firstDto = h.Works.Count > 0 ? WorkDto.FromDomain(h.Works[0]) : null;
                string? creator = GetCanonical(firstDto, "author")
                                  ?? GetCanonical(firstDto, "artist");
                string? releaseDate = NormalizeReleaseDate(
                    GetCanonical(firstDto, "release_date")
                    ?? GetCanonical(firstDto, "date")
                    ?? GetCanonical(firstDto, "year"));

                return new ContentGroupDto
                {
                    CollectionId = h.Id,
                    DisplayName = h.DisplayName ?? $"Collection {h.Id.ToString("N")[..8]}",
                    WikidataQid = h.WikidataQid,
                    PrimaryMediaType = primaryMediaType,
                    WorkCount = h.Works.Count,
                    DistinctTitleCount = CountDistinctWorkTitles(h.Works),
                    CoverUrl = cover,
                    BackgroundUrl = background,
                    BannerUrl = banner,
                    HeroUrl = null,
                    LogoUrl = logo,
                    CoverAspectClass = GetCanonical(firstDto, "cover_aspect_class"),
                    SquareAspectClass = GetCanonical(firstDto, "square_aspect_class"),
                    BackgroundAspectClass = GetCanonical(firstDto, "background_aspect_class"),
                    BannerAspectClass = GetCanonical(firstDto, "banner_aspect_class"),
                    CoverWidthPx = ParseNullableInt(GetCanonical(firstDto, "cover_width_px")),
                    CoverHeightPx = ParseNullableInt(GetCanonical(firstDto, "cover_height_px")),
                    SquareWidthPx = ParseNullableInt(GetCanonical(firstDto, "square_width_px")),
                    SquareHeightPx = ParseNullableInt(GetCanonical(firstDto, "square_height_px")),
                    BackgroundWidthPx = ParseNullableInt(GetCanonical(firstDto, "background_width_px")),
                    BackgroundHeightPx = ParseNullableInt(GetCanonical(firstDto, "background_height_px")),
                    BannerWidthPx = ParseNullableInt(GetCanonical(firstDto, "banner_width_px")),
                    BannerHeightPx = ParseNullableInt(GetCanonical(firstDto, "banner_height_px")),
                    Description = h.Description ?? GetCanonical(firstDto, "description"),
                    Tagline = GetCanonical(firstDto, "tagline"),
                    Creator = creator,
                    Director = string.Equals(primaryMediaType, "TV", StringComparison.OrdinalIgnoreCase)
                        ? null
                        : GetCanonical(firstDto, "director"),
                    Writer = GetCanonical(firstDto, "writer"),
                    ReleaseDate = releaseDate,
                    UniverseStatus = h.UniverseStatus,
                    CreatedAt = h.CreatedAt,
                    Network = GetCanonical(firstDto, "network"),
                    Year = GetCanonical(firstDto, "release_year") ?? GetCanonical(firstDto, "year"),
                    SeasonCount = string.Equals(primaryMediaType, "TV", StringComparison.OrdinalIgnoreCase)
                        ? h.Works
                            .Select(work => GetCanonical(WorkDto.FromDomain(work), "season_number"))
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count()
                        : null,
                };
            })
            .OrderBy(d => d.DisplayName)
            .ToList();

            return Results.Ok(dtos);
        })
        .WithName("GetContentGroups")
        .WithSummary("Returns Universe-type collections that contain works (albums, TV series, book series, movie series), grouped by primary media type.")
        .Produces<List<ContentGroupDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // GET /collections/system-views?mediaType=&groupField= — System view collections resolved as content groups.
        // Used by library container views (By Show, By Artist, By Album) that are driven by System collections
        // rather than ContentGroup collections.
        group.MapGet("/system-views", async (
            string? mediaType,
            string? groupField,
            IDatabaseConnection db,
            IPersonRepository personRepo,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("CollectionEndpoints.SystemViews");

            log.LogInformation("[ByAlbum] system-views called — mediaType={MediaType} groupField={GroupField}",
                mediaType ?? "(none)", groupField ?? "(none)");

            var viewCollections = BuiltInBrowseCollectionCatalog
                .GetSystemViewDefinitions(mediaType, groupField)
                .Select(view => view.ToCollection())
                .ToList();

            log.LogInformation("[ByAlbum] After filter: {Count} matching view collections", viewCollections.Count);

            if (viewCollections.Count == 0)
            {
                log.LogWarning("[ByAlbum] No matching dynamic browse view definitions were found.");
                return Results.Ok(new List<ContentGroupDto>());
            }

            var evaluator = new CollectionRuleEvaluator(db);
            var result = new List<ContentGroupDto>();

            using var conn = db.CreateConnection();

            foreach (var collection in viewCollections)
            {
                var predicates = CollectionRuleEvaluator.ParseRules(collection.RuleJson);
                if (predicates.Count == 0)
                {
                    continue;
                }

                // Evaluate collection rules to get entity_ids
                var entityIds = evaluator.Evaluate(predicates, collection.MatchMode, collection.SortField, collection.SortDirection);

                log.LogInformation("[ByAlbum] Collection '{CollectionName}' (groupByField={GroupByField}) matched {WorkCount} works from CollectionRuleEvaluator",
                    collection.DisplayName, collection.GroupByField, entityIds.Count);

                if (entityIds.Count == 0)
                {
                    continue;
                }

                var groupByField = collection.GroupByField!;

                // Determine primary media type from the collection's media_type predicate
                var primaryMediaType = predicates
                    .FirstOrDefault(p => p.Field.Equals("media_type", StringComparison.OrdinalIgnoreCase))
                    ?.Value ?? "Unknown";

                // Build IN clause for entity IDs
                var idList = string.Join(",", entityIds.Select(id => $"'{id}'"));

                // Group entity_ids by the group_by_field from canonical_values.
                // Many grouping fields (album, artist, show_name, series) are
                // Parent-scoped — after the retail/Wikidata workers run they are
                // stored on the topmost Work row (album, show, series), NOT on
                // the media_asset row.  The work_assets CTE therefore also
                // computes root_work_id (COALESCE up two parent_work_id hops),
                // and both asset_id and root_work_id are checked when joining
                // canonical_values so the grouping works regardless of whether
                // the field is Self-scoped (on the asset) or Parent-scoped (on
                // the topmost Work).
                using var cmd = conn.CreateCommand();
                var visibleAssetPredicate = HomeVisibilitySql.VisibleAssetPathPredicate("ma.file_path_root");
                cmd.CommandText = $"""
                    WITH work_assets AS (
                        SELECT
                            e.work_id,
                            ma.id AS asset_id,
                            COALESCE(gp.id, p.id, w.id) AS root_work_id
                        FROM editions e
                        INNER JOIN media_assets ma ON ma.edition_id = e.id
                        INNER JOIN works w  ON w.id  = e.work_id
                        LEFT  JOIN works p  ON p.id  = w.parent_work_id
                        LEFT  JOIN works gp ON gp.id = p.parent_work_id
                        WHERE e.work_id IN ({idList})
                          AND {visibleAssetPredicate}
                    ),
                    resolved_group_values AS (
                        SELECT
                            wa.work_id,
                            wa.asset_id,
                            wa.root_work_id,
                            CASE
                                WHEN @IsMusicAlbumGroup = 1 THEN COALESCE(
                                    (
                                        SELECT cv_parent_album.value
                                        FROM canonical_values cv_parent_album
                                        WHERE cv_parent_album.entity_id = wa.root_work_id
                                          AND cv_parent_album.key = 'album'
                                        LIMIT 1
                                    ),
                                    (
                                        SELECT cv_parent_title.value
                                        FROM canonical_values cv_parent_title
                                        WHERE cv_parent_title.entity_id = wa.root_work_id
                                          AND cv_parent_title.key = 'title'
                                        LIMIT 1
                                    ),
                                    (
                                        SELECT cv_asset_album.value
                                        FROM canonical_values cv_asset_album
                                        WHERE cv_asset_album.entity_id = wa.asset_id
                                          AND cv_asset_album.key = 'album'
                                        LIMIT 1
                                    )
                                )
                                ELSE cv_group.value
                            END AS group_name
                        FROM work_assets wa
                        LEFT JOIN canonical_values cv_group
                          ON @IsMusicAlbumGroup = 0
                         AND cv_group.key = @GroupField
                         AND (cv_group.entity_id = wa.asset_id
                              OR cv_group.entity_id = wa.root_work_id)
                    ),
                    grouped AS (
                        SELECT
                            rgv.group_name                          AS group_name,
                            COUNT(DISTINCT rgv.work_id)             AS work_count,
                            MIN(rgv.asset_id)                       AS first_asset_id,
                            MIN(rgv.root_work_id)                   AS first_root_work_id,
                            -- Count distinct albums for artist grouping (track_count = work_count)
                            COUNT(DISTINCT rgv.root_work_id)        AS album_count
                        FROM resolved_group_values rgv
                        WHERE rgv.group_name IS NOT NULL
                          AND TRIM(rgv.group_name) != ''
                        GROUP BY rgv.group_name
                    )
                    SELECT
                        g.group_name,
                        g.work_count,
                        CASE WHEN EXISTS (
                            SELECT 1 FROM canonical_values cv_cover_present
                            WHERE cv_cover_present.entity_id IN (g.first_root_work_id, g.first_asset_id)
                              AND cv_cover_present.key IN ('cover','cover_url','cover_width_px','cover_aspect_class')
                        ) THEN '/stream/' || g.first_asset_id || '/cover' END AS cover_url,
                        CASE WHEN EXISTS (
                            SELECT 1 FROM canonical_values cv_background_present
                            WHERE cv_background_present.entity_id IN (g.first_root_work_id, g.first_asset_id)
                              AND cv_background_present.key IN ('background','background_url','background_width_px','background_aspect_class')
                        ) THEN '/stream/' || g.first_asset_id || '/background' END AS background_url,
                        CASE WHEN EXISTS (
                            SELECT 1 FROM canonical_values cv_banner_present
                            WHERE cv_banner_present.entity_id IN (g.first_root_work_id, g.first_asset_id)
                              AND cv_banner_present.key IN ('banner','banner_url','banner_width_px','banner_aspect_class')
                        ) THEN '/stream/' || g.first_asset_id || '/banner' END AS banner_url,
                        NULL AS hero_url,
                        CASE WHEN EXISTS (
                            SELECT 1 FROM canonical_values cv_logo_present
                            WHERE cv_logo_present.entity_id IN (g.first_root_work_id, g.first_asset_id)
                              AND cv_logo_present.key IN ('logo','logo_url')
                        ) THEN '/stream/' || g.first_asset_id || '/logo' END AS logo_url,
                        COALESCE(
                            (
                                SELECT cv_creator.value
                                FROM canonical_values cv_creator
                                WHERE cv_creator.entity_id = g.first_root_work_id
                                  AND cv_creator.key IN ('artist','author')
                                LIMIT 1
                            ),
                            (
                                SELECT cv_creator2.value
                                FROM canonical_values cv_creator2
                                WHERE cv_creator2.entity_id = g.first_asset_id
                                  AND cv_creator2.key IN ('artist','author')
                                LIMIT 1
                            )
                        )                                           AS creator,
                        (
                            SELECT cv_net.value
                            FROM canonical_values cv_net
                            WHERE cv_net.entity_id = g.first_root_work_id
                              AND cv_net.key = 'network'
                            LIMIT 1
                        )                                           AS network,
                        COALESCE(
                            (
                                SELECT cv_year.value
                                FROM canonical_values cv_year
                                WHERE cv_year.entity_id = g.first_root_work_id
                                  AND cv_year.key = 'year'
                                LIMIT 1
                            ),
                            (
                                SELECT cv_year2.value
                                FROM canonical_values cv_year2
                                WHERE cv_year2.entity_id = g.first_asset_id
                                  AND cv_year2.key = 'year'
                                LIMIT 1
                            )
                        )                                           AS year,
                        COALESCE(
                            (
                                SELECT cv_desc.value
                                FROM canonical_values cv_desc
                                WHERE cv_desc.entity_id = g.first_root_work_id
                                  AND cv_desc.key = 'description'
                                LIMIT 1
                            ),
                            (
                                SELECT cv_desc2.value
                                FROM canonical_values cv_desc2
                                WHERE cv_desc2.entity_id = g.first_asset_id
                                  AND cv_desc2.key = 'description'
                                LIMIT 1
                            )
                        )                                           AS description,
                        (
                            SELECT cv_tagline.value
                            FROM canonical_values cv_tagline
                            WHERE cv_tagline.entity_id = g.first_root_work_id
                              AND cv_tagline.key = 'tagline'
                            LIMIT 1
                        )                                           AS tagline,
                        COALESCE(
                            (
                                SELECT cv_cover_aspect.value
                                FROM canonical_values cv_cover_aspect
                                WHERE cv_cover_aspect.entity_id = g.first_root_work_id
                                  AND cv_cover_aspect.key = 'cover_aspect_class'
                                LIMIT 1
                            ),
                            (
                                SELECT cv_cover_aspect2.value
                                FROM canonical_values cv_cover_aspect2
                                WHERE cv_cover_aspect2.entity_id = g.first_asset_id
                                  AND cv_cover_aspect2.key = 'cover_aspect_class'
                                LIMIT 1
                            )
                        )                                           AS cover_aspect_class,
                        COALESCE(
                            (
                                SELECT cv_square_aspect.value
                                FROM canonical_values cv_square_aspect
                                WHERE cv_square_aspect.entity_id = g.first_root_work_id
                                  AND cv_square_aspect.key = 'square_aspect_class'
                                LIMIT 1
                            ),
                            (
                                SELECT cv_square_aspect2.value
                                FROM canonical_values cv_square_aspect2
                                WHERE cv_square_aspect2.entity_id = g.first_asset_id
                                  AND cv_square_aspect2.key = 'square_aspect_class'
                                LIMIT 1
                            )
                        )                                           AS square_aspect_class,
                        COALESCE(
                            (
                                SELECT cv_background_aspect.value
                                FROM canonical_values cv_background_aspect
                                WHERE cv_background_aspect.entity_id = g.first_root_work_id
                                  AND cv_background_aspect.key = 'background_aspect_class'
                                LIMIT 1
                            ),
                            (
                                SELECT cv_background_aspect2.value
                                FROM canonical_values cv_background_aspect2
                                WHERE cv_background_aspect2.entity_id = g.first_asset_id
                                  AND cv_background_aspect2.key = 'background_aspect_class'
                                LIMIT 1
                            )
                        )                                           AS background_aspect_class,
                        COALESCE(
                            (
                                SELECT cv_banner_aspect.value
                                FROM canonical_values cv_banner_aspect
                                WHERE cv_banner_aspect.entity_id = g.first_root_work_id
                                  AND cv_banner_aspect.key = 'banner_aspect_class'
                                LIMIT 1
                            ),
                            (
                                SELECT cv_banner_aspect2.value
                                FROM canonical_values cv_banner_aspect2
                                WHERE cv_banner_aspect2.entity_id = g.first_asset_id
                                  AND cv_banner_aspect2.key = 'banner_aspect_class'
                                LIMIT 1
                            )
                        )                                           AS banner_aspect_class,
                        COALESCE(
                            (
                                SELECT cv_cover_width.value
                                FROM canonical_values cv_cover_width
                                WHERE cv_cover_width.entity_id = g.first_root_work_id
                                  AND cv_cover_width.key = 'cover_width_px'
                                LIMIT 1
                            ),
                            (
                                SELECT cv_cover_width2.value
                                FROM canonical_values cv_cover_width2
                                WHERE cv_cover_width2.entity_id = g.first_asset_id
                                  AND cv_cover_width2.key = 'cover_width_px'
                                LIMIT 1
                            )
                        )                                           AS cover_width_px,
                        COALESCE(
                            (
                                SELECT cv_cover_height.value
                                FROM canonical_values cv_cover_height
                                WHERE cv_cover_height.entity_id = g.first_root_work_id
                                  AND cv_cover_height.key = 'cover_height_px'
                                LIMIT 1
                            ),
                            (
                                SELECT cv_cover_height2.value
                                FROM canonical_values cv_cover_height2
                                WHERE cv_cover_height2.entity_id = g.first_asset_id
                                  AND cv_cover_height2.key = 'cover_height_px'
                                LIMIT 1
                            )
                        )                                           AS cover_height_px,
                        COALESCE(
                            (
                                SELECT cv_square_width.value
                                FROM canonical_values cv_square_width
                                WHERE cv_square_width.entity_id = g.first_root_work_id
                                  AND cv_square_width.key = 'square_width_px'
                                LIMIT 1
                            ),
                            (
                                SELECT cv_square_width2.value
                                FROM canonical_values cv_square_width2
                                WHERE cv_square_width2.entity_id = g.first_asset_id
                                  AND cv_square_width2.key = 'square_width_px'
                                LIMIT 1
                            )
                        )                                           AS square_width_px,
                        COALESCE(
                            (
                                SELECT cv_square_height.value
                                FROM canonical_values cv_square_height
                                WHERE cv_square_height.entity_id = g.first_root_work_id
                                  AND cv_square_height.key = 'square_height_px'
                                LIMIT 1
                            ),
                            (
                                SELECT cv_square_height2.value
                                FROM canonical_values cv_square_height2
                                WHERE cv_square_height2.entity_id = g.first_asset_id
                                  AND cv_square_height2.key = 'square_height_px'
                                LIMIT 1
                            )
                        )                                           AS square_height_px,
                        COALESCE(
                            (
                                SELECT cv_background_width.value
                                FROM canonical_values cv_background_width
                                WHERE cv_background_width.entity_id = g.first_root_work_id
                                  AND cv_background_width.key = 'background_width_px'
                                LIMIT 1
                            ),
                            (
                                SELECT cv_background_width2.value
                                FROM canonical_values cv_background_width2
                                WHERE cv_background_width2.entity_id = g.first_asset_id
                                  AND cv_background_width2.key = 'background_width_px'
                                LIMIT 1
                            )
                        )                                           AS background_width_px,
                        COALESCE(
                            (
                                SELECT cv_background_height.value
                                FROM canonical_values cv_background_height
                                WHERE cv_background_height.entity_id = g.first_root_work_id
                                  AND cv_background_height.key = 'background_height_px'
                                LIMIT 1
                            ),
                            (
                                SELECT cv_background_height2.value
                                FROM canonical_values cv_background_height2
                                WHERE cv_background_height2.entity_id = g.first_asset_id
                                  AND cv_background_height2.key = 'background_height_px'
                                LIMIT 1
                            )
                        )                                           AS background_height_px,
                        COALESCE(
                            (
                                SELECT cv_banner_width.value
                                FROM canonical_values cv_banner_width
                                WHERE cv_banner_width.entity_id = g.first_root_work_id
                                  AND cv_banner_width.key = 'banner_width_px'
                                LIMIT 1
                            ),
                            (
                                SELECT cv_banner_width2.value
                                FROM canonical_values cv_banner_width2
                                WHERE cv_banner_width2.entity_id = g.first_asset_id
                                  AND cv_banner_width2.key = 'banner_width_px'
                                LIMIT 1
                            )
                        )                                           AS banner_width_px,
                        COALESCE(
                            (
                                SELECT cv_banner_height.value
                                FROM canonical_values cv_banner_height
                                WHERE cv_banner_height.entity_id = g.first_root_work_id
                                  AND cv_banner_height.key = 'banner_height_px'
                                LIMIT 1
                            ),
                            (
                                SELECT cv_banner_height2.value
                                FROM canonical_values cv_banner_height2
                                WHERE cv_banner_height2.entity_id = g.first_asset_id
                                  AND cv_banner_height2.key = 'banner_height_px'
                                LIMIT 1
                            )
                        )                                           AS banner_height_px,
                        (
                            SELECT COUNT(DISTINCT cv_sn.value)
                            FROM work_assets wa_sn
                            INNER JOIN canonical_values cv_sn
                              ON cv_sn.entity_id = wa_sn.asset_id
                            WHERE wa_sn.root_work_id = g.first_root_work_id
                              AND cv_sn.key = 'season_number'
                        )                                           AS season_count,
                        g.album_count
                    FROM grouped g
                    ORDER BY g.group_name
                    """;

                var gp = cmd.CreateParameter();
                gp.ParameterName = "@GroupField";
                gp.Value = groupByField;
                cmd.Parameters.Add(gp);

                var isMusicAlbumGroup = cmd.CreateParameter();
                isMusicAlbumGroup.ParameterName = "@IsMusicAlbumGroup";
                isMusicAlbumGroup.Value = primaryMediaType.Equals("Music", StringComparison.OrdinalIgnoreCase)
                    && groupByField.Equals("album", StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0;
                cmd.Parameters.Add(isMusicAlbumGroup);

                // Collect rows first so we can close the reader before doing async person lookups.
                var rows = new List<(string GroupName, int WorkCount, string? CoverUrl, string? BackgroundUrl, string? BannerUrl, string? HeroUrl, string? LogoUrl, string? Creator, string? Network, string? Year, string? Description, string? Tagline, string? CoverAspectClass, string? SquareAspectClass, string? BackgroundAspectClass, string? BannerAspectClass, int? CoverWidthPx, int? CoverHeightPx, int? SquareWidthPx, int? SquareHeightPx, int? BackgroundWidthPx, int? BackgroundHeightPx, int? BannerWidthPx, int? BannerHeightPx, int? SeasonCount, int AlbumCount)>();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var groupName = reader.IsDBNull(0) ? null : reader.GetString(0);
                        if (string.IsNullOrWhiteSpace(groupName))
                        {
                            continue;
                        }

                        rows.Add((
                            groupName,
                            reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                            reader.IsDBNull(2) ? null : reader.GetString(2),
                            reader.IsDBNull(3) ? null : reader.GetString(3),
                            reader.IsDBNull(4) ? null : reader.GetString(4),
                            reader.IsDBNull(5) ? null : reader.GetString(5),
                            reader.IsDBNull(6) ? null : reader.GetString(6),
                            reader.IsDBNull(7) ? null : reader.GetString(7),
                            reader.IsDBNull(8) ? null : reader.GetString(8),
                            reader.IsDBNull(9) ? null : reader.GetString(9),
                            reader.IsDBNull(10) ? null : reader.GetString(10),
                            reader.IsDBNull(11) ? null : reader.GetString(11),
                            reader.IsDBNull(12) ? null : reader.GetString(12),
                            reader.IsDBNull(13) ? null : reader.GetString(13),
                            reader.IsDBNull(14) ? null : reader.GetString(14),
                            reader.IsDBNull(15) ? null : reader.GetString(15),
                            ReadNullableInt(reader, 16),
                            ReadNullableInt(reader, 17),
                            ReadNullableInt(reader, 18),
                            ReadNullableInt(reader, 19),
                            ReadNullableInt(reader, 20),
                            ReadNullableInt(reader, 21),
                            ReadNullableInt(reader, 22),
                            ReadNullableInt(reader, 23),
                            reader.IsDBNull(24) ? null : (int?)reader.GetInt32(24),
                            reader.IsDBNull(25) ? 0 : reader.GetInt32(25)
                        ));
                    }
                }

                log.LogInformation("[ByAlbum] SQL grouping query for collection '{CollectionName}' (groupByField={GroupByField}) returned {RowCount} distinct group(s)",
                    collection.DisplayName, groupByField, rows.Count);

                var isArtistGroup = groupByField.Equals("artist", StringComparison.OrdinalIgnoreCase);

                foreach (var row in rows)
                {
                    string? artistPhotoUrl = null;
                    Guid? artistPersonId = null;

                    if (isArtistGroup && !string.IsNullOrWhiteSpace(row.GroupName))
                    {
                        try
                        {
                            var person = await personRepo.FindByNameAsync(row.GroupName, ct);
                            if (person is not null)
                            {
                                artistPersonId = person.Id;
                                if (!string.IsNullOrEmpty(person.LocalHeadshotPath) || !string.IsNullOrEmpty(person.HeadshotUrl))
                                {
                                    artistPhotoUrl = $"/persons/{person.Id}/headshot";
                                }
                            }
                        }
                        catch { /* best-effort — missing photo is fine */ }
                    }

                    result.Add(new ContentGroupDto
                    {
                        CollectionId = collection.Id,
                        DisplayName = row.GroupName,
                        WikidataQid = null,
                        PrimaryMediaType = primaryMediaType,
                        WorkCount = row.WorkCount,
                        DistinctTitleCount = row.WorkCount,
                        CoverUrl = row.CoverUrl,
                        BackgroundUrl = row.BackgroundUrl,
                        BannerUrl = row.BannerUrl,
                        HeroUrl = row.HeroUrl,
                        LogoUrl = row.LogoUrl,
                        CoverAspectClass = row.CoverAspectClass,
                        SquareAspectClass = row.SquareAspectClass,
                        BackgroundAspectClass = row.BackgroundAspectClass,
                        BannerAspectClass = row.BannerAspectClass,
                        CoverWidthPx = row.CoverWidthPx,
                        CoverHeightPx = row.CoverHeightPx,
                        SquareWidthPx = row.SquareWidthPx,
                        SquareHeightPx = row.SquareHeightPx,
                        BackgroundWidthPx = row.BackgroundWidthPx,
                        BackgroundHeightPx = row.BackgroundHeightPx,
                        BannerWidthPx = row.BannerWidthPx,
                        BannerHeightPx = row.BannerHeightPx,
                        Description = row.Description,
                        Tagline = row.Tagline,
                        Creator = row.Creator,
                        UniverseStatus = "Complete",
                        CreatedAt = collection.CreatedAt,
                        ArtistPhotoUrl = artistPhotoUrl,
                        ArtistPersonId = artistPersonId,
                        Network = row.Network,
                        Year = row.Year,
                        SeasonCount = row.SeasonCount,
                        AlbumCount = row.AlbumCount > 0 ? row.AlbumCount : null,
                    });
                }
            }

            if (result.Count == 0 && IsMusicSystemView(mediaType, groupField))
            {
                var fallbackGroups = await BuildMusicSystemViewFallbackGroupsAsync(conn, personRepo, groupField!, log, ct);
                result.AddRange(fallbackGroups);
            }

            log.LogInformation("[ByAlbum] Returning {Total} content groups for mediaType={MediaType} groupField={GroupField}",
                result.Count, mediaType ?? "(none)", groupField ?? "(none)");

            var normalizedGroups = NormalizeSystemViewGroups(result, mediaType, groupField);
            return Results.Ok(normalizedGroups.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList());
        })
        .WithName("GetSystemViewGroups")
        .WithSummary("Resolves built-in browse views (By Show, By Artist, By Album) as dynamic content groups for the library container views.")
        .Produces<List<ContentGroupDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // ── Managed Collection endpoints (managed collections surface) ──────────────────────────────

        // GET /collections/managed — all non-Universe collections for the managed collections surface.
        group.MapGet("/managed", async (
            Guid? profileId,
            ICollectionRepository collectionRepo,
            IProfileRepository profileRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var collections = await collectionRepo.GetManagedCollectionsAsync(ct);
            var accessibleCollections = collections
                .Where(collection => CollectionAccessPolicy.CanAccess(collection, activeProfile))
                .ToList();
            var curatedCountByCollection = await collectionRepo.GetCollectionItemCountsAsync(
                accessibleCollections
                    .Where(collection => !string.Equals(collection.Resolution, "query", StringComparison.OrdinalIgnoreCase))
                    .Select(collection => collection.Id),
                ct);
            var dtos = new List<ManagedCollectionDto>();
            foreach (var collection in accessibleCollections)
            {
                var count = await GetManagedCollectionItemCountAsync(collection, collectionRepo, db, curatedCountByCollection, ct);
                dtos.Add(ManagedCollectionDto.FromDomain(collection, count, activeProfile));
            }

            return Results.Ok(dtos);
        })
        .WithName("GetManagedCollections")
        .WithSummary("List authored collections accessible to the active profile.")
        .Produces<List<ManagedCollectionDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // GET /collections/management-catalog — classified collection catalog for the Collections hub.
        group.MapGet("/management-catalog", async (
            Guid? profileId,
            IProfileRepository profileRepo,
            CollectionCatalogReadService catalogReadService,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var catalog = await catalogReadService.GetManagementCatalogAsync(activeProfile, ct);
            return Results.Ok(catalog);
        })
        .WithName("GetCollectionManagementCatalog")
        .WithSummary("Returns all collections visible to the active profile with server-side family and lane classification for the Collections hub.")
        .Produces<List<CollectionManagementCatalogDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // GET /collections/managed/counts — type → count for stats bar.
        group.MapPost("/reconcile", async (
            CollectionBackfillRequest? body,
            CollectionBackfillService backfillService,
            CancellationToken ct) =>
        {
            var result = await backfillService.RunAsync(body ?? new CollectionBackfillRequest(), ct);
            return Results.Ok(result);
        })
        .WithName("ReconcileCollections")
        .WithSummary("Repairs missing collection shelf assignments for already-ingested media.")
        .Produces<CollectionBackfillResult>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        group.MapGet("/managed/counts", async (
            Guid? profileId,
            ICollectionRepository collectionRepo,
            IProfileRepository profileRepo,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var counts = (await collectionRepo.GetManagedCollectionsAsync(ct))
                .Where(collection => CollectionAccessPolicy.CanAccess(collection, activeProfile))
                .GroupBy(collection => collection.CollectionType)
                .ToDictionary(grouping => grouping.Key, grouping => grouping.Count());
            return Results.Ok(counts);
        })
        .WithName("GetManagedCollectionCounts")
        .WithSummary("Returns authored collection count grouped by type for the active profile.")
        .Produces<Dictionary<string, int>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // GET /collections/{id}/items?limit=20 — curated item preview.
        group.MapGet("/media-lookup", async (
            string? q,
            string? mediaTypes,
            Guid? collectionId,
            int? offset,
            int? limit,
            Guid? profileId,
            ICollectionRepository collectionRepo,
            IProfileRepository profileRepo,
            ICollectionMediaLookupReadService mediaLookupReadService,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            HashSet<Guid> existingWorkIds = [];
            if (collectionId.HasValue)
            {
                var collection = await collectionRepo.GetByIdAsync(collectionId.Value, ct);
                if (collection is null)
                {
                    return Results.NotFound();
                }

                if (!CollectionAccessPolicy.CanAccess(collection, activeProfile))
                {
                    return Results.Forbid();
                }

                var existingItems = await collectionRepo.GetCollectionItemsAsync(collectionId.Value, 1000, ct);
                existingWorkIds = (await GetCollectionCatalogDisplayWorkIdsAsync(
                        existingItems.Select(item => item.WorkId),
                        db,
                        ct))
                    .ToHashSet();
            }

            var results = await mediaLookupReadService.LookupAsync(q, mediaTypes, existingWorkIds, offset, limit, ct);
            return Results.Ok(results);
        })
        .WithName("LookupCollectionMedia")
        .WithSummary("Searches local owned media for curated collection membership.")
        .Produces<List<CollectionMediaLookupDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapGet("/{id:guid}/summary", async (
            Guid id,
            Guid? profileId,
            IProfileRepository profileRepo,
            CollectionCatalogReadService catalogReadService,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var summary = await catalogReadService.GetSummaryAsync(id, activeProfile, ct);
            return summary is null ? Results.NotFound() : Results.Ok(summary);
        })
        .WithName("GetCollectionSummary")
        .WithSummary("Returns the Collections hub summary for one visible collection.")
        .Produces<CollectionManagementCatalogDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{id:guid}/items", async (
            Guid id,
            int? limit,
            IProfileRepository profileRepo,
            Guid? profileId,
            CollectionCatalogReadService catalogReadService,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var take = limit is > 0 ? limit.Value : 20;
            var result = await catalogReadService.GetItemsAsync(id, activeProfile, take, ct);
            if (!result.Found)
            {
                return Results.NotFound();
            }

            if (result.Forbidden)
            {
                return Results.Forbid();
            }

            return Results.Ok(result.Items);
        })
        .WithName("GetCollectionItems")
        .WithSummary("Returns curated items for a collection with resolved work metadata.")
        .Produces<List<CollectionItemDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapPost("/{id:guid}/items", async (
            Guid id,
            CollectionItemAddRequest body,
            ICollectionRepository collectionRepo,
            IProfileRepository profileRepo,
            Guid? profileId,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var collection = await collectionRepo.GetByIdAsync(id, ct);
            if (collection is null)
            {
                return Results.NotFound();
            }

            if (!CollectionAccessPolicy.CanEdit(collection, activeProfile))
            {
                return Results.Forbid();
            }

            if (!CollectionAccessPolicy.IsManagedCollectionType(collection.CollectionType)
                || !string.Equals(collection.Resolution, "materialized", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest("Only saved/manual collections support direct item membership.");
            }
            if (body.WorkId == Guid.Empty)
            {
                return Results.BadRequest("work_id is required.");
            }

            var collectionWorkId = await ResolveCollectionMembershipWorkIdAsync(body.WorkId, db, ct);
            var existingItems = await collectionRepo.GetCollectionItemsAsync(id, 1000, ct);
            var existingDisplayWorkIds = await GetCollectionCatalogDisplayWorkIdsAsync(existingItems.Select(item => item.WorkId), db, ct);
            if (existingDisplayWorkIds.Contains(collectionWorkId))
            {
                return Results.Ok();
            }

            var nextSortOrder = existingItems.Count == 0
                ? 1
                : existingItems.Max(item => item.SortOrder) + 1;

            await collectionRepo.AddCollectionItemAsync(new CollectionItem
            {
                Id = Guid.NewGuid(),
                CollectionId = id,
                WorkId = collectionWorkId,
                SortOrder = nextSortOrder,
                AddedAt = DateTimeOffset.UtcNow,
            }, ct);

            return Results.Ok();
        })
        .WithName("AddCollectionItem")
        .WithSummary("Adds a work to a saved/manual collection.")
        .RequireAnyRole();

        group.MapDelete("/{id:guid}/items/{itemId:guid}", async (
            Guid id,
            Guid itemId,
            ICollectionRepository collectionRepo,
            IProfileRepository profileRepo,
            Guid? profileId,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var collection = await collectionRepo.GetByIdAsync(id, ct);
            if (collection is null)
            {
                return Results.NotFound();
            }

            if (!CollectionAccessPolicy.CanEdit(collection, activeProfile))
            {
                return Results.Forbid();
            }

            if (!CollectionAccessPolicy.IsManagedCollectionType(collection.CollectionType)
                || !string.Equals(collection.Resolution, "materialized", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest("Only saved/manual collections support direct item membership.");
            }

            var existingItems = await collectionRepo.GetCollectionItemsAsync(id, 1000, ct);
            if (!existingItems.Any(item => item.Id == itemId))
            {
                return Results.NotFound();
            }

            await collectionRepo.RemoveCollectionItemAsync(itemId, ct);
            return Results.Ok();
        })
        .WithName("RemoveCollectionItem")
        .WithSummary("Removes a work from a saved/manual collection.")
        .RequireAnyRole();

        group.MapPut("/{id:guid}/items/reorder", async (
            Guid id,
            CollectionItemReorderRequest body,
            ICollectionRepository collectionRepo,
            IProfileRepository profileRepo,
            Guid? profileId,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var collection = await collectionRepo.GetByIdAsync(id, ct);
            if (collection is null)
            {
                return Results.NotFound();
            }

            if (!CollectionAccessPolicy.CanEdit(collection, activeProfile))
            {
                return Results.Forbid();
            }

            if (!CollectionAccessPolicy.IsManagedCollectionType(collection.CollectionType)
                || !string.Equals(collection.Resolution, "materialized", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest("Only saved/manual collections support direct item ordering.");
            }

            var requestedIds = body.ItemIds.Where(itemId => itemId != Guid.Empty).Distinct().ToList();
            var existingItems = await collectionRepo.GetCollectionItemsAsync(id, 1000, ct);
            var existingIds = existingItems.Select(item => item.Id).ToHashSet();
            if (requestedIds.Count != existingItems.Count || requestedIds.Any(itemId => !existingIds.Contains(itemId)))
            {
                return Results.BadRequest("item_ids must include every item in this collection exactly once.");
            }

            await collectionRepo.ReorderCollectionItemsAsync(id, requestedIds, ct);
            return Results.Ok();
        })
        .WithName("ReorderCollectionItems")
        .WithSummary("Reorders saved/manual collection items.")
        .RequireAnyRole();

        // GET /collections/{id}/square-artwork — serve collection-owned square artwork.
        group.MapGet("/{id:guid}/square-artwork", async (
            Guid id,
            ICollectionRepository collectionRepo,
            IProfileRepository profileRepo,
            Guid? profileId,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var collection = await collectionRepo.GetByIdAsync(id, ct);
            if (collection is null)
            {
                return Results.NotFound();
            }

            if (!CollectionAccessPolicy.CanAccess(collection, activeProfile))
            {
                return Results.Forbid();
            }

            if (string.IsNullOrWhiteSpace(collection.SquareArtworkPath) || !File.Exists(collection.SquareArtworkPath))
            {
                return Results.NotFound();
            }

            var bytes = await File.ReadAllBytesAsync(collection.SquareArtworkPath, ct);
            return Results.File(
                bytes,
                string.IsNullOrWhiteSpace(collection.SquareArtworkMimeType)
                    ? GetCollectionArtworkMimeType(collection.SquareArtworkPath)
                    : collection.SquareArtworkMimeType,
                Path.GetFileName(collection.SquareArtworkPath));
        })
        .WithName("GetCollectionSquareArtwork")
        .WithSummary("Serves custom square artwork for a collection.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        // POST /collections/{id}/square-artwork — upload collection-owned square artwork.
        group.MapPost("/{id:guid}/square-artwork", async (
            Guid id,
            HttpRequest request,
            ICollectionRepository collectionRepo,
            IProfileRepository profileRepo,
            TuvimaDataPaths dataPaths,
            Guid? profileId,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var collection = await collectionRepo.GetByIdAsync(id, ct);
            if (collection is null)
            {
                return Results.NotFound();
            }

            if (!CollectionAccessPolicy.IsManagedCollectionType(collection.CollectionType))
            {
                return Results.BadRequest($"Collection type '{collection.CollectionType}' is browse-only and cannot be edited here.");
            }

            if (!CollectionAccessPolicy.CanEdit(collection, activeProfile))
            {
                return Results.Forbid();
            }

            if (!request.HasFormContentType)
            {
                return Results.BadRequest("Expected multipart form data.");
            }

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest("No file uploaded.");
            }

            if (file.Length > 5 * 1024 * 1024)
            {
                return Results.BadRequest("Artwork must be 5 MB or smaller.");
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var mimeType = NormalizeCollectionArtworkMimeType(file.ContentType, extension);
            if (mimeType is null)
            {
                return Results.BadRequest("Artwork must be a JPEG or PNG image.");
            }

            dataPaths.EnsureRootExists();
            var directory = Path.Combine(dataPaths.Root, "collections", id.ToString("D"));
            Directory.CreateDirectory(directory);
            var targetPath = Path.Combine(directory, $"square{extension}");

            if (!string.IsNullOrWhiteSpace(collection.SquareArtworkPath)
                && !string.Equals(collection.SquareArtworkPath, targetPath, StringComparison.OrdinalIgnoreCase)
                && File.Exists(collection.SquareArtworkPath))
            {
                File.Delete(collection.SquareArtworkPath);
            }

            await using (var stream = File.Create(targetPath))
            await using (var upload = file.OpenReadStream())
            {
                await upload.CopyToAsync(stream, ct);
            }

            await collectionRepo.UpdateCollectionSquareArtworkAsync(id, targetPath, mimeType, ct);
            return Results.Ok(new { square_artwork_url = $"/collections/{id}/square-artwork" });
        })
        .WithName("UploadCollectionSquareArtwork")
        .WithSummary("Uploads custom square artwork for a managed collection.")
        .DisableAntiforgery()
        .RequireAnyRole();

        // DELETE /collections/{id}/square-artwork — clear collection-owned square artwork.
        group.MapDelete("/{id:guid}/square-artwork", async (
            Guid id,
            ICollectionRepository collectionRepo,
            IProfileRepository profileRepo,
            Guid? profileId,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var collection = await collectionRepo.GetByIdAsync(id, ct);
            if (collection is null)
            {
                return Results.NotFound();
            }

            if (!CollectionAccessPolicy.IsManagedCollectionType(collection.CollectionType))
            {
                return Results.BadRequest($"Collection type '{collection.CollectionType}' is browse-only and cannot be edited here.");
            }

            if (!CollectionAccessPolicy.CanEdit(collection, activeProfile))
            {
                return Results.Forbid();
            }

            if (!string.IsNullOrWhiteSpace(collection.SquareArtworkPath) && File.Exists(collection.SquareArtworkPath))
            {
                File.Delete(collection.SquareArtworkPath);
            }

            await collectionRepo.UpdateCollectionSquareArtworkAsync(id, null, null, ct);
            return Results.Ok();
        })
        .WithName("DeleteCollectionSquareArtwork")
        .WithSummary("Clears custom square artwork for a managed collection.")
        .RequireAnyRole();

        // PUT /collections/{id}/enabled — toggle collection visibility.
        group.MapPut("/{id:guid}/enabled", async (
            Guid id,
            EnabledRequest body,
            ICollectionRepository collectionRepo,
            IProfileRepository profileRepo,
            Guid? profileId,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var collection = await collectionRepo.GetByIdAsync(id, ct);
            if (collection is null)
            {
                return Results.NotFound();
            }

            if (!CollectionAccessPolicy.CanEdit(collection, activeProfile))
            {
                return Results.Forbid();
            }

            await collectionRepo.UpdateCollectionEnabledAsync(id, body.Enabled, ct);
            return Results.Ok();
        })
        .WithName("UpdateCollectionEnabled")
        .WithSummary("Toggle a collection's enabled state.")
        .RequireAnyRole();

        // PUT /collections/{id}/featured — toggle collection featured state.
        group.MapPut("/{id:guid}/featured", async (
            Guid id,
            FeaturedRequest body,
            ICollectionRepository collectionRepo,
            IProfileRepository profileRepo,
            Guid? profileId,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var collection = await collectionRepo.GetByIdAsync(id, ct);
            if (collection is null)
            {
                return Results.NotFound();
            }

            if (!CollectionAccessPolicy.CanEdit(collection, activeProfile))
            {
                return Results.Forbid();
            }

            await collectionRepo.UpdateCollectionFeaturedAsync(id, body.Featured, ct);
            return Results.Ok();
        })
        .WithName("UpdateCollectionFeatured")
        .WithSummary("Toggle a collection's featured state.")
        .RequireAnyRole();

        // ── Parameterized Collection endpoints ─────────────────────────────────────────

        // GET /collections/resolve/{id}?limit= — evaluate collection rules, return items
        group.MapGet("/resolve/{id:guid}", async (
            Guid id,
            int? limit,
            ICollectionRepository collectionRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var collection = await collectionRepo.GetByIdAsync(id, ct);
            if (collection is null)
            {
                return Results.NotFound();
            }

            // For materialized collections, return works directly
            if (collection.Resolution == "materialized")
            {
                var collectionWithWorks = await collectionRepo.GetCollectionWithWorksAsync(id, ct);
                if (collectionWithWorks is null)
                {
                    return Results.NotFound();
                }

                var take = limit ?? 0;
                var works = take > 0 ? collectionWithWorks.Works.Take(take).ToList() : collectionWithWorks.Works;
                var items = works.Select(w =>
                {
                    var dto = WorkDto.FromDomain(w);
                    return new CollectionResolvedItemDto
                    {
                        EntityId = w.Id,
                        Title = GetCanonical(dto, "title") ?? $"Work {w.Id.ToString("N")[..8]}",
                        Creator = GetCanonical(dto, "author") ?? GetCanonical(dto, "director") ?? GetCanonical(dto, "artist"),
                        MediaType = w.MediaType.ToString(),
                        CoverUrl = BuildCoverStreamUrl(w),
                        Year = GetCanonical(dto, "year"),
                    };
                }).ToList();

                return Results.Ok(items);
            }

            // For query-resolved collections, evaluate rules
            var predicates = CollectionRuleEvaluator.ParseRules(collection.RuleJson);
            if (predicates.Count == 0)
            {
                return Results.Ok(new List<CollectionResolvedItemDto>());
            }

            var evaluator = new CollectionRuleEvaluator(db);
            var entityIds = evaluator.Evaluate(predicates, collection.MatchMode, collection.SortField, collection.SortDirection, limit ?? 0);

            var resolved = ResolveEntityMetadata(db, entityIds);
            return Results.Ok(resolved);
        })
        .WithName("ResolveCollection")
        .WithSummary("Evaluate a collection's rules and return matching items.")
        .Produces<List<CollectionResolvedItemDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        // GET /collections/resolve/by-name?name=All%20Songs&limit=200
        // Resolves a System collection by display name and returns matching items.
        // Unlike /library/items, this path bypasses the libraryItem visibility filter so
        // items that are still in the pipeline (no QID, no review) are included.
        // Used by the library flat views (All Songs) to show music even before the
        // retail/Wikidata pipeline completes.  Fields are read from both the asset-level
        // and the root parent Work-level canonical_values rows so that parent-scoped
        // fields (artist, album, cover_url) are correctly resolved.
        group.MapGet("/resolve/by-name", async (
            string? name,
            int? limit,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.BadRequest("name parameter is required");
            }

            var definition = BuiltInBrowseCollectionCatalog.FindByName(name);
            var collection = definition?.ToCollection();

            if (collection is null)
            {
                return Results.NotFound($"No dynamic browse view found with name '{name}'");
            }

            var predicates = CollectionRuleEvaluator.ParseRules(collection.RuleJson);
            if (predicates.Count == 0)
            {
                return Results.Ok(new List<CollectionResolvedItemDto>());
            }

            var evaluator = new CollectionRuleEvaluator(db);
            var entityIds = evaluator.Evaluate(
                predicates, collection.MatchMode, collection.SortField, collection.SortDirection, limit ?? 200);

            var resolved = ResolveEntityMetadataWithLineage(db, entityIds);
            return Results.Ok(resolved);
        })
        .WithName("ResolveCollectionByName")
        .WithSummary("Resolves a System collection by display name and returns items, reading both asset-level and parent-Work-level canonical values. Bypasses the libraryItem visibility filter so in-flight items are included.")
        .Produces<List<CollectionResolvedItemDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        // GET /collections/by-location/{location} — collections placed at a location
        group.MapGet("/by-location/{location}", async (
            string location,
            ICollectionPlacementRepository placementRepo,
            ICollectionRepository collectionRepo,
            CancellationToken ct) =>
        {
            var placements = await placementRepo.GetByLocationAsync(location, ct);
            var result = new List<object>();

            foreach (var p in placements)
            {
                var collection = await collectionRepo.GetByIdAsync(p.CollectionId, ct);
                if (collection is null || !collection.IsEnabled)
                {
                    continue;
                }

                result.Add(new
                {
                    collection_id = collection.Id,
                    name = collection.DisplayName ?? $"Collection {collection.Id.ToString("N")[..8]}",
                    collection_type = collection.CollectionType,
                    icon_name = collection.IconName,
                    location = p.Location,
                    position = p.Position,
                    display_limit = p.DisplayLimit,
                    display_mode = p.DisplayMode,
                });
            }

            return Results.Ok(result);
        })
        .WithName("GetCollectionsByLocation")
        .WithSummary("Returns all collections placed at a specific UI location, ordered by position.")
        .RequireAnyRole();

        // POST /collections/preview — evaluate rules without saving
        group.MapPost("/preview", (
            CollectionPreviewRequest body,
            IDatabaseConnection db) =>
        {
            if (body.Rules.Count == 0)
            {
                return Results.Ok(new { count = 0, items = new List<CollectionResolvedItemDto>() });
            }

            var evaluator = new CollectionRuleEvaluator(db);
            var entityIds = evaluator.Evaluate(body.Rules, body.MatchMode, limit: body.Limit > 0 ? body.Limit : 20);

            var resolved = ResolveEntityMetadata(db, entityIds);
            return Results.Ok(new { count = entityIds.Count, items = resolved });
        })
        .WithName("PreviewCollection")
        .WithSummary("Evaluate collection rules and return matching items without saving.")
        .RequireAnyRole();

        // POST /collections — create a new collection
        group.MapPost("/", async (
            CollectionCreateRequest body,
            Guid? profileId,
            ICollectionRepository collectionRepo,
            ICollectionPlacementRepository placementRepo,
            IProfileRepository profileRepo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
            {
                return Results.BadRequest("Collection name is required.");
            }

            if (!CollectionAccessPolicy.IsManagedCollectionType(body.CollectionType))
            {
                return Results.BadRequest($"Collection type '{body.CollectionType}' is reserved for browse-only system data.");
            }

            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            if (activeProfile is null)
            {
                return Results.BadRequest("profileId is required to create a collection.");
            }

            var normalizedVisibility = CollectionAccessPolicy.NormalizeVisibility(body.Visibility);
            if (string.Equals(normalizedVisibility, CollectionAccessPolicy.SharedVisibility, StringComparison.OrdinalIgnoreCase)
                && !CollectionAccessPolicy.CanManageSharedCollections(activeProfile))
            {
                return Results.Forbid();
            }

            var ruleJson = body.Rules.Count > 0
                ? System.Text.Json.JsonSerializer.Serialize(body.Rules)
                : null;

            var ruleHash = body.Rules.Count > 0
                ? CollectionRuleEvaluator.ComputeRuleHash(body.Rules)
                : null;

            var resolution = body.CollectionType is "Playlist" || body.Rules.Count == 0
                ? "materialized"
                : "query";

            var collection = new Collection
            {
                Id = Guid.NewGuid(),
                DisplayName = body.Name,
                Description = body.Description,
                IconName = body.IconName,
                CollectionType = body.CollectionType,
                IsEnabled = true,
                MinItems = 0,
                RuleJson = ruleJson,
                Resolution = resolution,
                RuleHash = ruleHash,
                MatchMode = body.MatchMode,
                SortField = body.SortField,
                SortDirection = body.SortDirection,
                LiveUpdating = resolution == "query" && body.LiveUpdating,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            CollectionAccessPolicy.ApplyVisibility(collection, normalizedVisibility, activeProfile.Id);

            await collectionRepo.UpsertAsync(collection, ct);

            // Create placements
            if (body.Placements is { Count: > 0 })
            {
                foreach (var p in body.Placements)
                {
                    await placementRepo.UpsertAsync(new CollectionPlacement
                    {
                        Id = Guid.NewGuid(),
                        CollectionId = collection.Id,
                        Location = p.Location,
                        Position = p.Position,
                        DisplayLimit = p.DisplayLimit,
                        DisplayMode = p.DisplayMode,
                        IsVisible = true,
                        CreatedAt = DateTimeOffset.UtcNow,
                    }, ct);
                }
            }

            return Results.Created($"/collections/{collection.Id}", new { id = collection.Id, name = collection.DisplayName });
        })
        .WithName("CreateCollection")
        .WithSummary("Create a new collection with rules and optional placements.")
        .RequireAnyRole();

        // PUT /collections/{id} — update collection
        group.MapPut("/{id:guid}", async (
            Guid id,
            CollectionUpdateRequest body,
            ICollectionRepository collectionRepo,
            IProfileRepository profileRepo,
            Guid? profileId,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var collection = await collectionRepo.GetByIdAsync(id, ct);
            if (collection is null)
            {
                return Results.NotFound();
            }

            if (!CollectionAccessPolicy.IsManagedCollectionType(collection.CollectionType))
            {
                return Results.BadRequest($"Collection type '{collection.CollectionType}' is browse-only and cannot be edited here.");
            }

            if (!CollectionAccessPolicy.CanEdit(collection, activeProfile))
            {
                return Results.Forbid();
            }

            if (body.Name is not null)
            {
                collection.DisplayName = body.Name;
            }

            if (body.Description is not null)
            {
                collection.Description = body.Description;
            }

            if (body.IconName is not null)
            {
                collection.IconName = body.IconName;
            }

            if (body.MatchMode is not null)
            {
                collection.MatchMode = body.MatchMode;
            }

            if (body.SortField is not null)
            {
                collection.SortField = body.SortField;
            }

            if (body.SortDirection is not null)
            {
                collection.SortDirection = body.SortDirection;
            }

            if (body.LiveUpdating.HasValue)
            {
                collection.LiveUpdating = body.LiveUpdating.Value;
            }

            if (body.IsEnabled.HasValue)
            {
                collection.IsEnabled = body.IsEnabled.Value;
            }

            if (body.IsFeatured.HasValue)
            {
                collection.IsFeatured = body.IsFeatured.Value;
            }

            if (!string.IsNullOrWhiteSpace(body.Visibility))
            {
                var normalizedVisibility = CollectionAccessPolicy.NormalizeVisibility(body.Visibility);
                if (string.Equals(normalizedVisibility, CollectionAccessPolicy.SharedVisibility, StringComparison.OrdinalIgnoreCase)
                    && !CollectionAccessPolicy.CanManageSharedCollections(activeProfile))
                {
                    return Results.Forbid();
                }

                CollectionAccessPolicy.ApplyVisibility(collection, normalizedVisibility, activeProfile?.Id);
            }

            if (body.Rules is not null)
            {
                if (body.Rules.Count > 0)
                {
                    collection.RuleJson = System.Text.Json.JsonSerializer.Serialize(body.Rules);
                    collection.RuleHash = CollectionRuleEvaluator.ComputeRuleHash(body.Rules);
                    collection.Resolution = "query";
                }
                else
                {
                    collection.RuleJson = null;
                    collection.RuleHash = null;
                    collection.Resolution = "materialized";
                }
            }

            if (string.Equals(collection.Resolution, "materialized", StringComparison.OrdinalIgnoreCase))
            {
                collection.LiveUpdating = false;
            }

            collection.ModifiedAt = DateTimeOffset.UtcNow;
            await collectionRepo.UpsertAsync(collection, ct);
            return Results.Ok();
        })
        .WithName("UpdateCollection")
        .WithSummary("Update a collection's rules, settings, or metadata.")
        .RequireAnyRole();

        // DELETE /collections/{id} — soft delete (disable)
        group.MapDelete("/{id:guid}", async (
            Guid id,
            ICollectionRepository collectionRepo,
            IProfileRepository profileRepo,
            Guid? profileId,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var collection = await collectionRepo.GetByIdAsync(id, ct);
            if (collection is null)
            {
                return Results.NotFound();
            }

            if (!CollectionAccessPolicy.IsManagedCollectionType(collection.CollectionType))
            {
                return Results.BadRequest($"Collection type '{collection.CollectionType}' is browse-only and cannot be deleted here.");
            }

            if (collection.CollectionType == "System")
            {
                return Results.BadRequest("System collections cannot be deleted.");
            }

            if (!CollectionAccessPolicy.CanEdit(collection, activeProfile))
            {
                return Results.Forbid();
            }

            await collectionRepo.UpdateCollectionEnabledAsync(id, false, ct);
            return Results.Ok();
        })
        .WithName("DeleteCollection")
        .WithSummary("Soft-delete a collection by disabling it.")
        .RequireAnyRole();

        // GET /collections/field-values/{field} — distinct values for autocomplete
        group.MapGet("/field-values/{field}", (
            string field,
            int? limit,
            IDatabaseConnection db) =>
        {
            int take = limit ?? 50;
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();

            if (field == "media_type")
            {
                cmd.CommandText = "SELECT DISTINCT media_type FROM works WHERE status NOT IN ('InReview','Rejected') ORDER BY media_type LIMIT @Limit";
            }
            else
            {
                cmd.CommandText = """
                    SELECT DISTINCT value FROM canonical_values
                    WHERE key = @Field AND value IS NOT NULL AND value != ''
                    ORDER BY value
                    LIMIT @Limit
                    """;
                var fp = cmd.CreateParameter();
                fp.ParameterName = "@Field";
                fp.Value = field;
                cmd.Parameters.Add(fp);
            }

            var lp = cmd.CreateParameter();
            lp.ParameterName = "@Limit";
            lp.Value = take;
            cmd.Parameters.Add(lp);

            var values = new List<string>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                values.Add(reader.GetString(0));
            }

            return Results.Ok(values);
        })
        .WithName("GetFieldValues")
        .WithSummary("Returns distinct values for a metadata field (used for collection builder autocomplete).")
        .RequireAnyRole();

        // GET /collections/{id}/placements
        group.MapGet("/{id:guid}/placements", async (
            Guid id,
            ICollectionPlacementRepository placementRepo,
            CancellationToken ct) =>
        {
            var placements = await placementRepo.GetByCollectionIdAsync(id, ct);
            return Results.Ok(placements.Select(p => new
            {
                id = p.Id,
                location = p.Location,
                position = p.Position,
                display_limit = p.DisplayLimit,
                display_mode = p.DisplayMode,
                is_visible = p.IsVisible,
            }));
        })
        .WithName("GetCollectionPlacements")
        .WithSummary("Returns placements for a collection.")
        .RequireAnyRole();

        // PUT /collections/{id}/placements — replace all placements
        group.MapPut("/{id:guid}/placements", async (
            Guid id,
            List<PlacementRequest> body,
            ICollectionPlacementRepository placementRepo,
            CancellationToken ct) =>
        {
            await placementRepo.DeleteByCollectionIdAsync(id, ct);
            foreach (var p in body)
            {
                await placementRepo.UpsertAsync(new CollectionPlacement
                {
                    Id = Guid.NewGuid(),
                    CollectionId = id,
                    Location = p.Location,
                    Position = p.Position,
                    DisplayLimit = p.DisplayLimit,
                    DisplayMode = p.DisplayMode,
                    IsVisible = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                }, ct);
            }
            return Results.Ok();
        })
        .WithName("UpdateCollectionPlacements")
        .WithSummary("Replace all placements for a collection.")
        .RequireAnyRole();

        return app;
    }

    // ── Request bodies ────────────────────────────────────────────────────────

    public sealed record EnabledRequest(bool Enabled);
    public sealed record FeaturedRequest(bool Featured);

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async Task<Profile?> ResolveActiveProfileAsync(
        Guid? profileId,
        IProfileRepository profileRepo,
        CancellationToken ct)
    {
        if (!profileId.HasValue)
        {
            return null;
        }

        return await profileRepo.GetByIdAsync(profileId.Value, ct);
    }

    private static async Task<int> GetManagedCollectionItemCountAsync(
        Collection collection,
        ICollectionRepository collectionRepo,
        IDatabaseConnection db,
        IReadOnlyDictionary<Guid, int>? curatedCountByCollection,
        CancellationToken ct)
    {
        if (string.Equals(collection.Resolution, "query", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(collection.RuleJson))
        {
            var predicates = CollectionRuleEvaluator.ParseRules(collection.RuleJson);
            if (predicates.Count == 0)
            {
                return 0;
            }

            var evaluator = new CollectionRuleEvaluator(db);
            return evaluator.Evaluate(
                predicates,
                collection.MatchMode,
                collection.SortField,
                collection.SortDirection).Count;
        }

        if (curatedCountByCollection is not null && curatedCountByCollection.TryGetValue(collection.Id, out var count))
        {
            return count;
        }

        return await collectionRepo.GetCollectionItemCountAsync(collection.Id, ct);
    }

    private static int GetManagedCollectionItemCount(
        Collection collection,
        IReadOnlyDictionary<Guid, int> curatedCountByCollection,
        IReadOnlyList<Guid> workIds)
    {
        if (!string.Equals(collection.Resolution, "query", StringComparison.OrdinalIgnoreCase)
            && curatedCountByCollection.TryGetValue(collection.Id, out var count))
        {
            return Math.Max(count, workIds.Count);
        }

        return workIds.Count;
    }

    private static CollectionCatalogClassification ClassifyCollectionForCatalog(Collection collection)
    {
        var systemKey = GetSystemCollectionKey(collection);
        if (systemKey is not null)
        {
            return new CollectionCatalogClassification("System", "System", systemKey, true, SystemLaneForKey(systemKey));
        }

        var family = string.Equals(collection.Scope, "library", StringComparison.OrdinalIgnoreCase)
            ? "Global"
            : "User";
        return new CollectionCatalogClassification(family, collection.CollectionType, null, false);
    }

    private static string? GetSystemCollectionKey(Collection collection)
    {
        var normalizedName = (collection.DisplayName ?? string.Empty).Trim();
        if (normalizedName.Length == 0)
        {
            return null;
        }

        if (string.Equals(collection.CollectionType, "System", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedName.ToLowerInvariant().Replace(' ', '-');
        }

        return normalizedName switch
        {
            "Watchlist" => "watchlist",
            "Favorites" => "favorites",
            "Reading List" => "reading-list",
            "Listening Queue" => "listening-queue",
            "Currently Watching" => "currently-watching",
            _ => null,
        };
    }

    private static string? SystemLaneForKey(string systemKey) => systemKey switch
    {
        "favorites" => "Listen",
        "listening-queue" => "Listen",
        "watchlist" => "Watch",
        "currently-watching" => "Watch",
        "reading-list" => "Read",
        _ => null,
    };

    private static bool ShouldIncludeInManagementCatalog(
        Collection collection,
        CollectionCatalogClassification classification,
        CollectionMediaCounts mediaCounts,
        bool hasKnownSeriesManifest)
    {
        if (IsPlaylistCatalogCollection(collection))
        {
            return false;
        }

        if (classification.IsSystem || string.Equals(classification.Family, "User", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (CollectionAccessPolicy.IsManagedCollectionType(collection.CollectionType))
        {
            return true;
        }

        if (IsGeneratedSeriesCollection(collection) && GetCollectionCatalogAggregation(collection) is null)
        {
            return false;
        }

        if (mediaCounts.TotalCount < 2 && !hasKnownSeriesManifest)
        {
            return false;
        }

        return true;
    }

    private static bool IsPlaylistCatalogCollection(Collection collection)
        => string.Equals(collection.CollectionType, "Playlist", StringComparison.OrdinalIgnoreCase)
            || string.Equals(collection.CollectionType, "PlaylistFolder", StringComparison.OrdinalIgnoreCase)
            || string.Equals(collection.CollectionType, "Smart", StringComparison.OrdinalIgnoreCase);

    private static CollectionManagementCatalogCandidate SelectCatalogRepresentative(
        IReadOnlyList<CollectionManagementCatalogCandidate> entries)
    {
        return entries
            .OrderByDescending(entry => entry.MediaCounts.TotalCount)
            .ThenByDescending(entry => entry.ItemCount)
            .ThenBy(entry => entry.Collection.DisplayName, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static CollectionCatalogAggregation? GetCollectionCatalogAggregation(Collection collection)
    {
        if (!IsGeneratedSeriesCollection(collection))
        {
            return null;
        }

        if (TryGetRelationshipAggregation(collection, "fictional_universe", out var aggregation))
        {
            return aggregation;
        }

        if (TryGetRelationshipAggregation(collection, "franchise", out aggregation))
        {
            return aggregation;
        }

        if (TryGetRelationshipAggregation(collection, "series", out aggregation))
        {
            return aggregation;
        }

        return null;
    }

    private static bool ShouldIncludeCatalogGroup(IReadOnlyList<CollectionManagementCatalogCandidate> entries)
    {
        var generatedEntries = entries
            .Where(entry => IsGeneratedSeriesCollection(entry.Collection))
            .ToList();

        if (generatedEntries.Count == 0)
        {
            return true;
        }

        return generatedEntries
            .Where(entry => entry.Collection.Works.Count > 0)
            .Select(entry => entry.Collection.Id)
            .Distinct()
            .Count() >= 2;
    }

    private static bool TryGetRelationshipAggregation(
        Collection collection,
        string relationshipType,
        out CollectionCatalogAggregation aggregation)
    {
        var relationship = collection.Relationships
            .FirstOrDefault(candidate => string.Equals(candidate.RelType, relationshipType, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(candidate.RelQid));
        if (relationship is null)
        {
            aggregation = default!;
            return false;
        }

        aggregation = new CollectionCatalogAggregation(
            $"{relationshipType}:{NormalizeCatalogQid(relationship.RelQid)}",
            FirstNonBlank(relationship.RelLabel, collection.DisplayName));
        return true;
    }

    private static string NormalizeCatalogQid(string qid)
    {
        var value = qid.Contains('/') ? qid.Split('/')[^1] : qid;
        if (value.Contains("::", StringComparison.Ordinal))
        {
            value = value.Split("::", 2)[0];
        }

        return value.Trim();
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? ToNullableText(object? value) =>
        value switch
        {
            null => null,
            string text when string.IsNullOrWhiteSpace(text) => null,
            string text => text,
            byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };

    private static async Task<bool> HasKnownSeriesManifestAsync(
        Collection collection,
        ISeriesManifestRepository manifestRepo,
        CancellationToken ct)
    {
        if (!IsGeneratedSeriesCollection(collection))
        {
            return false;
        }

        var manifest = await manifestRepo.GetViewByCollectionIdAsync(collection.Id, ct);
        return manifest?.TotalCount > 1;
    }

    private static bool IsGeneratedSeriesCollection(Collection collection)
        => string.Equals(collection.CollectionType, "Universe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(collection.CollectionType, "Series", StringComparison.OrdinalIgnoreCase)
            || string.Equals(collection.CollectionType, "ContentGroup", StringComparison.OrdinalIgnoreCase);

    private static bool IsGeneratedTvShowContainer(Collection collection, CollectionMediaCounts mediaCounts)
    {
        if (!IsGeneratedSeriesCollection(collection))
        {
            return false;
        }

        return mediaCounts.TvCount > 0
            && mediaCounts.WatchCount == mediaCounts.TvCount
            && mediaCounts.ListenCount == 0
            && mediaCounts.ReadCount == 0
            && mediaCounts.OtherCount == 0;
    }

    private static async Task<CollectionMediaCounts> GetCollectionMediaCountsAsync(
        Collection collection,
        ICollectionRepository collectionRepo,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        var workIds = await GetCollectionWorkIdsAsync(collection, collectionRepo, db, ct);
        return await GetCollectionMediaCountsAsync(workIds, db, ct);
    }

    private static async Task<CollectionMediaCounts> GetCollectionMediaCountsAsync(
        IReadOnlyList<Guid> workIds,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        if (workIds.Count == 0)
        {
            return new CollectionMediaCounts(0, 0, 0, 0);
        }

        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<(string MediaType, int Count)>(new CommandDefinition(
                """
                SELECT media_type AS MediaType, COUNT(*) AS Count
                FROM works
                WHERE id IN @WorkIds
                GROUP BY media_type
                """,
                new { WorkIds = workIds.Select(id => id.ToString()).ToArray() },
                cancellationToken: ct));

        var watch = 0;
        var listen = 0;
        var read = 0;
        var other = 0;
        var tv = 0;
        foreach (var row in rows)
        {
            if (string.Equals(row.MediaType, "TV", StringComparison.OrdinalIgnoreCase))
            {
                tv += row.Count;
            }

            if (IsWatchMediaType(row.MediaType))
            {
                watch += row.Count;
            }
            else if (IsListenMediaType(row.MediaType))
            {
                listen += row.Count;
            }
            else if (IsReadMediaType(row.MediaType))
            {
                read += row.Count;
            }
            else
            {
                other += row.Count;
            }
        }

        return new CollectionMediaCounts(watch, listen, read, other, tv);
    }

    private static async Task<IReadOnlyList<Guid>> GetCollectionWorkIdsAsync(
        Collection collection,
        ICollectionRepository collectionRepo,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        if (string.Equals(collection.Resolution, "query", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(collection.RuleJson))
        {
            var predicates = CollectionRuleEvaluator.ParseRules(collection.RuleJson);
            if (predicates.Count == 0)
            {
                return [];
            }

            var evaluator = new CollectionRuleEvaluator(db);
            return evaluator.Evaluate(predicates, collection.MatchMode, collection.SortField, collection.SortDirection, 0);
        }

        var items = await collectionRepo.GetCollectionItemsAsync(collection.Id, 5000, ct);
        if (items.Count > 0)
        {
            return items.Select(item => item.WorkId).Distinct().ToList();
        }

        var collectionWithWorks = await collectionRepo.GetCollectionWithWorksAsync(collection.Id, ct);
        return collectionWithWorks?.Works.Select(work => work.Id).Distinct().ToList() ?? [];
    }

    private static async Task<IReadOnlyList<Guid>> GetAggregatedCollectionWorkIdsAsync(
        Guid collectionId,
        Profile? activeProfile,
        ICollectionRepository collectionRepo,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        var accessibleCollections = (await collectionRepo.GetAllAsync(ct))
            .Where(collection => CollectionAccessPolicy.CanAccess(collection, activeProfile))
            .ToList();
        var target = accessibleCollections.FirstOrDefault(collection => collection.Id == collectionId);
        if (target is null)
        {
            return [];
        }

        var targetGrouping = GetCollectionCatalogAggregation(target);
        var siblingCollections = targetGrouping is null
            ? new List<Collection> { target }
            : accessibleCollections
                .Where(collection => IsGeneratedSeriesCollection(collection)
                    && string.Equals(GetCollectionCatalogAggregation(collection)?.Key, targetGrouping.Key, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var workIds = new List<Guid>();
        foreach (var collection in ExpandWithChildCollections(siblingCollections, accessibleCollections))
        {
            workIds.AddRange(await GetCollectionWorkIdsAsync(collection, collectionRepo, db, ct));
        }

        return await GetCollectionCatalogDisplayWorkIdsAsync(workIds, db, ct);
    }

    private static async Task<IReadOnlyList<Guid>> GetCollectionCatalogSourceWorkIdsAsync(
        Collection collection,
        IReadOnlyList<Collection> accessibleCollections,
        ICollectionRepository collectionRepo,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        var workIds = new List<Guid>();
        foreach (var sourceCollection in ExpandWithChildCollections([collection], accessibleCollections))
        {
            workIds.AddRange(await GetCollectionWorkIdsAsync(sourceCollection, collectionRepo, db, ct));
        }

        return workIds.Distinct().ToList();
    }

    private static IReadOnlyList<Collection> ExpandWithChildCollections(
        IReadOnlyList<Collection> collections,
        IReadOnlyList<Collection> accessibleCollections)
    {
        var result = new List<Collection>();
        var queue = new Queue<Collection>(collections);
        var seen = new HashSet<Guid>();
        while (queue.Count > 0)
        {
            var collection = queue.Dequeue();
            if (!seen.Add(collection.Id))
            {
                continue;
            }

            result.Add(collection);
            foreach (var child in accessibleCollections.Where(candidate => candidate.ParentCollectionId == collection.Id))
            {
                queue.Enqueue(child);
            }
        }

        return result;
    }

    private static async Task<IReadOnlyList<Guid>> GetCollectionCatalogDisplayWorkIdsAsync(
        IEnumerable<Guid> sourceWorkIds,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        var workIds = sourceWorkIds.Distinct().ToList();
        if (workIds.Count == 0)
        {
            return [];
        }

        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<CollectionDisplayWorkRow>(new CommandDefinition(
            """
            SELECT DISTINCT
                   CASE
                       WHEN w.work_kind = 'child' THEN COALESCE(gp.id, p.id, w.id)
                       WHEN w.work_kind = 'parent' AND p.id IS NOT NULL THEN COALESCE(gp.id, p.id, w.id)
                       ELSE w.id
                   END AS WorkId
            FROM works w
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            WHERE w.id IN @WorkIds;
            """,
            new { WorkIds = workIds.Select(id => id.ToString("D")).ToArray() },
            cancellationToken: ct));

        return rows
            .Select(row => Guid.TryParse(row.WorkId, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
    }

    private static async Task<Guid> ResolveCollectionMembershipWorkIdAsync(
        Guid sourceWorkId,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        var displayIds = await GetCollectionCatalogDisplayWorkIdsAsync([sourceWorkId], db, ct);
        return displayIds.Count == 0 ? sourceWorkId : displayIds[0];
    }

    private static async Task<List<CollectionItemDto>> ResolveCollectionWorkIdsToItemsAsync(
        Guid collectionId,
        IReadOnlyList<Guid> workIds,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        var displayWorkIds = await GetCollectionCatalogDisplayWorkIdsAsync(workIds, db, ct);
        if (displayWorkIds.Count == 0)
        {
            return [];
        }

        using var conn = db.CreateConnection();
        var visibleWorkPredicate = HomeVisibilitySql.VisibleWorkPredicate("w.id", "w.curator_state", "w.is_catalog_only");
        var visibleAssetPredicate = HomeVisibilitySql.VisibleAssetPathPredicate("ma.file_path_root");
        var rows = (await conn.QueryAsync<GeneratedCollectionItemRow>(new CommandDefinition(
            $"""
            WITH RECURSIVE work_tree(RootWorkId, WorkId) AS (
                SELECT w.id AS RootWorkId,
                       w.id AS WorkId
                FROM works w
                WHERE w.id IN @WorkIds
                UNION ALL
                SELECT work_tree.RootWorkId,
                       child.id AS WorkId
                FROM works child
                INNER JOIN work_tree ON child.parent_work_id = work_tree.WorkId
            ),
            representative_assets AS (
                SELECT work_tree.RootWorkId AS WorkId,
                       MIN(ma.id) AS AssetId
                FROM work_tree
                INNER JOIN editions e ON e.work_id = work_tree.WorkId
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE {visibleAssetPredicate}
                GROUP BY work_tree.RootWorkId
            )
            SELECT w.id AS WorkId,
                   COALESCE(
                       NULLIF(title_work.value, ''),
                       NULLIF(episode_title.value, ''),
                       NULLIF(show_name.value, ''),
                       NULLIF(series_item.item_label, ''),
                       (
                           SELECT NULLIF(CAST(descendant_title.value AS TEXT), '')
                           FROM work_tree title_tree
                           INNER JOIN canonical_values descendant_title ON descendant_title.entity_id = title_tree.WorkId
                           WHERE title_tree.RootWorkId = w.id
                             AND descendant_title.key IN ('show_name', 'title')
                           ORDER BY CASE descendant_title.key WHEN 'show_name' THEN 0 ELSE 1 END
                           LIMIT 1
                       ),
                       'Untitled'
                   ) AS Title,
                   COALESCE(
                       NULLIF(CAST(author_work.value AS TEXT), ''),
                       NULLIF(CAST(artist_work.value AS TEXT), ''),
                       NULLIF(CAST(director_work.value AS TEXT), '')
                   ) AS Creator,
                   w.media_type AS MediaType,
                   COALESCE(
                       NULLIF(cover_asset.value, ''),
                       NULLIF(cover_work.value, ''),
                       CASE WHEN ra.AssetId IS NOT NULL THEN '/stream/' || ra.AssetId || '/cover' END
                   ) AS CoverUrl,
                   COALESCE(w.ordinal, series_item.sort_order, 999999) AS SortOrder
            FROM works w
            LEFT JOIN representative_assets ra ON ra.WorkId = w.id
            LEFT JOIN canonical_values title_work ON title_work.entity_id = w.id AND title_work.key = 'title'
            LEFT JOIN canonical_values episode_title ON episode_title.entity_id = w.id AND episode_title.key = 'episode_title'
            LEFT JOIN canonical_values show_name ON show_name.entity_id = w.id AND show_name.key = 'show_name'
            LEFT JOIN canonical_values author_work ON author_work.entity_id = w.id AND author_work.key = 'author'
            LEFT JOIN canonical_values artist_work ON artist_work.entity_id = w.id AND artist_work.key IN ('artist', 'album_artist')
            LEFT JOIN canonical_values director_work ON director_work.entity_id = w.id AND director_work.key = 'director'
            LEFT JOIN canonical_values cover_work ON cover_work.entity_id = w.id AND cover_work.key IN ('cover_url', 'cover', 'poster_url', 'poster', 'episode_still_url', 'episode_still', 'still_url', 'still')
            LEFT JOIN canonical_values cover_asset ON cover_asset.entity_id = ra.AssetId AND cover_asset.key IN ('cover_url', 'cover', 'poster_url', 'poster', 'episode_still_url', 'episode_still', 'still_url', 'still')
            LEFT JOIN series_manifest_items series_item ON series_item.linked_work_id = w.id AND series_item.collection_id = @CollectionId
            WHERE w.id IN @WorkIds
              AND ({visibleWorkPredicate} OR ra.AssetId IS NOT NULL)
            ORDER BY SortOrder, Title COLLATE NOCASE, w.id;
            """,
            new
            {
                CollectionId = collectionId.ToString("D"),
                WorkIds = displayWorkIds.Select(id => id.ToString("D")).ToArray(),
            },
            cancellationToken: ct))).ToList();

        return rows
            .GroupBy(row => row.WorkId)
            .Select(group => group.OrderBy(row => row.SortOrder).ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase).First())
            .Select(row => new CollectionItemDto
            {
                Id = DeterministicCollectionItemId(collectionId, row.WorkId),
                WorkId = row.WorkId,
                Title = row.Title,
                Creator = ToNullableText(row.Creator),
                MediaType = row.MediaType,
                CoverUrl = row.CoverUrl,
                SortOrder = row.SortOrder,
            }).ToList();
    }

    private static Guid DeterministicCollectionItemId(Guid collectionId, Guid workId)
    {
        var bytes = collectionId.ToByteArray().Concat(workId.ToByteArray()).ToArray();
        return new Guid(System.Security.Cryptography.MD5.HashData(bytes));
    }

    private static async Task<IReadOnlyList<CollectionArtworkItemDto>> GetCollectionArtworkItemsAsync(
        Collection collection,
        ICollectionRepository collectionRepo,
        IDatabaseConnection db,
        int limit,
        CancellationToken ct)
    {
        var workIds = (await GetCollectionWorkIdsAsync(collection, collectionRepo, db, ct))
            .Distinct()
            .ToList();
        return await GetCollectionArtworkItemsAsync(workIds, db, limit, ct);
    }

    private static async Task<IReadOnlyList<CollectionArtworkItemDto>> GetCollectionArtworkItemsAsync(
        IReadOnlyList<Guid> sourceWorkIds,
        IDatabaseConnection db,
        int limit,
        CancellationToken ct)
    {
        var workIds = (await GetCollectionCatalogDisplayWorkIdsAsync(sourceWorkIds, db, ct))
            .Take(Math.Clamp(limit, 1, 8))
            .ToList();
        if (workIds.Count == 0)
        {
            return [];
        }

        using var conn = db.CreateConnection();
        var visibleWorkPredicate = HomeVisibilitySql.VisibleWorkPredicate("w.id", "w.curator_state", "w.is_catalog_only");
        var visibleAssetPredicate = HomeVisibilitySql.VisibleAssetPathPredicate("ma.file_path_root");
        var rows = (await conn.QueryAsync<CollectionArtworkItemRow>(new CommandDefinition(
            $"""
            WITH RECURSIVE work_tree(RootWorkId, WorkId) AS (
                SELECT w.id AS RootWorkId,
                       w.id AS WorkId
                FROM works w
                WHERE w.id IN @WorkIds
                UNION ALL
                SELECT work_tree.RootWorkId,
                       child.id AS WorkId
                FROM works child
                INNER JOIN work_tree ON child.parent_work_id = work_tree.WorkId
            ),
            representative_assets AS (
                SELECT work_tree.RootWorkId AS WorkId,
                       MIN(ma.id) AS AssetId
                FROM work_tree
                INNER JOIN editions e ON e.work_id = work_tree.WorkId
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE {visibleAssetPredicate}
                GROUP BY work_tree.RootWorkId
            )
            SELECT w.id AS WorkId,
                   COALESCE(
                       NULLIF(title_work.value, ''),
                       NULLIF(episode_title.value, ''),
                       NULLIF(show_name.value, ''),
                       NULLIF(series_item.item_label, ''),
                       'Untitled'
                   ) AS Title,
                   w.media_type AS MediaType,
                   COALESCE(
                       NULLIF(cover_asset.value, ''),
                       NULLIF(cover_work.value, ''),
                       CASE WHEN ra.AssetId IS NOT NULL THEN '/stream/' || ra.AssetId || '/cover' END
                   ) AS CoverUrl,
                   COALESCE(
                       NULLIF(primary_work.value, ''),
                       NULLIF(cover_primary_work.value, ''),
                       NULLIF(preferred_cover.primary_hex, '')
                   ) AS PrimaryColor,
                   COALESCE(
                       NULLIF(secondary_work.value, ''),
                       NULLIF(cover_secondary_work.value, ''),
                       NULLIF(preferred_cover.secondary_hex, '')
                   ) AS SecondaryColor,
                   COALESCE(
                       NULLIF(accent_work.value, ''),
                       NULLIF(dominant_work.value, ''),
                       NULLIF(cover_accent_work.value, ''),
                       NULLIF(preferred_cover.accent_hex, '')
                   ) AS AccentColor,
                   COALESCE(
                       NULLIF(preferred_cover.local_image_path_s, ''),
                       NULLIF(preferred_cover.local_image_path_m, ''),
                       NULLIF(preferred_cover.local_image_path, '')
                   ) AS LocalImagePath
            FROM works w
            LEFT JOIN representative_assets ra ON ra.WorkId = w.id
            LEFT JOIN entity_assets preferred_cover ON preferred_cover.id = (
                SELECT ea.id
                FROM entity_assets ea
                WHERE ea.entity_id = w.id
                  AND ea.entity_type = 'Work'
                  AND ea.asset_type IN ('CoverArt', 'SquareArt', 'Background', 'Banner')
                ORDER BY ea.is_preferred DESC, ea.created_at DESC, ea.id
                LIMIT 1
            )
            LEFT JOIN canonical_values title_work ON title_work.entity_id = w.id AND title_work.key = 'title'
            LEFT JOIN canonical_values episode_title ON episode_title.entity_id = w.id AND episode_title.key = 'episode_title'
            LEFT JOIN canonical_values show_name ON show_name.entity_id = w.id AND show_name.key = 'show_name'
            LEFT JOIN canonical_values cover_asset ON cover_asset.entity_id = ra.AssetId AND cover_asset.key IN ('cover_url', 'cover', 'poster_url', 'poster', 'episode_still_url', 'episode_still', 'still_url', 'still')
            LEFT JOIN canonical_values cover_work ON cover_work.entity_id = w.id AND cover_work.key IN ('cover_url', 'cover', 'poster_url', 'poster', 'episode_still_url', 'episode_still', 'still_url', 'still')
            LEFT JOIN canonical_values primary_work ON primary_work.entity_id = w.id AND primary_work.key = 'artwork_primary_hex'
            LEFT JOIN canonical_values secondary_work ON secondary_work.entity_id = w.id AND secondary_work.key = 'artwork_secondary_hex'
            LEFT JOIN canonical_values accent_work ON accent_work.entity_id = w.id AND accent_work.key = 'artwork_accent_hex'
            LEFT JOIN canonical_values dominant_work ON dominant_work.entity_id = w.id AND dominant_work.key = 'dominant_color'
            LEFT JOIN canonical_values cover_primary_work ON cover_primary_work.entity_id = w.id AND cover_primary_work.key = 'cover_primary_hex'
            LEFT JOIN canonical_values cover_secondary_work ON cover_secondary_work.entity_id = w.id AND cover_secondary_work.key = 'cover_secondary_hex'
            LEFT JOIN canonical_values cover_accent_work ON cover_accent_work.entity_id = w.id AND cover_accent_work.key = 'cover_accent_hex'
            LEFT JOIN series_manifest_items series_item ON series_item.linked_work_id = w.id
            WHERE w.id IN @WorkIds
              AND ({visibleWorkPredicate} OR ra.AssetId IS NOT NULL)
            """,
            new { WorkIds = workIds.Select(id => id.ToString()).ToArray() },
            cancellationToken: ct))).ToList();

        var rowById = rows
            .Where(row => Guid.TryParse(row.WorkId, out _))
            .GroupBy(row => Guid.Parse(row.WorkId))
            .ToDictionary(grouping => grouping.Key, grouping => grouping.First());

        return workIds
            .Where(rowById.ContainsKey)
            .Select(id =>
            {
                var row = rowById[id];
                return new CollectionArtworkItemDto
                {
                    WorkId = id,
                    Title = string.IsNullOrWhiteSpace(row.Title) ? "Untitled" : row.Title,
                    MediaType = row.MediaType ?? "Unknown",
                    CoverUrl = row.CoverUrl,
                    PrimaryColor = row.PrimaryColor,
                    SecondaryColor = row.SecondaryColor,
                    AccentColor = row.AccentColor,
                    ArtworkShape = ArtworkShapeForMediaType(row.MediaType),
                    LocalImagePath = row.LocalImagePath,
                };
            })
            .ToList();
    }

    private static bool IsWatchMediaType(string? mediaType) =>
        string.Equals(mediaType, "Movies", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "Movie", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "TV", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "Video", StringComparison.OrdinalIgnoreCase);

    private static bool IsListenMediaType(string? mediaType) =>
        string.Equals(mediaType, "Music", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "Audio", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "Audiobooks", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "Audiobook", StringComparison.OrdinalIgnoreCase);

    private static bool IsReadMediaType(string? mediaType) =>
        string.Equals(mediaType, "Books", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "Book", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "Comics", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "Comic", StringComparison.OrdinalIgnoreCase);

    private static string ArtworkShapeForMediaType(string? mediaType)
    {
        if (IsReadMediaType(mediaType))
        {
            return "portrait";
        }

        if (IsWatchMediaType(mediaType))
        {
            return "portrait";
        }

        if (IsListenMediaType(mediaType))
        {
            return "square";
        }

        return "square";
    }

    private static MediaType? TryParseMediaType(string? mediaType) =>
        Enum.TryParse<MediaType>(mediaType, ignoreCase: true, out var parsed)
            ? parsed
            : mediaType switch
            {
                "Movie" => MediaType.Movies,
                "Book" => MediaType.Books,
                "Audiobook" => MediaType.Audiobooks,
                "Comic" => MediaType.Comics,
                "Shows" or "Show" => MediaType.TV,
                _ => null,
            };

    private static ArtworkShape? TryParseArtworkShape(string? shape) => shape?.Trim().ToLowerInvariant() switch
    {
        "square" => ArtworkShape.Square,
        "portrait" => ArtworkShape.Portrait,
        "wide" or "landscape" => ArtworkShape.Wide,
        _ => null,
    };

    private sealed class CollectionArtworkItemRow
    {
        public string WorkId { get; init; } = string.Empty;
        public string? Title { get; init; }
        public string? MediaType { get; init; }
        public string? CoverUrl { get; init; }
        public string? PrimaryColor { get; init; }
        public string? SecondaryColor { get; init; }
        public string? AccentColor { get; init; }
        public string? LocalImagePath { get; init; }
    }

    private static string? NormalizeCollectionArtworkMimeType(string? contentType, string extension)
    {
        if (string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase) || extension == ".png")
        {
            return "image/png";
        }

        if (string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "image/jpg", StringComparison.OrdinalIgnoreCase)
            || extension is ".jpg" or ".jpeg")
        {
            return "image/jpeg";
        }

        return null;
    }

    private static string GetCollectionArtworkMimeType(string path) =>
        string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";

    private static bool WorkMatchesQuery(WorkDto w, string query) =>
        w.CanonicalValues.Any(cv =>
            cv.Value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private sealed class CollectionSearchRow
    {
        public Guid WorkId { get; init; }
        public Guid? CollectionId { get; init; }
        public string MediaType { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string? Author { get; init; }
        public string CollectionDisplayName { get; init; } = string.Empty;
        public string? CoverUrl { get; init; }
    }

    private static string? GetCanonical(WorkDto? w, string key)
    {
        var raw = w?.CanonicalValues
            .FirstOrDefault(cv => cv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?.Value;
        return raw;
    }

    private static async Task<Dictionary<Guid, Guid?>> LoadPrimaryAssetIdsAsync(
        IEnumerable<Guid> workIds,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        var ids = workIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        using var conn = db.CreateConnection();
        using var cmd = conn.CreateCommand();
        var parameterNames = new List<string>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = $"@workId{i}";
            parameter.Value = GuidSql.ToBlob(ids[i]);
            cmd.Parameters.Add(parameter);
            parameterNames.Add(parameter.ParameterName);
        }

        cmd.CommandText = $"""
            SELECT e.work_id AS WorkId,
                   MIN(ma.id) AS AssetId
            FROM editions e
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            WHERE e.work_id IN ({string.Join(", ", parameterNames)})
            GROUP BY e.work_id;
            """;

        var results = new Dictionary<Guid, Guid?>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var workId = GuidSql.FromDb(reader.GetValue(0));
            var assetId = GuidSql.FromDbNullable(reader.GetValue(1));
            results[workId] = assetId;
        }

        return results;
    }

    /// <summary>
    /// Builds the preferred cover URL from a Work's canonical values.
    /// </summary>
    private static string? BuildCoverStreamUrl(Work? w)
    {
        if (w is null)
        {
            return null;
        }

        return w.CanonicalValues
            .FirstOrDefault(c =>
                string.Equals(c.Key, MetadataFieldConstants.CoverUrl, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Key, MetadataFieldConstants.Cover, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static string? BuildBackgroundStreamUrl(Work? w)
    {
        if (w is null)
        {
            return null;
        }

        return w.CanonicalValues
            .FirstOrDefault(c =>
                string.Equals(c.Key, "background", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Key, "background_url", StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static string? BuildBannerStreamUrl(Work? w)
    {
        if (w is null)
        {
            return null;
        }

        return w.CanonicalValues
            .FirstOrDefault(c =>
                string.Equals(c.Key, "banner", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Key, "banner_url", StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static string? BuildLogoStreamUrl(Work? w)
    {
        if (w is null)
        {
            return null;
        }

        var canonicalLogo = w.CanonicalValues
            .FirstOrDefault(c =>
                string.Equals(c.Key, "logo", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Key, "logo_url", StringComparison.OrdinalIgnoreCase))
            ?.Value;
        if (!string.IsNullOrWhiteSpace(canonicalLogo))
        {
            return canonicalLogo;
        }

        var assetId = w.CanonicalValues
            .Select(c => c.EntityId)
            .FirstOrDefault(id => id != Guid.Empty);
        return assetId != Guid.Empty ? $"/stream/{assetId}/logo" : null;
    }

    private static string? BuildHeroStreamUrl(Work? w)
        => null;

    private static PlaybackTechnicalSummary? BuildPlaybackSummaryFromWork(WorkDto work)
    {
        string? Canonical(string key) => GetCanonical(work, key);

        var subtitleLanguages = SplitValues(Canonical("subtitle_languages"));
        var summary = new PlaybackTechnicalSummary
        {
            VideoResolutionLabel = FormatResolution(
                ParseNullableInt(Canonical("video_width")),
                ParseNullableInt(Canonical("video_height"))),
            VideoCodec = NormalizeCodec(Canonical("video_codec")),
            AudioLanguage = SplitValues(Canonical("audio_language")).FirstOrDefault(),
            AudioCodec = NormalizeCodec(Canonical("audio_codec")),
            AudioChannels = FormatAudioChannels(Canonical("audio_channels")),
            SubtitleSummary = FormatSubtitleSummary(subtitleLanguages),
            AudioLanguages = SplitValues(Canonical("audio_language")),
            SubtitleLanguages = subtitleLanguages,
        };

        if (string.IsNullOrWhiteSpace(summary.VideoResolutionLabel)
            && string.IsNullOrWhiteSpace(summary.VideoCodec)
            && string.IsNullOrWhiteSpace(summary.AudioLanguage)
            && string.IsNullOrWhiteSpace(summary.AudioCodec)
            && string.IsNullOrWhiteSpace(summary.AudioChannels)
            && string.IsNullOrWhiteSpace(summary.SubtitleSummary))
        {
            return null;
        }

        return summary;
    }

    private static string? NormalizeReleaseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, out var parsed))
        {
            return parsed.ToString("MMMM d, yyyy");
        }

        return value.Length > 10 && DateTime.TryParse(value, out var parsedDate)
            ? parsedDate.ToString("MMMM d, yyyy")
            : value;
    }

    private static int? ParseNullableInt(string? value) =>
        int.TryParse(value, out var parsed) ? parsed : null;

    private static int? ReadNullableInt(System.Data.IDataRecord reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var raw = reader.GetValue(ordinal);
        return raw switch
        {
            int value when value > 0 => value,
            long value when value > 0 => (int)value,
            string value when int.TryParse(value, out var parsed) && parsed > 0 => parsed,
            _ => null,
        };
    }

    private static List<string> SplitValues(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split(['|', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static string? FormatResolution(int? width, int? height)
    {
        if (width is null || height is null || width <= 0 || height <= 0)
        {
            return null;
        }

        var h = height.Value;
        return h switch
        {
            >= 2160 => "2160p",
            >= 1440 => "1440p",
            >= 1080 => "1080p",
            >= 720 => "720p",
            >= 480 => "480p",
            _ => $"{h}p",
        };
    }

    private static string? FormatAudioChannels(string? value)
    {
        if (!int.TryParse(value, out var parsed) || parsed <= 0)
        {
            return null;
        }

        return parsed switch
        {
            1 => "Mono",
            2 => "2.0",
            _ => $"{parsed - 1}.1",
        };
    }

    private static string? FormatSubtitleSummary(IReadOnlyList<string> languages)
    {
        if (languages.Count == 0)
        {
            return null;
        }

        if (languages.Count == 1)
        {
            return languages[0];
        }

        return $"{languages[0]} + {languages.Count - 1} more";
    }

    private static string? NormalizeCodec(string? codec)
    {
        if (string.IsNullOrWhiteSpace(codec))
        {
            return null;
        }

        return codec.ToLowerInvariant() switch
        {
            "h264" => "H.264",
            "hevc" => "HEVC",
            "aac" => "AAC",
            "ac3" => "AC3",
            "eac3" => "EAC3",
            "dts" => "DTS",
            "truehd" => "TrueHD",
            "opus" => "Opus",
            "flac" => "FLAC",
            "subrip" => "SRT",
            _ => codec.ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Merges Wikidata-discovered tracks (from <c>child_entities_json</c>) into the owned-track list,
    /// flagging those without a matching local file as <c>IsOwned = false</c>. Owned tracks are matched
    /// to Wikidata tracks by case-insensitive title.
    /// </summary>
    private static List<CollectionGroupWorkDto> MergeUnownedMusicTracks(
        List<CollectionGroupWorkDto> ownedTracks,
        string? childEntitiesJson,
        string? albumCover)
    {
        if (string.IsNullOrWhiteSpace(childEntitiesJson))
        {
            // No Wikidata data — sort owned by track number and return.
            return SortAlbumTracks(ownedTracks);
        }

        try
        {
            using var doc = JsonDocument.Parse(childEntitiesJson);
            if (!doc.RootElement.TryGetProperty("tracks", out var tracksArr) ||
                tracksArr.ValueKind != JsonValueKind.Array)
            {
                return SortAlbumTracks(ownedTracks);
            }

            // Build a lookup of owned tracks by normalized title for matching.
            var ownedByAppleMusicId = ownedTracks
                .Where(t => !string.IsNullOrWhiteSpace(t.AppleMusicId))
                .GroupBy(t => t.AppleMusicId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var ownedByTitleAndNumber = ownedTracks
                .Where(t => !string.IsNullOrWhiteSpace(t.Title))
                .GroupBy(t => BuildTrackMatchKey(t.Title, t.DiscNumber, ParseNullableInt(t.TrackNumber)))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var ownedByTitle = ownedTracks
                .Where(t => !string.IsNullOrWhiteSpace(t.Title))
                .GroupBy(t => NormalizeTrackTitle(t.Title))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var merged = new List<CollectionGroupWorkDto>();
            var seenOwned = new HashSet<Guid>();
            int manifestOrdinal = 0;

            foreach (var trackEl in tracksArr.EnumerateArray())
            {
                manifestOrdinal++;
                var title = ReadJsonString(trackEl, "title", "trackName", "name");
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var trackNumber = ReadJsonInt(trackEl, "track_number", "trackNumber", "number");
                var discNumber = ReadJsonInt(trackEl, "disc_number", "discNumber");
                var ordinal = ReadJsonInt(trackEl, "ordinal", "position") ?? trackNumber ?? manifestOrdinal;
                var durationSeconds = ReadChildDurationSeconds(trackEl);
                var appleMusicId = ReadJsonString(trackEl, "apple_music_id", "appleMusicId", "trackId");
                var owned = (!string.IsNullOrWhiteSpace(appleMusicId)
                        ? ownedByAppleMusicId.GetValueOrDefault(appleMusicId)
                        : null)
                    ?? ownedByTitleAndNumber.GetValueOrDefault(BuildTrackMatchKey(title, discNumber, trackNumber));
                if (owned is null)
                {
                    var titleMatch = ownedByTitle.GetValueOrDefault(NormalizeTrackTitle(title));
                    if (titleMatch is not null
                        && !HasKnownTrackIdentityConflict(titleMatch, discNumber, trackNumber, durationSeconds))
                    {
                        owned = titleMatch;
                    }
                }
                if (owned is not null)
                {
                    // Owned — keep the local row but normalise the track number from Wikidata.
                    merged.Add(new CollectionGroupWorkDto
                    {
                        WorkId = owned.WorkId,
                        AssetId = owned.AssetId,
                        Title = owned.Title,
                        Ordinal = owned.Ordinal ?? ordinal,
                        Year = owned.Year,
                        Duration = FirstNonBlank(owned.Duration, FormatAudioDuration(durationSeconds, null)),
                        DurationSeconds = owned.DurationSeconds ?? durationSeconds,
                        CoverUrl = owned.CoverUrl ?? albumCover,
                        BackgroundUrl = owned.BackgroundUrl,
                        BannerUrl = owned.BannerUrl,
                        HeroUrl = owned.HeroUrl,
                        WikidataQid = owned.WikidataQid,
                        TrackNumber = FirstNonBlank(owned.TrackNumber, (trackNumber ?? ordinal).ToString(CultureInfo.InvariantCulture)),
                        DiscNumber = owned.DiscNumber ?? discNumber,
                        AppleMusicId = FirstNonBlank(owned.AppleMusicId, appleMusicId),
                        Status = owned.Status,
                        Description = owned.Description,
                        Director = owned.Director,
                        Writer = owned.Writer,
                        ReleaseDate = owned.ReleaseDate,
                        PlaybackSummary = owned.PlaybackSummary,
                        IsOwned = true,
                        Stage1 = owned.Stage1,
                        Stage2 = owned.Stage2,
                        Stage3 = owned.Stage3,
                    });
                    seenOwned.Add(owned.WorkId);
                }
                else
                {
                    // Unowned — synthesize a row from Wikidata data.
                    merged.Add(new CollectionGroupWorkDto
                    {
                        WorkId = Guid.Empty,
                        Title = title,
                        Ordinal = ordinal,
                        TrackNumber = (trackNumber ?? ordinal).ToString(CultureInfo.InvariantCulture),
                        DiscNumber = discNumber,
                        AppleMusicId = appleMusicId,
                        Duration = FormatAudioDuration(durationSeconds, null),
                        DurationSeconds = durationSeconds,
                        CoverUrl = albumCover,
                        Status = "Missing",
                        IsOwned = false,
                    });
                }
            }

            // Append any owned tracks that didn't match a Wikidata title (rare — bonus tracks, mislabeled).
            foreach (var t in ownedTracks)
            {
                if (!seenOwned.Contains(t.WorkId))
                {
                    merged.Add(t);
                }
            }

            return SortAlbumTracks(merged);
        }
        catch (JsonException)
        {
            // Malformed JSON — fall back to owned-only.
            return SortAlbumTracks(ownedTracks);
        }
    }

    /// <summary>
    /// Merges Wikidata-discovered child entities (from <c>child_entities_json</c>)
    /// into <paramref name="sectionMap"/> as unowned rows, deduplicating against
    /// owned rows by case-insensitive title. Supports TV (episodes grouped by
    /// season), music (tracks in flat "_flat" section), and comics (issues).
    ///
    /// Called by <c>system-view-detail</c> after the owned-works reader loop.
    /// </summary>
    private static void MergeUnownedChildEntities(
        Dictionary<string, List<CollectionGroupWorkDto>> sectionMap,
        string childEntitiesJson,
        string groupField,
        string? secondaryGroup,
        string? fallbackCover)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(childEntitiesJson);
            var root = doc.RootElement;

            // Determine which array key to read. Mirrors ReconciliationAdapter conventions.
            // "tracks" for music, "episodes" for TV (flat or grouped), "issues" for comics.
            string[]? arrayKeys = groupField.ToLowerInvariant() switch
            {
                "show_name" => ["episodes", "seasons"],
                "album" => ["tracks"],
                "series" => ["issues"],
                _ => null,
            };

            if (arrayKeys is null)
            {
                return;
            }

            // TV episodes may be nested: root.seasons[].episodes[].
            if (groupField.Equals("show_name", StringComparison.OrdinalIgnoreCase)
                && root.TryGetProperty("seasons", out var seasonsArr)
                && seasonsArr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var seasonEl in seasonsArr.EnumerateArray())
                {
                    var seasonNum = seasonEl.TryGetProperty("season_number", out var snEl)
                        && snEl.ValueKind == System.Text.Json.JsonValueKind.Number
                        ? snEl.GetInt32().ToString()
                        : null;

                    if (!seasonEl.TryGetProperty("episodes", out var epArr)
                        || epArr.ValueKind != System.Text.Json.JsonValueKind.Array)
                    {
                        continue;
                    }

                    MergeChildArray(sectionMap, epArr, seasonNum ?? "Unknown",
                        isEpisode: true, fallbackCover);
                }
                return;
            }

            // Flat structure: tracks, issues, or flat episodes.
            foreach (var key in arrayKeys)
            {
                if (root.TryGetProperty(key, out var arr)
                    && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    MergeChildArray(sectionMap, arr, "_flat",
                        isEpisode: key == "episodes", fallbackCover);
                    return;
                }
            }
        }
        catch
        {
            // Malformed JSON — leave sectionMap as-is (owned only).
        }
    }

    /// <summary>
    /// Adds unowned rows from <paramref name="childArray"/> into
    /// <paramref name="sectionMap"/>[<paramref name="sectionKey"/>],
    /// skipping entries whose title already appears as an owned row.
    /// </summary>
    private static void MergeChildArray(
        Dictionary<string, List<CollectionGroupWorkDto>> sectionMap,
        System.Text.Json.JsonElement childArray,
        string sectionKey,
        bool isEpisode,
        string? fallbackCover)
    {
        // Build a set of owned titles in this section for O(1) dedup.
        var ownedTitles = sectionMap.TryGetValue(sectionKey, out var existing)
            ? existing
                .Where(w => w.IsOwned && !string.IsNullOrWhiteSpace(w.Title))
                .Select(w => isEpisode ? w.Title.Trim().ToLowerInvariant() : NormalizeTrackTitle(w.Title))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!sectionMap.ContainsKey(sectionKey))
        {
            sectionMap[sectionKey] = [];
        }

        int wikiOrdinal = 0;
        foreach (var el in childArray.EnumerateArray())
        {
            wikiOrdinal++;
            var title = el.TryGetProperty("title", out var tEl)
                && tEl.ValueKind == System.Text.Json.JsonValueKind.String
                ? tEl.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            // Skip if an owned row with the same title is already in this section.
            var titleKey = isEpisode ? title.Trim().ToLowerInvariant() : NormalizeTrackTitle(title);
            if (ownedTitles.Contains(titleKey))
            {
                continue;
            }

            var trackNumber = ReadJsonInt(el, "track_number", "trackNumber", "number");
            var discNumber = ReadJsonInt(el, "disc_number", "discNumber");
            var ordinal = ReadJsonInt(el, "ordinal", "position") ?? trackNumber ?? wikiOrdinal;
            var durationSeconds = ReadChildDurationSeconds(el);
            var appleMusicId = ReadJsonString(el, "apple_music_id", "appleMusicId", "trackId");

            var episodeNumStr = isEpisode
                ? (ReadJsonInt(el, "episode_number", "episodeNumber") ?? ordinal).ToString(CultureInfo.InvariantCulture)
                : null;

            sectionMap[sectionKey].Add(new CollectionGroupWorkDto
            {
                WorkId = Guid.Empty,
                Title = title,
                Ordinal = ordinal,
                Episode = episodeNumStr,
                TrackNumber = isEpisode ? null : (trackNumber ?? ordinal).ToString(CultureInfo.InvariantCulture),
                DiscNumber = isEpisode ? null : discNumber,
                AppleMusicId = isEpisode ? null : appleMusicId,
                Duration = FormatAudioDuration(durationSeconds, null),
                DurationSeconds = durationSeconds,
                CoverUrl = fallbackCover,
                Status = "Missing",
                IsOwned = false,
            });
        }
    }

    private static List<CollectionGroupWorkDto> SortAlbumTracks(IEnumerable<CollectionGroupWorkDto> tracks)
        => tracks
            .OrderBy(track => track.DiscNumber ?? 1)
            .ThenBy(track => ParseNullableInt(track.TrackNumber) ?? track.Ordinal ?? int.MaxValue)
            .ThenBy(track => track.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string NormalizeTrackTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().ToLowerInvariant();
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s*[\(\[\{].*?[\)\]\}]\s*", " ");
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\b(remaster(ed)?|remix|mono|stereo|explicit|clean|single version|album version)\b", " ");
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[^\p{L}\p{Nd}]+", " ");
        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string BuildTrackMatchKey(string? title, int? discNumber, int? trackNumber)
        => $"{NormalizeTrackTitle(title)}|{discNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}|{trackNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}";

    private static bool HasKnownTrackIdentityConflict(
        CollectionGroupWorkDto owned,
        int? manifestDiscNumber,
        int? manifestTrackNumber,
        double? manifestDurationSeconds)
    {
        if (owned.DiscNumber is { } ownedDisc
            && manifestDiscNumber is { } manifestDisc
            && ownedDisc != manifestDisc)
        {
            return true;
        }

        var ownedTrackNumber = ParseNullableInt(owned.TrackNumber);
        if (ownedTrackNumber is { } ownedTrack
            && manifestTrackNumber is { } manifestTrack
            && ownedTrack != manifestTrack)
        {
            return true;
        }

        if (owned.DurationSeconds is { } ownedDuration
            && manifestDurationSeconds is { } manifestDuration
            && Math.Abs(ownedDuration - manifestDuration) > 3)
        {
            return true;
        }

        return false;
    }

    private static string? ReadJsonString(JsonElement element, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static int? ReadJsonInt(JsonElement element, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!element.TryGetProperty(key, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed))
            {
                return parsed;
            }

            if (value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static double? ReadJsonDouble(JsonElement element, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!element.TryGetProperty(key, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var parsed))
            {
                return parsed;
            }

            if (value.ValueKind == JsonValueKind.String
                && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static double? ReadChildDurationSeconds(JsonElement element)
    {
        var seconds = ReadJsonDouble(element, "duration_seconds", "durationSeconds");
        if (seconds is > 0)
        {
            return seconds;
        }

        var millis = ReadJsonDouble(element, "duration_ms", "durationMillis", "trackTimeMillis");
        if (millis is > 0)
        {
            return millis.Value / 1000d;
        }

        return NormalizeAudioDurationSeconds(ReadJsonString(element, "duration", "runtime"));
    }

    private static double? NormalizeAudioDurationSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var span) && span.TotalSeconds > 0)
        {
            return span.TotalSeconds;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric) && numeric > 0)
        {
            return numeric >= 60000 ? numeric / 1000d : numeric;
        }

        return null;
    }

    private static string? FormatAudioDuration(double? seconds, string? fallback)
    {
        if (seconds is > 0)
        {
            var span = TimeSpan.FromSeconds(seconds.Value);
            return span.TotalHours >= 1
                ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : span.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }

        var fallbackSeconds = NormalizeAudioDurationSeconds(fallback);
        if (fallbackSeconds is > 0)
        {
            return FormatAudioDuration(fallbackSeconds, null);
        }

        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }

    private static string? FirstCanonicalValue(IReadOnlyList<CanonicalValue> values, params string[] keys)
        => values
            .FirstOrDefault(value => keys.Any(key => string.Equals(value.Key, key, StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrWhiteSpace(value.Value))
            ?.Value;

    private static CollectionPalette ResolvePalette(
        Guid? entityId,
        IReadOnlyList<CanonicalValue> canonicalValues,
        IDatabaseConnection db)
    {
        var primary = FirstCanonicalValue(canonicalValues,
            MetadataFieldConstants.ArtworkPrimaryHex,
            "cover_primary_hex",
            "primary_color");
        var secondary = FirstCanonicalValue(canonicalValues,
            MetadataFieldConstants.ArtworkSecondaryHex,
            "cover_secondary_hex",
            "secondary_color");
        var accent = FirstCanonicalValue(canonicalValues,
            MetadataFieldConstants.ArtworkAccentHex,
            "cover_accent_hex",
            "accent_color",
            "dominant_color");

        var colors = new List<string>();
        AddColor(colors, primary);
        AddColor(colors, secondary);
        AddColor(colors, accent);

        if (entityId.HasValue && (string.IsNullOrWhiteSpace(primary) || string.IsNullOrWhiteSpace(secondary) || string.IsNullOrWhiteSpace(accent)))
        {
            using var conn = db.CreateConnection();
            var row = conn.QueryFirstOrDefault<AssetPaletteRow>("""
                SELECT primary_hex AS PrimaryHex,
                       secondary_hex AS SecondaryHex,
                       accent_hex AS AccentHex
                FROM entity_assets
                WHERE entity_id = @EntityId
                  AND entity_type = 'Work'
                  AND asset_type IN ('CoverArt', 'SquareArt', 'Background', 'Banner')
                ORDER BY is_preferred DESC, created_at DESC, id
                LIMIT 1;
                """, new { EntityId = entityId.Value });

            primary ??= row?.PrimaryHex;
            secondary ??= row?.SecondaryHex;
            accent ??= row?.AccentHex;
            AddColor(colors, row?.PrimaryHex);
            AddColor(colors, row?.SecondaryHex);
            AddColor(colors, row?.AccentHex);
        }

        return new CollectionPalette(primary, secondary, accent, colors);
    }

    private static void AddColor(List<string> colors, string? color)
    {
        if (!string.IsNullOrWhiteSpace(color) && !colors.Contains(color, StringComparer.OrdinalIgnoreCase))
        {
            colors.Add(color);
        }
    }

    private static async Task<string?> EnsureAppleAlbumTrackManifestAsync(
        Guid? rootWorkId,
        string? artist,
        string? album,
        string? existingChildEntitiesJson,
        IReadOnlyList<CanonicalValue> rootCanonicalValues,
        ICanonicalValueRepository canonicalRepo,
        AppleRetailClient appleRetailClient,
        CancellationToken ct)
    {
        if (!NeedsAppleAlbumTrackGapFill(existingChildEntitiesJson))
        {
            return existingChildEntitiesJson;
        }

        var collectionId = FirstCanonicalValue(rootCanonicalValues, BridgeIdKeys.AppleMusicCollectionId);
        if (string.IsNullOrWhiteSpace(collectionId))
        {
            collectionId = await appleRetailClient.SearchAlbumAsync(artist, album, "us", "en", ct);
        }

        if (string.IsNullOrWhiteSpace(collectionId))
        {
            return existingChildEntitiesJson;
        }

        var appleTracks = await appleRetailClient.FetchAlbumTracksAsync(collectionId, "us", "en", ct);
        if (appleTracks.Count == 0)
        {
            return existingChildEntitiesJson;
        }

        var appleManifest = BuildAppleAlbumTrackManifest(appleTracks);
        var mergedManifest = MergeTrackManifests(existingChildEntitiesJson, appleManifest);
        if (rootWorkId.HasValue && !string.IsNullOrWhiteSpace(mergedManifest) && !string.Equals(mergedManifest, existingChildEntitiesJson, StringComparison.Ordinal))
        {
            await canonicalRepo.UpsertBatchAsync(
                [
                    new CanonicalValue
                    {
                        EntityId = rootWorkId.Value,
                        Key = MetadataFieldConstants.ChildEntitiesJson,
                        Value = mergedManifest,
                        LastScoredAt = DateTimeOffset.UtcNow,
                        WinningProviderId = WellKnownProviders.AppleApi,
                    },
                    new CanonicalValue
                    {
                        EntityId = rootWorkId.Value,
                        Key = MetadataFieldConstants.TrackCount,
                        Value = CountManifestTracks(mergedManifest).ToString(CultureInfo.InvariantCulture),
                        LastScoredAt = DateTimeOffset.UtcNow,
                        WinningProviderId = WellKnownProviders.AppleApi,
                    },
                ],
                ct);
        }

        return mergedManifest;
    }

    private static bool NeedsAppleAlbumTrackGapFill(string? childEntitiesJson)
    {
        if (string.IsNullOrWhiteSpace(childEntitiesJson))
        {
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(childEntitiesJson);
            if (!doc.RootElement.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array || tracks.GetArrayLength() == 0)
            {
                return true;
            }

            return tracks.EnumerateArray().Any(track =>
                string.IsNullOrWhiteSpace(ReadJsonString(track, "title", "trackName", "name"))
                || ReadJsonInt(track, "ordinal", "position") is null
                || ReadJsonInt(track, "track_number", "trackNumber", "number") is null
                || ReadChildDurationSeconds(track) is null);
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static string BuildAppleAlbumTrackManifest(IReadOnlyList<JsonNode> tracks)
    {
        var array = new JsonArray();
        var ordinal = 0;
        foreach (var track in tracks)
        {
            ordinal++;
            var title = track["trackName"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var trackNumber = track["trackNumber"]?.GetValue<int?>() ?? ordinal;
            var durationMillis = track["trackTimeMillis"]?.GetValue<long?>();
            var item = new JsonObject
            {
                ["title"] = title,
                ["ordinal"] = trackNumber,
                ["track_number"] = trackNumber,
                ["source"] = "apple_itunes",
            };

            if (track["discNumber"]?.GetValue<int?>() is { } discNumber)
            {
                item["disc_number"] = discNumber;
            }

            if (durationMillis is > 0)
            {
                item["duration_seconds"] = Math.Round(durationMillis.Value / 1000d, 3);
            }

            if (track["trackId"]?.GetValue<long?>() is { } trackId)
            {
                item["apple_music_id"] = trackId.ToString(CultureInfo.InvariantCulture);
            }

            array.Add(item);
        }

        return new JsonObject { ["tracks"] = array }.ToJsonString();
    }

    private static string MergeTrackManifests(string? existingJson, string appleJson)
    {
        var items = ReadTrackManifest(existingJson, defaultSource: "wikidata");
        var appleItems = ReadTrackManifest(appleJson, defaultSource: "apple_itunes");
        if (items.Count == 0)
        {
            items = appleItems;
        }
        else
        {
            foreach (var appleItem in appleItems)
            {
                var existing = !string.IsNullOrWhiteSpace(appleItem.AppleMusicId)
                    ? items.FirstOrDefault(item => string.Equals(item.AppleMusicId, appleItem.AppleMusicId, StringComparison.OrdinalIgnoreCase))
                    : null;
                existing ??= items.FirstOrDefault(item =>
                    string.Equals(
                        BuildTrackMatchKey(item.Title, item.DiscNumber, item.TrackNumber),
                        BuildTrackMatchKey(appleItem.Title, appleItem.DiscNumber, appleItem.TrackNumber),
                        StringComparison.OrdinalIgnoreCase))
                    ?? items.FirstOrDefault(item =>
                        string.Equals(NormalizeTrackTitle(item.Title), NormalizeTrackTitle(appleItem.Title), StringComparison.OrdinalIgnoreCase)
                        && !HasTrackManifestIdentityConflict(item, appleItem));

                if (existing is null)
                {
                    items.Add(appleItem);
                    continue;
                }

                existing.TrackNumber ??= appleItem.TrackNumber;
                existing.Ordinal = existing.Ordinal <= 0 ? appleItem.Ordinal : existing.Ordinal;
                existing.DiscNumber ??= appleItem.DiscNumber;
                existing.DurationSeconds ??= appleItem.DurationSeconds;
                existing.AppleMusicId ??= appleItem.AppleMusicId;
            }
        }

        var array = new JsonArray();
        foreach (var item in items
                     .OrderBy(item => item.DiscNumber ?? 1)
                     .ThenBy(item => item.TrackNumber ?? item.Ordinal)
                     .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase))
        {
            var obj = new JsonObject
            {
                ["title"] = item.Title,
                ["ordinal"] = item.Ordinal,
                ["source"] = item.Source,
            };
            if (item.TrackNumber is { } trackNumber)
            {
                obj["track_number"] = trackNumber;
            }
            if (item.DiscNumber is { } discNumber)
            {
                obj["disc_number"] = discNumber;
            }
            if (item.DurationSeconds is { } duration)
            {
                obj["duration_seconds"] = Math.Round(duration, 3);
            }
            if (!string.IsNullOrWhiteSpace(item.AppleMusicId))
            {
                obj["apple_music_id"] = item.AppleMusicId;
            }

            array.Add(obj);
        }

        return new JsonObject { ["tracks"] = array }.ToJsonString();
    }

    private static bool HasTrackManifestIdentityConflict(
        AlbumTrackManifestItem existing,
        AlbumTrackManifestItem incoming)
    {
        if (existing.DiscNumber is { } existingDisc
            && incoming.DiscNumber is { } incomingDisc
            && existingDisc != incomingDisc)
        {
            return true;
        }

        if (existing.TrackNumber is { } existingTrack
            && incoming.TrackNumber is { } incomingTrack
            && existingTrack != incomingTrack)
        {
            return true;
        }

        if (existing.DurationSeconds is { } existingDuration
            && incoming.DurationSeconds is { } incomingDuration
            && Math.Abs(existingDuration - incomingDuration) > 3)
        {
            return true;
        }

        return false;
    }

    private static List<AlbumTrackManifestItem> ReadTrackManifest(string? json, string defaultSource)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var ordinal = 0;
            var result = new List<AlbumTrackManifestItem>();
            foreach (var track in tracks.EnumerateArray())
            {
                ordinal++;
                var title = ReadJsonString(track, "title", "trackName", "name");
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var trackNumber = ReadJsonInt(track, "track_number", "trackNumber", "number");
                result.Add(new AlbumTrackManifestItem
                {
                    Title = title,
                    Ordinal = ReadJsonInt(track, "ordinal", "position") ?? trackNumber ?? ordinal,
                    TrackNumber = trackNumber,
                    DiscNumber = ReadJsonInt(track, "disc_number", "discNumber"),
                    DurationSeconds = ReadChildDurationSeconds(track),
                    AppleMusicId = ReadJsonString(track, "apple_music_id", "appleMusicId", "trackId"),
                    Source = ReadJsonString(track, "source", "provider") ?? defaultSource,
                });
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int CountManifestTracks(string manifestJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(manifestJson);
            return doc.RootElement.TryGetProperty("tracks", out var tracks) && tracks.ValueKind == JsonValueKind.Array
                ? tracks.GetArrayLength()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private sealed record CollectionPalette(
        string? PrimaryColor,
        string? SecondaryColor,
        string? AccentColor,
        List<string> DominantColors);

    private sealed class AssetPaletteRow
    {
        public string? PrimaryHex { get; init; }
        public string? SecondaryHex { get; init; }
        public string? AccentHex { get; init; }
    }

    private sealed class AlbumTrackManifestItem
    {
        public string Title { get; init; } = string.Empty;
        public int Ordinal { get; set; }
        public int? TrackNumber { get; set; }
        public int? DiscNumber { get; set; }
        public double? DurationSeconds { get; set; }
        public string? AppleMusicId { get; set; }
        public string Source { get; init; } = "provider";
    }

    private static List<CollectionResolvedItemDto> ResolveEntityMetadata(IDatabaseConnection db, IReadOnlyList<Guid> entityIds)
    {
        if (entityIds.Count == 0)
        {
            return [];
        }

        using var conn = db.CreateConnection();
        var result = new List<CollectionResolvedItemDto>();

        foreach (var entityId in entityIds)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT cv.key, cv.value
                FROM editions e
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                INNER JOIN canonical_values cv ON cv.entity_id = ma.id
                WHERE e.work_id = @EntityId
                  AND cv.key IN ('title', 'author', 'director', 'artist', 'year')
                UNION ALL
                SELECT 'media_type', w.media_type
                FROM works w WHERE w.id = @EntityId
                UNION ALL
                SELECT '_asset_id', MIN(ma2.id)
                FROM editions e2
                INNER JOIN media_assets ma2 ON ma2.edition_id = e2.id
                WHERE e2.work_id = @EntityId
                """;
            var p = cmd.CreateParameter();
            p.ParameterName = "@EntityId";
            p.Value = entityId.ToString();
            cmd.Parameters.Add(p);

            string? title = null, creator = null, mediaType = null, cover = null, year = null;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                var val = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (string.IsNullOrEmpty(val))
                {
                    continue;
                }

                switch (key)
                {
                    case "title": title = val; break;
                    case "author" when creator is null: creator = val; break;
                    case "artist" when creator is null: creator = val; break;
                    case "_asset_id": cover = $"/stream/{val}/cover"; break;
                    case "year": year = val; break;
                    case "media_type": mediaType = val; break;
                }
            }

            result.Add(new CollectionResolvedItemDto
            {
                EntityId = entityId,
                Title = title ?? "Unknown",
                Creator = creator,
                MediaType = mediaType ?? "Unknown",
                CoverUrl = cover,
                Year = year,
            });
        }

        return result;
    }

    private static IReadOnlyList<ContentGroupDto> NormalizeSystemViewGroups(
        IReadOnlyList<ContentGroupDto> groups,
        string? mediaType,
        string? groupField)
    {
        if (groups.Count == 0)
        {
            return groups;
        }

        var normalizedGroups = IsMusicAlbumSystemView(mediaType, groupField)
            ? groups
                .GroupBy(group => BuildSystemViewGroupIdentity(group, mediaType, groupField), StringComparer.OrdinalIgnoreCase)
                .Select(group => (Key: group.Key, Items: (IReadOnlyList<ContentGroupDto>)group.ToList()))
                .ToList()
            : groups
                .Select(group => (
                    Key: BuildSystemViewGroupIdentity(group, mediaType, groupField),
                    Items: (IReadOnlyList<ContentGroupDto>)[group]))
                .ToList();

        return normalizedGroups
            .Select(group =>
            {
                var preferred = group.Items
                    .OrderByDescending(ScoreSystemViewGroup)
                    .ThenByDescending(item => item.CreatedAt)
                    .First();

                var seasonCounts = group.Items
                    .Where(item => item.SeasonCount.HasValue)
                    .Select(item => item.SeasonCount!.Value)
                    .ToList();

                var albumCounts = group.Items
                    .Where(item => item.AlbumCount.HasValue)
                    .Select(item => item.AlbumCount!.Value)
                    .ToList();

                return new ContentGroupDto
                {
                    CollectionId = CreateDeterministicSystemViewGroupId($"{mediaType}|{groupField}|{group.Key}"),
                    DisplayName = preferred.DisplayName.Trim(),
                    WikidataQid = preferred.WikidataQid,
                    PrimaryMediaType = preferred.PrimaryMediaType,
                    WorkCount = group.Items.Max(item => item.WorkCount),
                    DistinctTitleCount = group.Items
                        .Where(item => item.DistinctTitleCount.HasValue)
                        .Select(item => item.DistinctTitleCount!.Value)
                        .DefaultIfEmpty(group.Items.Max(item => item.WorkCount))
                        .Max(),
                    CoverUrl = preferred.CoverUrl,
                    BackgroundUrl = preferred.BackgroundUrl,
                    BannerUrl = preferred.BannerUrl,
                    HeroUrl = null,
                    LogoUrl = preferred.LogoUrl,
                    CoverAspectClass = preferred.CoverAspectClass,
                    SquareAspectClass = preferred.SquareAspectClass,
                    BackgroundAspectClass = preferred.BackgroundAspectClass,
                    BannerAspectClass = preferred.BannerAspectClass,
                    CoverWidthPx = preferred.CoverWidthPx,
                    CoverHeightPx = preferred.CoverHeightPx,
                    SquareWidthPx = preferred.SquareWidthPx,
                    SquareHeightPx = preferred.SquareHeightPx,
                    BackgroundWidthPx = preferred.BackgroundWidthPx,
                    BackgroundHeightPx = preferred.BackgroundHeightPx,
                    BannerWidthPx = preferred.BannerWidthPx,
                    BannerHeightPx = preferred.BannerHeightPx,
                    Description = preferred.Description,
                    Tagline = preferred.Tagline,
                    Creator = preferred.Creator,
                    Director = preferred.Director,
                    Writer = preferred.Writer,
                    ReleaseDate = preferred.ReleaseDate,
                    UniverseStatus = preferred.UniverseStatus,
                    CreatedAt = preferred.CreatedAt,
                    ArtistPhotoUrl = preferred.ArtistPhotoUrl,
                    ArtistPersonId = preferred.ArtistPersonId,
                    Network = preferred.Network,
                    Year = preferred.Year,
                    SeasonCount = seasonCounts.Count == 0 ? null : seasonCounts.Max(),
                    AlbumCount = albumCounts.Count == 0 ? null : albumCounts.Max(),
                };
            })
            .ToList();
    }

    private static bool IsMusicAlbumSystemView(string? mediaType, string? groupField)
        => string.Equals(mediaType, "Music", StringComparison.OrdinalIgnoreCase)
           && string.Equals(groupField, "album", StringComparison.OrdinalIgnoreCase);

    private static bool IsMusicSystemView(string? mediaType, string? groupField)
        => string.Equals(mediaType, "Music", StringComparison.OrdinalIgnoreCase)
           && (string.Equals(groupField, "album", StringComparison.OrdinalIgnoreCase)
               || string.Equals(groupField, "artist", StringComparison.OrdinalIgnoreCase));

    private static async Task<IReadOnlyList<ContentGroupDto>> BuildMusicSystemViewFallbackGroupsAsync(
        System.Data.IDbConnection conn,
        IPersonRepository personRepo,
        string groupField,
        ILogger log,
        CancellationToken ct)
    {
        var visibleAssetPredicate = HomeVisibilitySql.VisibleAssetPathPredicate("ma.file_path_root");
        var rows = (await conn.QueryAsync<MusicSystemViewGroupRow>(new CommandDefinition($"""
            WITH work_assets AS (
                SELECT
                    w.id AS WorkId,
                    ma.id AS AssetId,
                    COALESCE(gp.id, p.id, w.id) AS RootWorkId,
                    ma.file_path_root AS FilePathRoot
                FROM works w
                INNER JOIN editions e ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                LEFT JOIN works p ON p.id = w.parent_work_id
                LEFT JOIN works gp ON gp.id = p.parent_work_id
                WHERE w.media_type = 'Music'
                  AND COALESCE(w.is_catalog_only, 0) = 0
                  AND {visibleAssetPredicate}
            ),
            resolved_values AS (
                SELECT
                    wa.WorkId,
                    wa.AssetId,
                    COALESCE(
                        NULLIF(TRIM(root_album.value), ''),
                        NULLIF(TRIM(work_album.value), ''),
                        NULLIF(TRIM(asset_album.value), '')
                    ) AS Album,
                    COALESCE(
                        NULLIF(TRIM(root_artist.value), ''),
                        NULLIF(TRIM(work_artist.value), ''),
                        NULLIF(TRIM(asset_artist.value), '')
                    ) AS Artist,
                    COALESCE(
                        NULLIF(TRIM(asset_title.value), ''),
                        NULLIF(TRIM(work_title.value), '')
                    ) AS Title,
                    COALESCE(
                        NULLIF(TRIM(root_year.value), ''),
                        NULLIF(TRIM(work_year.value), ''),
                        NULLIF(TRIM(asset_year.value), '')
                    ) AS Year,
                    COALESCE(
                        NULLIF(TRIM(root_description.value), ''),
                        NULLIF(TRIM(work_description.value), ''),
                        NULLIF(TRIM(asset_description.value), '')
                    ) AS Description,
                    COALESCE(
                        NULLIF(TRIM(root_cover_aspect.value), ''),
                        NULLIF(TRIM(work_cover_aspect.value), ''),
                        NULLIF(TRIM(asset_cover_aspect.value), '')
                    ) AS CoverAspectClass
                FROM work_assets wa
                LEFT JOIN canonical_values root_album ON root_album.entity_id = wa.RootWorkId AND root_album.key = 'album'
                LEFT JOIN canonical_values work_album ON work_album.entity_id = wa.WorkId AND work_album.key = 'album'
                LEFT JOIN canonical_values asset_album ON asset_album.entity_id = wa.AssetId AND asset_album.key = 'album'
                LEFT JOIN canonical_values root_artist ON root_artist.entity_id = wa.RootWorkId AND root_artist.key = 'artist'
                LEFT JOIN canonical_values work_artist ON work_artist.entity_id = wa.WorkId AND work_artist.key = 'artist'
                LEFT JOIN canonical_values asset_artist ON asset_artist.entity_id = wa.AssetId AND asset_artist.key = 'artist'
                LEFT JOIN canonical_values work_title ON work_title.entity_id = wa.WorkId AND work_title.key = 'title'
                LEFT JOIN canonical_values asset_title ON asset_title.entity_id = wa.AssetId AND asset_title.key = 'title'
                LEFT JOIN canonical_values root_year ON root_year.entity_id = wa.RootWorkId AND root_year.key = 'year'
                LEFT JOIN canonical_values work_year ON work_year.entity_id = wa.WorkId AND work_year.key = 'year'
                LEFT JOIN canonical_values asset_year ON asset_year.entity_id = wa.AssetId AND asset_year.key = 'year'
                LEFT JOIN canonical_values root_description ON root_description.entity_id = wa.RootWorkId AND root_description.key = 'description'
                LEFT JOIN canonical_values work_description ON work_description.entity_id = wa.WorkId AND work_description.key = 'description'
                LEFT JOIN canonical_values asset_description ON asset_description.entity_id = wa.AssetId AND asset_description.key = 'description'
                LEFT JOIN canonical_values root_cover_aspect ON root_cover_aspect.entity_id = wa.RootWorkId AND root_cover_aspect.key = 'cover_aspect_class'
                LEFT JOIN canonical_values work_cover_aspect ON work_cover_aspect.entity_id = wa.WorkId AND work_cover_aspect.key = 'cover_aspect_class'
                LEFT JOIN canonical_values asset_cover_aspect ON asset_cover_aspect.entity_id = wa.AssetId AND asset_cover_aspect.key = 'cover_aspect_class'
            ),
            grouped AS (
                SELECT
                    CASE
                        WHEN @GroupField = 'artist' THEN Artist
                        ELSE Album
                    END AS GroupName,
                    CASE
                        WHEN @GroupField = 'album' THEN Artist
                    END AS Creator,
                    COUNT(DISTINCT WorkId) AS WorkCount,
                    COUNT(DISTINCT COALESCE(NULLIF(Title, ''), hex(WorkId))) AS DistinctTitleCount,
                    COUNT(DISTINCT COALESCE(NULLIF(Album, ''), hex(WorkId))) AS AlbumCount,
                    MIN(AssetId) AS FirstAssetId,
                    MIN(Year) AS Year,
                    MIN(Description) AS Description,
                    MIN(CoverAspectClass) AS CoverAspectClass
                FROM resolved_values
                WHERE CASE
                        WHEN @GroupField = 'artist' THEN Artist
                        ELSE Album
                    END IS NOT NULL
                GROUP BY
                    CASE
                        WHEN @GroupField = 'artist' THEN lower(Artist)
                        ELSE lower(Album)
                    END,
                    CASE
                        WHEN @GroupField = 'album' THEN lower(COALESCE(Artist, ''))
                    END
            )
            SELECT
                GroupName,
                Creator,
                WorkCount,
                DistinctTitleCount,
                AlbumCount,
                FirstAssetId,
                Year,
                Description,
                CoverAspectClass
            FROM grouped
            ORDER BY GroupName
            """,
            new { GroupField = groupField.ToLowerInvariant() },
            cancellationToken: ct))).AsList();

        log.LogInformation("[ByAlbum] Music canonical fallback for groupField={GroupField} returned {RowCount} distinct group(s)",
            groupField, rows.Count);

        var isArtistGroup = string.Equals(groupField, "artist", StringComparison.OrdinalIgnoreCase);
        var result = new List<ContentGroupDto>(rows.Count);
        foreach (var row in rows)
        {
            string? artistPhotoUrl = null;
            Guid? artistPersonId = null;

            if (isArtistGroup && !string.IsNullOrWhiteSpace(row.GroupName))
            {
                try
                {
                    var person = await personRepo.FindByNameAsync(row.GroupName, ct);
                    if (person is not null)
                    {
                        artistPersonId = person.Id;
                        if (!string.IsNullOrEmpty(person.LocalHeadshotPath) || !string.IsNullOrEmpty(person.HeadshotUrl))
                        {
                            artistPhotoUrl = $"/persons/{person.Id}/headshot";
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.LogDebug(ex, "Artist lookup failed while building Music system-view fallback group {Artist}", row.GroupName);
                }
            }

            result.Add(new ContentGroupDto
            {
                CollectionId = CreateDeterministicSystemViewGroupId($"Music|{groupField}|{row.GroupName}|{row.Creator}"),
                DisplayName = row.GroupName,
                WikidataQid = null,
                PrimaryMediaType = "Music",
                WorkCount = row.WorkCount,
                DistinctTitleCount = row.DistinctTitleCount,
                CoverUrl = row.FirstAssetId.HasValue ? $"/stream/{row.FirstAssetId.Value}/cover" : null,
                CoverAspectClass = row.CoverAspectClass,
                Description = row.Description,
                Creator = row.Creator,
                UniverseStatus = "Complete",
                CreatedAt = DateTimeOffset.UtcNow,
                ArtistPhotoUrl = artistPhotoUrl,
                ArtistPersonId = artistPersonId,
                Year = row.Year,
                AlbumCount = isArtistGroup && row.AlbumCount > 0 ? row.AlbumCount : null,
            });
        }

        return result;
    }

    private sealed class MusicSystemViewGroupRow
    {
        public string GroupName { get; init; } = string.Empty;
        public string? Creator { get; init; }
        public int WorkCount { get; init; }
        public int DistinctTitleCount { get; init; }
        public int AlbumCount { get; init; }
        public Guid? FirstAssetId { get; init; }
        public string? Year { get; init; }
        public string? Description { get; init; }
        public string? CoverAspectClass { get; init; }
    }

    private static int CountDistinctWorkTitles(IEnumerable<Work> works)
    {
        var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var work in works)
        {
            var dto = WorkDto.FromDomain(work);
            var title = GetCanonical(dto, "title") ?? GetCanonical(dto, "original_title");
            titles.Add(NormalizeDistinctTitle(title) ?? work.Id.ToString("N"));
        }

        return titles.Count;
    }

    private static string? NormalizeDistinctTitle(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string BuildSystemViewGroupIdentity(ContentGroupDto group, string? mediaType, string? groupField)
    {
        var name = NormalizeSystemViewIdentity(group.DisplayName);
        if (string.Equals(mediaType, "Music", StringComparison.OrdinalIgnoreCase)
            && string.Equals(groupField, "album", StringComparison.OrdinalIgnoreCase))
        {
            return $"{name}|{NormalizeSystemViewIdentity(group.Creator)}";
        }

        return string.Join("|",
            name,
            NormalizeSystemViewIdentity(group.Creator),
            NormalizeSystemViewIdentity(group.Network),
            NormalizeSystemViewIdentity(group.Year));
    }

    private static int ScoreSystemViewGroup(ContentGroupDto group)
    {
        var score = 0;
        score += string.IsNullOrWhiteSpace(group.CoverUrl) ? 0 : 8;
        score += string.IsNullOrWhiteSpace(group.ArtistPhotoUrl) ? 0 : 8;
        score += string.IsNullOrWhiteSpace(group.Description) ? 0 : 4;
        score += string.IsNullOrWhiteSpace(group.Creator) ? 0 : 2;
        score += group.WorkCount;
        return score;
    }

    private static string NormalizeSystemViewIdentity(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "(blank)"
            : value.Trim().ToLowerInvariant();

    private static Guid CreateDeterministicSystemViewGroupId(string value)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes);
    }

    /// <summary>
    /// Lineage-aware variant of <see cref="ResolveEntityMetadata"/> used by the
    /// <c>/resolve/by-name</c> endpoint.  For each Work this reads canonical values
    /// from both the asset row (Self-scoped fields: title, track_number) and from
    /// the topmost parent Work row (Parent-scoped fields: artist, album, genre,
    /// year).  Cover art is resolved via <c>/stream/{assetId}/cover</c> from the
    /// asset ID rather than canonical_values.  This mirrors the LibraryItemRepository
    /// pattern so that music items have correct artist/album/cover values even
    /// after the lineage-aware write splits them onto the album Work's entity_id.
    /// </summary>
    private static List<CollectionResolvedItemDto> ResolveEntityMetadataWithLineage(
        IDatabaseConnection db,
        IReadOnlyList<Guid> entityIds)
    {
        if (entityIds.Count == 0)
        {
            return [];
        }

        using var conn = db.CreateConnection();
        var result = new List<CollectionResolvedItemDto>(entityIds.Count);

        foreach (var entityId in entityIds)
        {
            using var cmd = conn.CreateCommand();
            // Union 1: Self-scoped canonical values stored on the media_asset row.
            // Union 2: Parent-scoped canonical values stored on the topmost Work row
            //          (COALESCE walks parent_work_id up two levels — covers TV
            //           episode → show and music track → album).
            // Union 3: media_type read directly from the works table (not canonical_values).
            // Union 4: asset_id for cover art URL construction.
            cmd.CommandText = """
                -- Self-scoped: canonical_values keyed on media_asset.id
                SELECT cv.key, cv.value, 0 AS priority
                FROM editions e
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                INNER JOIN canonical_values cv ON cv.entity_id = ma.id
                WHERE e.work_id = @EntityId
                  AND cv.key IN ('title', 'author', 'director', 'artist', 'year', 'album')
                UNION ALL
                -- Parent-scoped: canonical_values keyed on root parent Work id
                SELECT cv.key, cv.value, 1 AS priority
                FROM works w
                LEFT  JOIN works p  ON p.id  = w.parent_work_id
                LEFT  JOIN works gp ON gp.id = p.parent_work_id
                INNER JOIN canonical_values cv
                  ON cv.entity_id = COALESCE(gp.id, p.id, w.id)
                WHERE w.id = @EntityId
                  AND cv.key IN ('title', 'author', 'director', 'artist', 'year', 'album')
                UNION ALL
                -- media_type from works table
                SELECT 'media_type', w2.media_type, 0
                FROM works w2 WHERE w2.id = @EntityId
                UNION ALL
                -- asset_id for cover art URL construction
                SELECT '_asset_id', MIN(ma2.id), 0
                FROM editions e2
                INNER JOIN media_assets ma2 ON ma2.edition_id = e2.id
                WHERE e2.work_id = @EntityId
                """;
            var p = cmd.CreateParameter();
            p.ParameterName = "@EntityId";
            p.Value = entityId.ToString();
            cmd.Parameters.Add(p);

            // Collect all rows; for each key keep the first (priority 0 = asset-level
            // for Self-scope, priority 1 = parent-level for Parent-scope).
            // Self-scope rows win for title/episode fields; Parent-scope rows win for
            // artist/album/cover because they are emitted first from Union 1 only when
            // the value actually lives on the asset (pre-lineage ingestion).
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                var val = reader.IsDBNull(1) ? "" : reader.GetString(1);
                if (string.IsNullOrEmpty(val))
                {
                    continue;
                }
                // First occurrence of each key wins (Union order = Self then Parent).
                if (!seen.ContainsKey(key))
                {
                    seen[key] = val;
                }
            }

            seen.TryGetValue("title", out var title);
            seen.TryGetValue("author", out var author);
            seen.TryGetValue("director", out var director);
            seen.TryGetValue("artist", out var artist);
            seen.TryGetValue("year", out var year);
            seen.TryGetValue("media_type", out var mediaType);

            string? cover = null;
            if (seen.TryGetValue("_asset_id", out var assetId))
            {
                cover = $"/stream/{assetId}/cover";
            }

            var creator = artist ?? author ?? director;

            result.Add(new CollectionResolvedItemDto
            {
                EntityId = entityId,
                Title = !string.IsNullOrEmpty(title) ? title : "Unknown",
                Creator = creator,
                MediaType = !string.IsNullOrEmpty(mediaType) ? mediaType : "Unknown",
                CoverUrl = cover,
                Year = year,
            });
        }

        return result;
    }

    private static string? BuildLookupSubtitle(CollectionMediaLookupRow row)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.Creator))
        {
            parts.Add(row.Creator);
        }

        if (!string.IsNullOrWhiteSpace(row.Year))
        {
            parts.Add(row.Year);
        }

        if (!string.IsNullOrWhiteSpace(row.MediaType))
        {
            parts.Add(row.MediaType);
        }

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string? BuildLookupParentContext(CollectionMediaLookupRow row)
    {
        if (string.Equals(row.WorkKind, "parent", StringComparison.OrdinalIgnoreCase))
        {
            return row.MediaType.Contains("Music", StringComparison.OrdinalIgnoreCase) ? "Album"
                : row.MediaType.Contains("TV", StringComparison.OrdinalIgnoreCase) ? "Series"
                : row.MediaType.Contains("comic", StringComparison.OrdinalIgnoreCase) ? "Series"
                : row.MediaType.Contains("book", StringComparison.OrdinalIgnoreCase) ? "Series"
                : "Container";
        }

        if (row.MediaType.Contains("TV", StringComparison.OrdinalIgnoreCase))
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(row.ShowName))
            {
                parts.Add(row.ShowName);
            }

            if (!string.IsNullOrWhiteSpace(row.SeasonNumber))
            {
                parts.Add($"Season {row.SeasonNumber}");
            }

            return parts.Count == 0 ? null : string.Join(" / ", parts);
        }

        if (row.MediaType.Contains("Music", StringComparison.OrdinalIgnoreCase))
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(row.Artist))
            {
                parts.Add(row.Artist);
            }

            if (!string.IsNullOrWhiteSpace(row.Album))
            {
                parts.Add(row.Album);
            }

            return parts.Count == 0 ? null : string.Join(" / ", parts);
        }

        return null;
    }

    private static string BuildLookupRoute(CollectionMediaLookupRow row)
    {
        if (string.Equals(row.WorkKind, "parent", StringComparison.OrdinalIgnoreCase))
        {
            if (row.MediaType.Contains("TV", StringComparison.OrdinalIgnoreCase))
            {
                return $"/details/tvshow/{row.WorkId:D}?context=watch";
            }

            if (row.MediaType.Contains("Music", StringComparison.OrdinalIgnoreCase))
            {
                return $"/details/musicalbum/{row.WorkId:D}?context=listen";
            }

            if (row.MediaType.Contains("comic", StringComparison.OrdinalIgnoreCase))
            {
                return $"/details/comicseries/{row.WorkId:D}?context=comics";
            }

            if (row.MediaType.Contains("book", StringComparison.OrdinalIgnoreCase))
            {
                return $"/details/bookseries/{row.WorkId:D}?context=read";
            }
        }

        if (row.MediaType.Contains("TV", StringComparison.OrdinalIgnoreCase))
        {
            return $"/details/tvepisode/{row.WorkId:D}?context=watch";
        }

        if (row.MediaType.Contains("movie", StringComparison.OrdinalIgnoreCase))
        {
            return $"/watch/movie/{row.WorkId:D}";
        }

        if (row.MediaType.Contains("music", StringComparison.OrdinalIgnoreCase))
        {
            return $"/details/musictrack/{row.WorkId:D}?context=listen";
        }

        if (row.MediaType.Contains("audio", StringComparison.OrdinalIgnoreCase))
        {
            return $"/listen/audiobook/{row.WorkId:D}";
        }

        if (row.MediaType.Contains("comic", StringComparison.OrdinalIgnoreCase))
        {
            return $"/details/comicissue/{row.WorkId:D}?context=comics";
        }

        return $"/book/{row.WorkId:D}";
    }

    private sealed record CollectionMediaLookupRow(
        Guid WorkId,
        string MediaType,
        string? WorkKind,
        int? Ordinal,
        Guid? AssetId,
        string Title,
        string? Creator,
        string? Year,
        string? ArtworkUrl,
        string? ShowName,
        string? SeasonNumber,
        string? Album,
        string? Artist);

    private sealed class GeneratedCollectionItemRow
    {
        public Guid WorkId { get; init; }
        public string Title { get; init; } = string.Empty;
        public object? Creator { get; init; }
        public string MediaType { get; init; } = string.Empty;
        public string? CoverUrl { get; init; }
        public int SortOrder { get; init; }
    }

    private sealed record CollectionDisplayWorkRow(string WorkId);

    private sealed record CollectionCatalogAggregation(string Key, string? Label);

    private sealed record CollectionManagementCatalogCandidate(
        Collection Collection,
        CollectionCatalogClassification Classification,
        CollectionCatalogAggregation? Grouping,
        IReadOnlyList<Guid> WorkIds,
        int ItemCount,
        CollectionMediaCounts MediaCounts);
}
