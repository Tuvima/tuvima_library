using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services;
using MediaEngine.Api.Services.Display;
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
            ICollectionBrowseReadService browseReadService,
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
                var rid = await browseReadService.GetRootWorkIdAsync(collection.Works[0].Id, ct);
                if (rid.HasValue)
                {
                    rootParentWorkId = rid.Value;
                    parentCvs = await canonicalRepo.GetByEntityAsync(rid.Value, ct);
                }
            }

            string? ParentCv(string key) =>
                parentCvs.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

            var rootWorkQid = collection.WikidataQid ?? ParentCv(BridgeIdKeys.WikidataQid);
            var primaryAssetIds = await browseReadService.GetPrimaryAssetIdsAsync(collection.Works.Select(w => w.Id), ct);

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
                    var primaryAssetId = primaryAssetIds.GetValueOrDefault(w.Id);
                    string? coverUrl = BuildCoverStreamUrl(w, primaryAssetId);
                    string? backgroundUrl = BuildBackgroundStreamUrl(w, primaryAssetId);
                    string? bannerUrl = BuildBannerStreamUrl(w, primaryAssetId);
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
            var paletteRow = rootParentWorkId.HasValue
                ? await browseReadService.GetAssetPaletteAsync(rootParentWorkId.Value, ct)
                : null;
            var collectionPalette = ResolvePalette(parentCvs, paletteRow);

            // Resolve cover URL as a /stream/ endpoint. Cover art is downloaded
            // to disk by CoverArtWorker and served via StreamEndpoints. We need
            // the root parent work's asset_id to build the URL.
            string? collectionCover = null;
            string? collectionBackground = null;
            string? collectionBanner = null;
            if (rootParentWorkId.HasValue)
            {
                if (await browseReadService.GetRepresentativeAssetIdAsync(rootParentWorkId.Value, ct) is { } rootAssetId)
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
            ICollectionBrowseReadService browseReadService,
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
                var primaryAssetIds = await browseReadService.GetPrimaryAssetIdsAsync(collection.Works.Select(w => w.Id), ct);
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
                            CoverUrl = BuildCoverStreamUrl(w, primaryAssetIds.GetValueOrDefault(w.Id)),
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
                    albumCover = BuildCoverStreamUrl(
                        collection.Works[0],
                        primaryAssetIds.GetValueOrDefault(collection.Works[0].Id));
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
            ICollectionBrowseReadService browseReadService,
            IPersonRepository personRepo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(artistName))
            {
                return Results.BadRequest("artistName parameter is required");
            }

            var rows = await browseReadService.GetArtistWorksAsync(artistName, ct);
            var albumMap = new Dictionary<string, List<CollectionGroupWorkDto>>(StringComparer.OrdinalIgnoreCase);
            var albumCovers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var albumYears = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var albumChildJson = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            string? combinedCreator = null;
            string? combinedGenre = null;
            var allYears = new List<string>();

            foreach (var row in rows)
            {
                var rawDuration = row.DurationSecondsValue ?? row.Duration ?? row.Runtime;
                var durationSeconds = NormalizeAudioDurationSeconds(rawDuration);
                var displayDuration = FormatAudioDuration(durationSeconds, rawDuration);

                combinedCreator ??= row.Artist;
                combinedGenre ??= row.Genre;

                var year = row.ReleaseYear ?? row.YearValue;
                if (!string.IsNullOrWhiteSpace(year))
                {
                    allYears.Add(year);
                }

                var albumKey = row.Album ?? "Unknown Album";
                if (!albumMap.TryGetValue(albumKey, out var tracks))
                {
                    tracks = [];
                    albumMap[albumKey] = tracks;
                }
                if (!albumCovers.ContainsKey(albumKey))
                {
                    albumCovers[albumKey] = row.Cover;
                }

                if (!albumYears.ContainsKey(albumKey) || string.IsNullOrWhiteSpace(albumYears[albumKey]))
                {
                    albumYears[albumKey] = year;
                }

                if (!albumChildJson.ContainsKey(albumKey) || string.IsNullOrWhiteSpace(albumChildJson[albumKey]))
                {
                    albumChildJson[albumKey] = row.ChildEntitiesJson;
                }

                tracks.Add(new CollectionGroupWorkDto
                {
                    WorkId = row.WorkId,
                    AssetId = row.AssetId,
                    Title = row.Title ?? $"Track {row.WorkId.ToString("N")[..8]}",
                    Year = year,
                    Duration = displayDuration,
                    DurationSeconds = durationSeconds,
                    CoverUrl = row.Cover,
                    TrackNumber = row.TrackNumber,
                    DiscNumber = ParseNullableInt(row.DiscNumber),
                    AppleMusicId = row.AppleMusicId,
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
            ICollectionBrowseReadService browseReadService,
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

            var rows = await browseReadService.GetSystemViewDetailWorksAsync(
                groupField,
                groupValue,
                mediaType,
                artistName,
                ct);
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

            foreach (var row in rows)
            {
                var workId = row.WorkId;
                var assetId = row.AssetId;
                var rootWorkId = row.RootWorkId;
                var title = row.Title;
                var episodeTitle = row.EpisodeTitle;
                var cover = row.Cover;
                var background = row.Background;
                var banner = row.Banner;
                var hero = row.Hero;
                var logo = row.Logo;
                var primaryColor = row.PrimaryColor;
                var secondaryColor = row.SecondaryColor;
                var accentColor = row.AccentColor;
                var genre = row.Genre;
                var durationSecondsValue = row.DurationSecondsValue;
                var duration = row.Duration;
                var runtime = row.Runtime;
                var rawDuration = durationSecondsValue ?? duration ?? runtime;
                var durationSeconds = isMusicAlbumGroup ? NormalizeAudioDurationSeconds(rawDuration) : null;
                var displayDuration = isMusicAlbumGroup ? FormatAudioDuration(durationSeconds, rawDuration) : rawDuration;
                var releaseYear = row.ReleaseYear;
                var yearVal = row.YearValue;
                var episodeNum = row.EpisodeNumber;
                var trackNum = row.TrackNumber;
                var discNum = row.DiscNumber;
                var appleMusicId = row.AppleMusicId;
                var seqIndex = row.SeriesIndex;
                var childJson = row.ChildEntitiesJson;

                // Accumulate the first non-null child_entities_json we encounter —
                // it may appear on any owned sibling in the same group.
                collectedChildJson ??= string.IsNullOrWhiteSpace(childJson) ? null : childJson;

                // Determine creator (author, director, artist, or network for TV)
                var creator = row.Author;
                var directorVal = row.Director;
                var artistVal = row.Artist;
                var networkVal = row.Network;
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
                    var secVal = secondaryGroup switch
                    {
                        "season_number" => row.SeasonNumber,
                        "album" => row.Album,
                        _ => null,
                    };
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

            var paletteRow = combinedRootWorkId.HasValue
                ? await browseReadService.GetAssetPaletteAsync(combinedRootWorkId.Value, ct)
                : null;
            var palette = ResolvePalette(rootCanonicals, paletteRow);
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
        group.MapGet("/content-groups", async (
            ICollectionRepository collectionRepo,
            ICollectionBrowseReadService browseReadService,
            CancellationToken ct) =>
        {
            var collections = await collectionRepo.GetContentGroupsAsync(ct);
            var primaryAssetIds = await browseReadService.GetPrimaryAssetIdsAsync(
                collections.SelectMany(collection => collection.Works).Select(work => work.Id),
                ct);

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
                    var primaryAssetId = primaryAssetIds.GetValueOrDefault(w.Id);
                    cover = BuildCoverStreamUrl(w, primaryAssetId);
                    background = BuildBackgroundStreamUrl(w, primaryAssetId);
                    banner = BuildBannerStreamUrl(w, primaryAssetId);
                    logo = BuildLogoStreamUrl(w, primaryAssetId);
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
                var previewItems = h.Works
                    .Select(work =>
                    {
                        var dto = WorkDto.FromDomain(work);
                        var primaryAssetId = primaryAssetIds.GetValueOrDefault(work.Id);
                        var coverUrl = BuildCoverStreamUrl(work, primaryAssetId);
                        var backgroundUrl = BuildBackgroundStreamUrl(work, primaryAssetId);
                        var bannerUrl = BuildBannerStreamUrl(work, primaryAssetId);
                        var imageUrl = string.Equals(primaryMediaType, "TV", StringComparison.OrdinalIgnoreCase)
                            ? backgroundUrl ?? bannerUrl ?? coverUrl
                            : coverUrl ?? backgroundUrl ?? bannerUrl;
                        var title = GetCanonical(dto, "title") ?? h.DisplayName ?? "Untitled";
                        var description = GetCanonical(dto, "short_description")
                            ?? GetCanonical(dto, "description");
                        var mediaKind = DisplayMediaRules.NormalizeDisplayKind(primaryMediaType);
                        return new
                        {
                            Work = work,
                            ImageUrl = imageUrl,
                            Shape = ResolveContentGroupPreviewShape(
                                primaryMediaType,
                                imageUrl,
                                coverUrl,
                                backgroundUrl,
                                bannerUrl,
                                ParseNullableInt(GetCanonical(dto, "cover_width_px")),
                                ParseNullableInt(GetCanonical(dto, "cover_height_px"))),
                            Title = title,
                            Description = description,
                            Facts = DisplayFactBuilder.Build(
                                mediaKind,
                                title,
                                year: GetCanonical(dto, "release_year") ?? GetCanonical(dto, "year"),
                                author: GetCanonical(dto, "author"),
                                artist: GetCanonical(dto, "artist"),
                                contentRating: GetCanonical(dto, "content_rating") ?? GetCanonical(dto, "certification"),
                                runtime: GetCanonical(dto, "runtime"),
                                duration: GetCanonical(dto, "duration") ?? GetCanonical(dto, "duration_seconds"),
                                pageCount: GetCanonical(dto, "page_count"),
                                starRating: GetCanonical(dto, "rating") ?? GetCanonical(dto, "star_rating")),
                            Position = GetCanonical(dto, "series_position")
                                ?? GetCanonical(dto, "episode_number")
                                ?? work.Ordinal?.ToString(CultureInfo.InvariantCulture),
                        };
                    })
                    .Where(item => !string.IsNullOrWhiteSpace(item.ImageUrl))
                    .OrderBy(item => item.Work.Ordinal is null)
                    .ThenBy(item => item.Work.Ordinal)
                    .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                    .Take(12)
                    .Select(item => new ContentGroupPreviewItemDto(
                        item.Work.Id,
                        item.Title,
                        item.ImageUrl!,
                        item.Shape,
                        item.Position,
                        item.Description,
                        item.Facts))
                    .ToList();

                return new ContentGroupDto
                {
                    CollectionId = h.Id,
                    DisplayName = h.DisplayName ?? $"Collection {h.Id.ToString("N")[..8]}",
                    WikidataQid = h.WikidataQid,
                    PrimaryMediaType = primaryMediaType,
                    WorkCount = h.Works.Count,
                    DistinctTitleCount = CountDistinctWorkTitles(h.Works),
                    PreviewItems = previewItems,
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
            ICollectionBrowseReadService browseReadService,
            CancellationToken ct) =>
        {
            var result = await browseReadService.GetSystemViewGroupsAsync(mediaType, groupField, ct);
            var normalizedGroups = NormalizeSystemViewGroups(result, mediaType, groupField);
            return Results.Ok(normalizedGroups
                .OrderBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList());
        })
        .WithName("GetSystemViewGroups")
        .WithSummary("Resolves built-in browse views (By Show, By Artist, By Album) as dynamic content groups for the library container views.")
        .Produces<List<ContentGroupDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // ── Managed Collection endpoints (managed collections surface) ──────────────────────────────

        // GET /collections/managed — all non-Universe collections for the managed collections surface.
        group.MapGet("/managed", async (
            Guid? profileId,
            IProfileRepository profileRepo,
            CollectionCatalogReadService catalogReadService,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            return Results.Ok(await catalogReadService.GetManagedAsync(activeProfile, ct));
        })
        .WithName("GetManagedCollections")
        .WithSummary("List authored collections accessible to the active profile.")
        .Produces<List<ManagedCollectionDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // GET /collections/catalog — classified collection catalog for the Collections hub.
        group.MapGet("/catalog", async (
            Guid? profileId,
            IProfileRepository profileRepo,
            CollectionCatalogReadService catalogReadService,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var catalog = await catalogReadService.GetCatalogAsync(activeProfile, ct);
            return Results.Ok(catalog);
        })
        .WithName("GetCollectionCatalog")
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
            CollectionCatalogReadService catalogReadService,
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
                existingWorkIds = (await catalogReadService.GetDisplayWorkIdsAsync(
                        existingItems.Select(item => item.WorkId), ct))
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
            CollectionCatalogReadService catalogReadService,
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

            var collectionWorkId = await catalogReadService.ResolveMembershipWorkIdAsync(body.WorkId, ct);
            var existingItems = await collectionRepo.GetCollectionItemsAsync(id, 1000, ct);
            var existingDisplayWorkIds = await catalogReadService.GetDisplayWorkIdsAsync(existingItems.Select(item => item.WorkId), ct);
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
            ICollectionBrowseReadService browseReadService,
            ICollectionMediaLookupReadService mediaLookupReadService,
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
                var primaryAssetIds = await browseReadService.GetPrimaryAssetIdsAsync(works.Select(w => w.Id), ct);
                var items = works.Select(w =>
                {
                    var dto = WorkDto.FromDomain(w);
                    return new CollectionResolvedItemDto
                    {
                        EntityId = w.Id,
                        Title = GetCanonical(dto, "title") ?? $"Work {w.Id.ToString("N")[..8]}",
                        Creator = GetCanonical(dto, "author") ?? GetCanonical(dto, "director") ?? GetCanonical(dto, "artist"),
                        MediaType = w.MediaType.ToString(),
                        CoverUrl = BuildCoverStreamUrl(w, primaryAssetIds.GetValueOrDefault(w.Id)),
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

            var entityIds = browseReadService.EvaluateRules(
                predicates, collection.MatchMode, collection.SortField, collection.SortDirection, limit ?? 0);

            var resolved = await mediaLookupReadService.ResolveMetadataAsync(entityIds, ct);
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
            ICollectionBrowseReadService browseReadService,
            ICollectionMediaLookupReadService mediaLookupReadService,
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

            var entityIds = browseReadService.EvaluateRules(
                predicates, collection.MatchMode, collection.SortField, collection.SortDirection, limit ?? 200);

            var resolved = await mediaLookupReadService.ResolveMetadataAsync(entityIds, ct);
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
        group.MapPost("/preview", async (
            CollectionPreviewRequest body,
            ICollectionBrowseReadService browseReadService,
            ICollectionMediaLookupReadService mediaLookupReadService,
            CancellationToken ct) =>
        {
            if (body.Rules.Count == 0)
            {
                return Results.Ok(new { count = 0, items = new List<CollectionResolvedItemDto>() });
            }

            var entityIds = browseReadService.EvaluateRules(
                body.Rules, body.MatchMode, limit: body.Limit > 0 ? body.Limit : 20);

            var resolved = await mediaLookupReadService.ResolveMetadataAsync(entityIds, ct);
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
        group.MapGet("/field-values/{field}", async (
            string field,
            int? limit,
            ICollectionBrowseReadService browseReadService,
            CancellationToken ct) =>
        {
            var values = await browseReadService.GetFieldValuesAsync(field, limit ?? 50, ct);
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

    private static bool IsGeneratedSeriesCollection(Collection collection)
        => string.Equals(collection.CollectionType, "Universe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(collection.CollectionType, "Series", StringComparison.OrdinalIgnoreCase)
            || string.Equals(collection.CollectionType, "ContentGroup", StringComparison.OrdinalIgnoreCase);

    private sealed class CollectionArtworkItemRow
    {
        public Guid WorkId { get; init; }
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

    private static string? BuildCoverStreamUrl(Work? w, Guid? assetId = null)
    {
        return BuildArtworkStreamUrl(
            w,
            "cover",
            MetadataFieldConstants.CoverState,
            assetId,
            MetadataFieldConstants.CoverUrl,
            MetadataFieldConstants.Cover);
    }

    private static string ResolveContentGroupPreviewShape(
        string primaryMediaType,
        string? imageUrl,
        string? coverUrl,
        string? backgroundUrl,
        string? bannerUrl,
        int? coverWidth,
        int? coverHeight)
    {
        if (string.Equals(primaryMediaType, "Music", StringComparison.OrdinalIgnoreCase))
        {
            return "square";
        }

        if (string.Equals(imageUrl, backgroundUrl, StringComparison.OrdinalIgnoreCase)
            || string.Equals(imageUrl, bannerUrl, StringComparison.OrdinalIgnoreCase))
        {
            return "wide";
        }

        if (string.Equals(imageUrl, coverUrl, StringComparison.OrdinalIgnoreCase)
            && coverWidth is > 0
            && coverHeight is > 0)
        {
            var ratio = coverWidth.Value / (double)coverHeight.Value;
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

    private static string? BuildBackgroundStreamUrl(Work? w, Guid? assetId = null)
    {
        return BuildArtworkStreamUrl(
            w,
            "background",
            "background_state",
            assetId,
            "background",
            "background_url");
    }

    private static string? BuildBannerStreamUrl(Work? w, Guid? assetId = null)
    {
        return BuildArtworkStreamUrl(
            w,
            "banner",
            "banner_state",
            assetId,
            "banner",
            "banner_url");
    }

    private static string? BuildLogoStreamUrl(Work? w, Guid? assetId = null)
    {
        return BuildArtworkStreamUrl(
            w,
            "logo",
            "logo_state",
            assetId,
            "logo",
            "logo_url");
    }

    private static string? BuildArtworkStreamUrl(
        Work? work,
        string streamKind,
        string stateKey,
        Guid? fallbackAssetId,
        params string[] valueKeys)
    {
        if (work is null)
        {
            return null;
        }

        var value = FirstCanonicalValue(work, valueKeys);
        var state = FirstCanonicalValue(work, stateKey);
        var assetId = fallbackAssetId.GetValueOrDefault();
        if (assetId == Guid.Empty)
        {
            assetId = FirstOwnedAssetId(work);
        }

        return assetId != Guid.Empty
            ? DisplayArtworkUrlResolver.Resolve(value, assetId, streamKind, state)
            : SuppressExternalProviderArtworkUrl(value);
    }

    private static Guid FirstOwnedAssetId(Work work) =>
        work.Editions
            .SelectMany(edition => edition.MediaAssets)
            .Select(asset => asset.Id)
            .FirstOrDefault(id => id != Guid.Empty);

    private static string? FirstCanonicalValue(Work work, params string[] keys) =>
        work.CanonicalValues
            .FirstOrDefault(c => keys.Any(key => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)))
            ?.Value;

    private static string? SuppressExternalProviderArtworkUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            ? null
            : value;
    }

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
        IReadOnlyList<CanonicalValue> canonicalValues,
        CollectionPaletteReadModel? storedPalette)
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

        if (storedPalette is not null
            && (string.IsNullOrWhiteSpace(primary)
                || string.IsNullOrWhiteSpace(secondary)
                || string.IsNullOrWhiteSpace(accent)))
        {
            primary ??= storedPalette.PrimaryHex;
            secondary ??= storedPalette.SecondaryHex;
            accent ??= storedPalette.AccentHex;
            AddColor(colors, storedPalette.PrimaryHex);
            AddColor(colors, storedPalette.SecondaryHex);
            AddColor(colors, storedPalette.AccentHex);
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
                    CollectionId = SystemViewGroupIdentity.CreateId(preferred, mediaType, groupField),
                    RootWorkId = preferred.RootWorkId,
                    DisplayName = preferred.DisplayName.Trim(),
                    WikidataQid = preferred.WikidataQid,
                    PrimaryMediaType = preferred.PrimaryMediaType,
                    WorkCount = group.Items.Max(item => item.WorkCount),
                    DistinctTitleCount = group.Items
                        .Where(item => item.DistinctTitleCount.HasValue)
                        .Select(item => item.DistinctTitleCount!.Value)
                        .DefaultIfEmpty(group.Items.Max(item => item.WorkCount))
                        .Max(),
                    PreviewItems = group.Items
                        .SelectMany(item => item.PreviewItems)
                        .DistinctBy(item => item.WorkId)
                        .Take(4)
                        .ToList(),
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

    private static string NormalizeSystemViewIdentity(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "(blank)"
            : value.Trim().ToLowerInvariant();

    private static int ScoreSystemViewGroup(ContentGroupDto group)
    {
        var score = 0;
        score += string.IsNullOrWhiteSpace(group.CoverUrl) ? 0 : 8;
        score += string.IsNullOrWhiteSpace(group.ArtistPhotoUrl) ? 0 : 8;
        score += string.IsNullOrWhiteSpace(group.Description) ? 0 : 4;
        score += string.IsNullOrWhiteSpace(group.Creator) ? 0 : 2;
        return score + group.WorkCount;
    }

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

    private sealed record CollectionDisplayWorkRow(Guid WorkId);

    private sealed record CollectionCatalogAggregation(string Key, string? Label);

    private sealed record CollectionManagementCatalogCandidate(
        Collection Collection,
        CollectionCatalogClassification Classification,
        CollectionCatalogAggregation? Grouping,
        IReadOnlyList<Guid> WorkIds,
        int ItemCount,
        CollectionMediaCounts MediaCounts);
}
