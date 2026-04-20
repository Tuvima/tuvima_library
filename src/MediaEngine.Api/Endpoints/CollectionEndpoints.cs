using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using Dapper;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
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

        group.MapGet("/", async (
            ICollectionRepository collectionRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var collections = await collectionRepo.GetAllAsync(ct);

            // Collect work IDs that have at least one non-staging media asset
            var libraryWorkIds = new HashSet<Guid>();
            using (var conn = db.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT DISTINCT e.work_id
                    FROM editions e
                    INNER JOIN media_assets ma ON ma.edition_id = e.id
                    WHERE ma.file_path_root NOT LIKE '%/.data/staging/%'
                      AND ma.file_path_root NOT LIKE '%\.data\staging\%'
                      AND ma.file_path_root NOT LIKE '%/.data\staging/%'
                    """;
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (Guid.TryParse(reader.GetString(0), out var wid))
                        libraryWorkIds.Add(wid);
                }
            }

            // Filter collections: only include works that are in the library (not staging)
            var filtered = new List<CollectionDto>();
            foreach (var collection in collections)
            {
                var libraryWorks = collection.Works.Where(w => libraryWorkIds.Contains(w.Id)).ToList();
                if (libraryWorks.Count == 0) continue;

                var filteredCollection = new Collection
                {
                    Id             = collection.Id,
                    UniverseId     = collection.UniverseId,
                    DisplayName    = collection.DisplayName,
                    CreatedAt      = collection.CreatedAt,
                    UniverseStatus = collection.UniverseStatus,
                    ParentCollectionId    = collection.ParentCollectionId,
                    WikidataQid    = collection.WikidataQid,
                };
                foreach (var w in libraryWorks)         filteredCollection.Works.Add(w);
                foreach (var r in collection.Relationships)    filteredCollection.Relationships.Add(r);

                filtered.Add(CollectionDto.FromDomain(filteredCollection));
            }

            return Results.Ok(filtered);
        })
        .WithName("GetAllCollections")
        .WithSummary("List all media collections with their works and canonical metadata values.")
        .Produces<List<CollectionDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapGet("/search", async (
            string? q,
            ICollectionRepository collectionRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Results.Ok(Array.Empty<SearchResultDto>());

            var query = q.Trim();
            var collections  = await collectionRepo.GetAllAsync(ct);

            // Collect work IDs that have at least one non-staging media asset
            var libraryWorkIds = new HashSet<Guid>();
            using (var conn = db.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT DISTINCT e.work_id
                    FROM editions e
                    INNER JOIN media_assets ma ON ma.edition_id = e.id
                    WHERE ma.file_path_root NOT LIKE '%/.data/staging/%'
                      AND ma.file_path_root NOT LIKE '%\.data\staging\%'
                      AND ma.file_path_root NOT LIKE '%/.data\staging/%'
                    """;
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (Guid.TryParse(reader.GetString(0), out var wid))
                        libraryWorkIds.Add(wid);
                }
            }

            // Build DTOs for library-only works, keeping a cover URL map from domain objects.
            var filtered = new List<CollectionDto>();
            var coverMap = new Dictionary<Guid, string>();
            foreach (var collection in collections)
            {
                var libraryWorks = collection.Works.Where(w => libraryWorkIds.Contains(w.Id)).ToList();
                if (libraryWorks.Count == 0) continue;

                var filteredCollection = new Collection
                {
                    Id             = collection.Id,
                    UniverseId     = collection.UniverseId,
                    DisplayName    = collection.DisplayName,
                    CreatedAt      = collection.CreatedAt,
                    UniverseStatus = collection.UniverseStatus,
                    ParentCollectionId    = collection.ParentCollectionId,
                    WikidataQid    = collection.WikidataQid,
                };
                foreach (var w in libraryWorks)
                {
                    filteredCollection.Works.Add(w);
                    var url = BuildCoverStreamUrl(w);
                    if (url is not null) coverMap[w.Id] = url;
                }
                foreach (var r in collection.Relationships)    filteredCollection.Relationships.Add(r);

                filtered.Add(CollectionDto.FromDomain(filteredCollection));
            }

            var dtos = filtered;

            var results = dtos
                .SelectMany(collection => collection.Works
                    .Where(w => WorkMatchesQuery(w, query))
                    .Select(w => new SearchResultDto
                    {
                        WorkId          = w.Id,
                        CollectionId           = collection.Id,
                        Title           = GetCanonical(w, "title")   ?? $"Work {w.Id}",
                        Author          = GetCanonical(w, "author"),
                        MediaType       = w.MediaType,
                        CollectionDisplayName  = GetCanonical(collection.Works.FirstOrDefault()!, "title")
                                          ?? collection.Id.ToString("N")[..8],
                        CoverUrl        = coverMap.GetValueOrDefault(w.Id),
                    }))
                .Take(20)
                .ToList();

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
                        Id             = h.Id,
                        UniverseId     = h.UniverseId,
                        DisplayName    = h.DisplayName,
                        Description    = h.Description,
                        WikidataQid    = h.WikidataQid,
                        ParentCollectionId    = null,
                        UniverseStatus = h.UniverseStatus,
                        CreatedAt      = h.CreatedAt,
                        ChildCollectionCount  = children.Count,
                        MediaTypes     = string.Join(", ", mediaTypes),
                        TotalWorks     = children.Sum(c => c.Works.Count),
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
                id             = h.Id,
                displayName    = h.DisplayName,
                parentCollectionId    = h.ParentCollectionId,
                createdAt      = h.CreatedAt,
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
                return Results.NotFound();

            if (!collection.ParentCollectionId.HasValue)
                return Results.Ok(new { parentCollection = (object?)null });

            var parent = await collectionRepo.GetByIdAsync(collection.ParentCollectionId.Value, ct);
            if (parent is null)
                return Results.Ok(new { parentCollection = (object?)null });

            return Results.Ok(new
            {
                parentCollection = new
                {
                    id             = parent.Id,
                    displayName    = parent.DisplayName,
                    createdAt      = parent.CreatedAt,
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
            var dtos    = allCollections.Select(CollectionDto.FromDomain).ToList();

            var target = dtos.FirstOrDefault(h => h.Id == id);
            if (target is null)
                return Results.NotFound($"Collection '{id}' not found.");

            int take = limit is > 0 ? limit.Value : 20;

            var targetSeries = GetCanonical(target.Works.FirstOrDefault(), "series");
            var targetAuthor = GetCanonical(target.Works.FirstOrDefault(), "author");
            var targetGenre  = GetCanonical(target.Works.FirstOrDefault(), "genre");

            var result   = new List<CollectionDto>();
            var seen     = new HashSet<Guid> { id };
            string reason = "explore";
            string title  = "Explore Your Library";

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
                    reason = "series";
                    title  = $"More in {targetSeries}";
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
                    if (result.Count == 0) { reason = "author"; title = $"More by {targetAuthor}"; }
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
                    if (result.Count == 0) { reason = "genre"; title = $"More {targetGenreFirst}"; }
                    result.AddRange(matches);
                    matches.ForEach(h => seen.Add(h.Id));
                }
            }

            // Stage 4: random fill
            if (result.Count < take)
            {
                var fill = dtos
                    .Where(h => !seen.Contains(h.Id))
                    .OrderBy(_ => Guid.NewGuid())
                    .Take(take - result.Count)
                    .ToList();
                result.AddRange(fill);
            }

            return Results.Ok(new RelatedCollectionsResponse
            {
                SectionTitle = title,
                Reason       = reason,
                Collections         = result,
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
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var collection = await collectionRepo.GetCollectionWithWorksAsync(collectionId, ct);
            if (collection is null)
                return Results.NotFound();

            // Determine primary media type from the works.
            var primaryMediaType = collection.Works
                .GroupBy(w => w.MediaType.ToString())
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key;

            bool isTv = string.Equals(primaryMediaType, "TV", StringComparison.OrdinalIgnoreCase);

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
                idParam.Value = collection.Works[0].Id.ToString();
                rootCmd.Parameters.Add(idParam);

                var rootIdObj = await rootCmd.ExecuteScalarAsync(ct);
                if (rootIdObj is string rootIdStr && Guid.TryParse(rootIdStr, out var rid))
                {
                    rootParentWorkId = rid;
                    parentCvs = await canonicalRepo.GetByEntityAsync(rid, ct);
                }
            }

            string? ParentCv(string key) =>
                parentCvs.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

            var rootWorkQid = collection.WikidataQid ?? ParentCv(BridgeIdKeys.WikidataQid);

            // Build per-work DTOs.
            var workDtos = collection.Works
                .OrderBy(w => w.Ordinal ?? int.MaxValue)
                .ThenBy(w => w.Id)
                .Select(w =>
                {
                    var workDto = WorkDto.FromDomain(w);
                    string? title       = (isTv ? GetCanonical(workDto, "episode_title") : null)
                                         ?? GetCanonical(workDto, "title")
                                         ?? $"Work {w.Id.ToString("N")[..8]}";
                    string? year        = GetCanonical(workDto, "release_year")
                                         ?? GetCanonical(workDto, "year");
                    string? duration    = GetCanonical(workDto, "duration")
                                         ?? GetCanonical(workDto, "runtime");
                    string? coverUrl    = BuildCoverStreamUrl(w);
                    string? backgroundUrl = BuildBackgroundStreamUrl(w);
                    string? bannerUrl   = BuildBannerStreamUrl(w);
                    string? heroUrl     = BuildHeroStreamUrl(w);
                    string? season      = GetCanonical(workDto, "season_number");
                    string? episode     = GetCanonical(workDto, "episode_number");
                    string? trackNumber = GetCanonical(workDto, "track_number");
                    string? description = GetCanonical(workDto, "description");
                    string? director    = GetCanonical(workDto, "director");
                    string? writer      = GetCanonical(workDto, "writer");
                    string? releaseDate = NormalizeReleaseDate(
                        GetCanonical(workDto, "release_date")
                        ?? GetCanonical(workDto, "date")
                        ?? GetCanonical(workDto, "year"));

                    // Derive a display status from wikidata_status / match_level.
                    string status = w.WikidataStatus switch
                    {
                        "confirmed" => "Verified",
                        "skipped"   => "Unlinked",
                        _           => "Provisional",
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
                        WorkId        = w.Id,
                        Title         = title,
                        Ordinal = w.Ordinal,
                        Year          = year,
                        Duration      = duration,
                        CoverUrl      = coverUrl,
                        BackgroundUrl = backgroundUrl,
                        BannerUrl     = bannerUrl,
                        HeroUrl       = heroUrl,
                        WikidataQid   = w.WikidataQid,
                        Season        = season,
                        Episode       = episode,
                        TrackNumber   = trackNumber,
                        Status        = status,
                        Description   = description,
                        Director      = director,
                        Writer        = writer,
                        ReleaseDate   = releaseDate,
                        PlaybackSummary = BuildPlaybackSummaryFromWork(workDto),
                        Stage1        = stage1,
                        Stage2        = stage2,
                        Stage3        = stage3,
                    };
                })
                .ToList();

            // Collection-level header canonical values come from the topmost Work row.
            // Phase 4 — parent-scoped fields (author, director, artist, genre, cover,
            // network) live on the root parent Work, not on individual child works.
            string? collectionCreator  = ParentCv("author") ?? ParentCv("director") ?? ParentCv("artist");
            string? collectionDirector = ParentCv("director");
            string? collectionWriter   = ParentCv("writer");
            string? collectionGenre    = ParentCv("genre");
            string? collectionNetwork  = isTv ? ParentCv("network") : null;
            string? collectionDescription = ParentCv("description");
            string? collectionTagline = ParentCv("tagline");
            string? collectionReleaseDate = NormalizeReleaseDate(
                ParentCv("release_date")
                ?? ParentCv("date")
                ?? ParentCv("year"));

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
                widParam.Value = rootParentWorkId.Value.ToString();
                coverCmd.Parameters.Add(widParam);
                var rootAssetObj = await coverCmd.ExecuteScalarAsync(ct);
                if (rootAssetObj is string rootAssetStr)
                {
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
            List<CollectionGroupWorkDto>   flatWorks = [];

            bool isMusic = string.Equals(primaryMediaType, "Music", StringComparison.OrdinalIgnoreCase);

            if (isTv)
            {
                seasons = workDtos
                    .GroupBy(w => int.TryParse(w.Season, out var sn) ? sn : 0)
                    .OrderBy(g => g.Key)
                    .Select(g => new CollectionGroupSeasonDto
                    {
                        SeasonNumber = g.Key,
                        SeasonLabel  = $"Season {g.Key}",
                        Episodes     = g.OrderBy(e => int.TryParse(e.Episode, out var en) ? en : e.Ordinal ?? int.MaxValue).ToList(),
                    })
                    .ToList();
            }
            else if (isMusic && workDtos.Count > 1)
            {
                // Music: tracks are already within one album collection, show as flat list with track ordering
                flatWorks = workDtos
                    .OrderBy(w => int.TryParse(w.TrackNumber, out var tn) ? tn : w.Ordinal ?? int.MaxValue)
                    .ToList();
            }
            else
            {
                flatWorks = workDtos;
            }

            // Top billed cast for TV and Movies — read the Parent-scoped
            // cast_member array (P161) and resolve each entry to a Person
            // record so the Dashboard can open the people drawer on click.
            // Capped at 10 entries to match the design.
            var topCast = new List<CollectionGroupPersonDto>();
            bool hasCast = (isTv || string.Equals(primaryMediaType, "Movies", StringComparison.OrdinalIgnoreCase))
                           && rootParentWorkId.HasValue;
            if (hasCast)
            {
                topCast = await BuildCharacterAwareCastAsync(
                    rootWorkQid,
                    rootParentWorkId!.Value,
                    canonicalArrayRepo,
                    personRepo,
                    db,
                    ct);
            }

            var response = new CollectionGroupDetailDto
            {
                CollectionId            = collection.Id,
                DisplayName      = collection.DisplayName ?? $"Collection {collection.Id.ToString("N")[..8]}",
                RootWorkId       = rootParentWorkId,
                WikidataQid      = collection.WikidataQid,
                PrimaryMediaType = primaryMediaType,
                CoverUrl         = collectionCover,
                BackgroundUrl    = collectionBackground,
                BannerUrl        = collectionBanner,
                Description      = collectionDescription,
                Tagline          = collectionTagline,
                Creator          = collectionCreator,
                Director         = collectionDirector,
                Writer           = collectionWriter,
                ReleaseDate      = collectionReleaseDate,
                YearRange        = yearRange,
                Genre            = collectionGenre,
                Network          = collectionNetwork,
                SeasonCount      = isTv ? seasons.Count : null,
                TopCast          = topCast,
                TotalItems       = collection.Works.Count,
                Seasons          = seasons,
                Works            = flatWorks,
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
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(collectionIdsParam))
                return Results.BadRequest("collection_ids parameter is required");

            var collectionIds = collectionIdsParam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Guid.TryParse(s, out var id) ? id : (Guid?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            if (collectionIds.Count == 0)
                return Results.BadRequest("No valid collection IDs provided");

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
                if (collection is null) continue;

                // Build owned track DTOs from collection.Works.
                var ownedTracks = collection.Works
                    .OrderBy(w => w.Ordinal ?? int.MaxValue)
                    .ThenBy(w => w.Id)
                    .Select(w =>
                    {
                        var wDto = WorkDto.FromDomain(w);
                        return new CollectionGroupWorkDto
                        {
                            WorkId        = w.Id,
                            Title         = GetCanonical(wDto, "title") ?? $"Track {w.Id.ToString("N")[..8]}",
                            Ordinal = w.Ordinal,
                            Year          = GetCanonical(wDto, "release_year") ?? GetCanonical(wDto, "year"),
                            Duration      = GetCanonical(wDto, "duration") ?? GetCanonical(wDto, "runtime"),
                            CoverUrl      = BuildCoverStreamUrl(w),
                            WikidataQid   = w.WikidataQid,
                            TrackNumber   = GetCanonical(wDto, "track_number"),
                            Status        = w.WikidataStatus switch
                            {
                                "confirmed" => "Verified",
                                "skipped"   => "Unlinked",
                                _           => "Provisional",
                            },
                            IsOwned       = true,
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
                        if (!string.IsNullOrWhiteSpace(childJson)) break;
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
                    SeasonLabel  = collection.DisplayName ?? $"Album {albumIndex + 1}",
                    CoverUrl     = albumCover,
                    AlbumCollectionId   = collection.Id,
                    Year         = albumYear,
                    Episodes     = mergedTracks,
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
                            artistPhotoUrl = $"/persons/{person.Id}/headshot";
                    }
                }
                catch { /* best-effort lookup */ }
            }

            var response = new CollectionGroupDetailDto
            {
                CollectionId            = collectionIds[0],
                DisplayName      = combinedCreator ?? "Unknown Artist",
                PrimaryMediaType = "Music",
                CoverUrl         = null, // artist view header uses ArtistPhotoUrl, not an album cover
                Creator          = combinedCreator,
                YearRange        = yearRange,
                Genre            = combinedGenre,
                TotalItems       = totalItems,
                Seasons          = allSeasons,
                Works            = [],
                ArtistPhotoUrl   = artistPhotoUrl,
                ArtistPersonId   = artistPersonId,
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
                return Results.BadRequest("artistName parameter is required");

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
                        MAX(CASE WHEN cv.key = 'title' THEN cv.value END) AS title,
                        MAX(CASE WHEN cv.key = 'album' THEN cv.value END) AS album,
                        MAX(CASE WHEN cv.key = 'artist' THEN cv.value END) AS artist,
                        MAX(CASE WHEN cv.key = 'track_number' THEN cv.value END) AS track_number,
                        MAX(CASE WHEN cv.key = 'release_year' THEN cv.value END) AS release_year,
                        MAX(CASE WHEN cv.key = 'year' THEN cv.value END) AS year_val,
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
                var workId = reader.GetGuid(reader.GetOrdinal("work_id"));
                var title = reader.IsDBNull(reader.GetOrdinal("title")) ? null : reader.GetString(reader.GetOrdinal("title"));
                var album = reader.IsDBNull(reader.GetOrdinal("album")) ? null : reader.GetString(reader.GetOrdinal("album"));
                var trackNum = reader.IsDBNull(reader.GetOrdinal("track_number")) ? null : reader.GetString(reader.GetOrdinal("track_number"));
                var releaseYear = reader.IsDBNull(reader.GetOrdinal("release_year")) ? null : reader.GetString(reader.GetOrdinal("release_year"));
                var yearVal = reader.IsDBNull(reader.GetOrdinal("year_val")) ? null : reader.GetString(reader.GetOrdinal("year_val"));
                var duration = reader.IsDBNull(reader.GetOrdinal("duration")) ? null : reader.GetString(reader.GetOrdinal("duration"));
                var runtime = reader.IsDBNull(reader.GetOrdinal("runtime")) ? null : reader.GetString(reader.GetOrdinal("runtime"));
                var cover = reader.IsDBNull(reader.GetOrdinal("cover")) ? null : reader.GetString(reader.GetOrdinal("cover"));
                var genre = reader.IsDBNull(reader.GetOrdinal("genre")) ? null : reader.GetString(reader.GetOrdinal("genre"));
                var artistVal = reader.IsDBNull(reader.GetOrdinal("artist")) ? null : reader.GetString(reader.GetOrdinal("artist"));
                var childJson = reader.IsDBNull(reader.GetOrdinal("child_entities_json")) ? null : reader.GetString(reader.GetOrdinal("child_entities_json"));

                combinedCreator ??= artistVal;
                combinedGenre ??= genre;

                var year = releaseYear ?? yearVal;
                if (!string.IsNullOrWhiteSpace(year)) allYears.Add(year);

                var albumKey = album ?? "Unknown Album";
                if (!albumMap.TryGetValue(albumKey, out var tracks))
                {
                    tracks = [];
                    albumMap[albumKey] = tracks;
                }
                if (!albumCovers.ContainsKey(albumKey))
                    albumCovers[albumKey] = cover;
                if (!albumYears.ContainsKey(albumKey) || string.IsNullOrWhiteSpace(albumYears[albumKey]))
                    albumYears[albumKey] = year;
                if (!albumChildJson.ContainsKey(albumKey) || string.IsNullOrWhiteSpace(albumChildJson[albumKey]))
                    albumChildJson[albumKey] = childJson;

                tracks.Add(new CollectionGroupWorkDto
                {
                    WorkId      = workId,
                    Title       = title ?? $"Track {workId.ToString("N")[..8]}",
                    Year        = year,
                    Duration    = duration ?? runtime,
                    CoverUrl    = cover,
                    TrackNumber = trackNum,
                    Status      = "Provisional",
                    IsOwned     = true,
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
                    SeasonLabel  = albumKey,
                    CoverUrl     = albumCover,
                    Year         = albumYear,
                    AlbumCollectionId   = null, // by-name lookup has no concrete collection id
                    Episodes     = merged,
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
                            artistPhotoUrl = $"/persons/{person.Id}/headshot";
                    }
                }
                catch { /* best-effort lookup */ }
            }

            var response = new CollectionGroupDetailDto
            {
                CollectionId            = Guid.Empty,
                DisplayName      = artistName,
                PrimaryMediaType = "Music",
                CoverUrl         = null,
                Creator          = combinedCreator,
                YearRange        = yearRange,
                Genre            = combinedGenre,
                TotalItems       = totalItems,
                Seasons          = seasons,
                Works            = [],
                ArtistPhotoUrl   = artistPhotoUrl,
                ArtistPersonId   = artistPersonId,
            };

            return Results.Ok(response);
        })
        .WithName("GetArtistDetailByName")
        .WithSummary("Returns artist drill-down detail by artist name, querying directly from canonical values. Used when system-view collections are active and ContentGroup collections are unavailable.")
        .Produces<CollectionGroupDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAnyRole();

        // GET /collections/system-view-detail?groupField=show_name&groupValue=Breaking Bad&mediaType=TV
        // Generic system-view drill-down that works for any group field (show_name, series, album, artist).
        // Returns a CollectionGroupDetailDto with seasons/sections grouped by a secondary field when available.
        group.MapGet("/system-view-detail", async (
            [Microsoft.AspNetCore.Mvc.FromQuery(Name = "groupField")] string? groupField,
            [Microsoft.AspNetCore.Mvc.FromQuery(Name = "groupValue")] string? groupValue,
            [Microsoft.AspNetCore.Mvc.FromQuery(Name = "mediaType")] string? mediaType,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(groupField) || string.IsNullOrWhiteSpace(groupValue))
                return Results.BadRequest("groupField and groupValue parameters are required");

            // Determine the secondary grouping field and sort fields based on the primary group
            var (secondaryGroup, sortFields) = groupField.ToLowerInvariant() switch
            {
                "show_name"  => ("season_number", "season_number, episode_number, title"),
                "artist"     => ("album", "album, CAST(track_number AS INTEGER), title"),
                "series"     => ((string?)null, "CAST(series_index AS INTEGER), title"),
                _            => ((string?)null, "title"),
            };

            // Label for secondary groups
            var secondaryLabelPrefix = groupField.ToLowerInvariant() switch
            {
                "show_name" => "Season ",
                "artist"    => (string?)null, // use album name directly
                _           => null,
            };

            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();

            // Build the query: find all works matching groupField=groupValue, optionally filtered by media_type
            var mediaTypeFilter = !string.IsNullOrWhiteSpace(mediaType)
                ? "INNER JOIN works w ON w.id = e.work_id AND w.media_type = @MediaType"
                : "INNER JOIN works w ON w.id = e.work_id";

            cmd.CommandText = $"""
                WITH matched_works AS (
                    SELECT DISTINCT e.work_id
                    FROM canonical_values cv
                    INNER JOIN media_assets ma ON ma.id = cv.entity_id
                    INNER JOIN editions e ON e.id = ma.edition_id
                    {mediaTypeFilter}
                    WHERE cv.key = @GroupField AND cv.value = @GroupValue COLLATE NOCASE
                ),
                work_data AS (
                    SELECT
                        mw.work_id,
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
                        MAX(CASE WHEN cv.key = 'release_year'        THEN cv.value END) AS release_year,
                        MAX(CASE WHEN cv.key = 'year'                THEN cv.value END) AS year_val,
                        MAX(CASE WHEN cv.key = 'duration'            THEN cv.value END) AS duration,
                        MAX(CASE WHEN cv.key = 'runtime'             THEN cv.value END) AS runtime,
                        '/stream/' || MIN(ma.id) || '/cover' AS cover,
                        MAX(CASE WHEN cv.key = 'genre'               THEN cv.value END) AS genre,
                        MAX(CASE WHEN cv.key = 'network'             THEN cv.value END) AS network,
                        MAX(CASE WHEN cv.key = 'child_entities_json' THEN cv.value END) AS child_entities_json
                    FROM matched_works mw
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
            string? combinedGenre = null;
            string? combinedNetwork = null;
            var allYears = new List<string>();
            int totalItems = 0;
            // Collect child_entities_json from any owned work that carries it.
            string? collectedChildJson = null;

            while (reader.Read())
            {
                var workId = reader.GetGuid(reader.GetOrdinal("work_id"));
                var title = reader.IsDBNull(reader.GetOrdinal("title")) ? null : reader.GetString(reader.GetOrdinal("title"));
                var episodeTitle = reader.IsDBNull(reader.GetOrdinal("episode_title")) ? null : reader.GetString(reader.GetOrdinal("episode_title"));
                var cover = reader.IsDBNull(reader.GetOrdinal("cover")) ? null : reader.GetString(reader.GetOrdinal("cover"));
                var genre = reader.IsDBNull(reader.GetOrdinal("genre")) ? null : reader.GetString(reader.GetOrdinal("genre"));
                var duration = reader.IsDBNull(reader.GetOrdinal("duration")) ? null : reader.GetString(reader.GetOrdinal("duration"));
                var runtime = reader.IsDBNull(reader.GetOrdinal("runtime")) ? null : reader.GetString(reader.GetOrdinal("runtime"));
                var releaseYear = reader.IsDBNull(reader.GetOrdinal("release_year")) ? null : reader.GetString(reader.GetOrdinal("release_year"));
                var yearVal = reader.IsDBNull(reader.GetOrdinal("year_val")) ? null : reader.GetString(reader.GetOrdinal("year_val"));
                var episodeNum = reader.IsDBNull(reader.GetOrdinal("episode_number")) ? null : reader.GetString(reader.GetOrdinal("episode_number"));
                var trackNum = reader.IsDBNull(reader.GetOrdinal("track_number")) ? null : reader.GetString(reader.GetOrdinal("track_number"));
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
                    creator ??= networkVal ?? directorVal ?? artistVal;
                else
                    creator ??= directorVal ?? artistVal;

                combinedCreator ??= creator;
                combinedCover ??= cover;
                combinedGenre ??= genre;

                combinedNetwork ??= networkVal;

                var year = releaseYear ?? yearVal;
                if (!string.IsNullOrWhiteSpace(year)) allYears.Add(year);

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
                    WorkId       = workId,
                    Title        = episodeTitle ?? title ?? $"Item {workId.ToString("N")[..8]}",
                    Year         = year,
                    Duration     = duration ?? runtime,
                    CoverUrl     = cover,
                    Episode      = episodeNum,
                    TrackNumber  = trackNum,
                    Ordinal      = int.TryParse(seqIndex, out var si) ? si : null,
                    Status       = "Provisional",
                    IsOwned      = true,
                });

                totalItems++;
            }

            // M-083: Merge unowned items from child_entities_json.
            // For TV shows the JSON has an "episodes" array grouped by season;
            // for music it has "tracks"; for comics "issues". We use the same
            // child-entity parsing used by MergeUnownedMusicTracks.
            if (!string.IsNullOrWhiteSpace(collectedChildJson))
            {
                MergeUnownedChildEntities(
                    sectionMap,
                    collectedChildJson,
                    groupField,
                    secondaryGroup,
                    combinedCover);
            }

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
                        SeasonLabel  = secondaryLabelPrefix is not null
                            ? $"{secondaryLabelPrefix}{kvp.Key}"
                            : kvp.Key,
                        Episodes     = kvp.Value,
                    })
                    .ToList();
                flatWorks = [];
            }
            else
            {
                seasons = [];
                flatWorks = sectionMap.Values.SelectMany(v => v).ToList();
            }

            var response = new CollectionGroupDetailDto
            {
                CollectionId            = Guid.Empty,
                DisplayName      = groupValue,
                PrimaryMediaType = mediaType ?? "Unknown",
                CoverUrl         = combinedCover,
                Creator          = combinedCreator,
                YearRange        = yearRange,
                Genre            = combinedGenre,
                Network          = combinedNetwork,
                TotalItems       = totalItems,
                Seasons          = seasons,
                Works            = flatWorks,
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
                    if (cover is not null || background is not null || banner is not null || logo is not null) break;
                }

                // Creator from first work.
                var firstDto = h.Works.Count > 0 ? WorkDto.FromDomain(h.Works[0]) : null;
                string? creator = GetCanonical(firstDto, "author")
                                  ?? GetCanonical(firstDto, "director")
                                  ?? GetCanonical(firstDto, "artist");
                string? releaseDate = NormalizeReleaseDate(
                    GetCanonical(firstDto, "release_date")
                    ?? GetCanonical(firstDto, "date")
                    ?? GetCanonical(firstDto, "year"));

                return new ContentGroupDto
                {
                    CollectionId            = h.Id,
                    DisplayName      = h.DisplayName ?? $"Collection {h.Id.ToString("N")[..8]}",
                    WikidataQid      = h.WikidataQid,
                    PrimaryMediaType = primaryMediaType,
                    WorkCount        = h.Works.Count,
                    CoverUrl         = cover,
                    BackgroundUrl    = background,
                    BannerUrl        = banner,
                    LogoUrl          = logo,
                    Description      = h.Description ?? GetCanonical(firstDto, "description"),
                    Tagline          = GetCanonical(firstDto, "tagline"),
                    Creator          = creator,
                    Director         = GetCanonical(firstDto, "director"),
                    Writer           = GetCanonical(firstDto, "writer"),
                    ReleaseDate      = releaseDate,
                    UniverseStatus   = h.UniverseStatus,
                    CreatedAt        = h.CreatedAt,
                    Network          = GetCanonical(firstDto, "network"),
                    Year             = GetCanonical(firstDto, "release_year") ?? GetCanonical(firstDto, "year"),
                    SeasonCount      = string.Equals(primaryMediaType, "TV", StringComparison.OrdinalIgnoreCase)
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
        // Used by Vault container views (By Show, By Artist, By Album) that are driven by System collections
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
                if (predicates.Count == 0) continue;

                // Evaluate collection rules to get entity_ids
                var entityIds = evaluator.Evaluate(predicates, collection.MatchMode, collection.SortField, collection.SortDirection);

                log.LogInformation("[ByAlbum] Collection '{CollectionName}' (groupByField={GroupByField}) matched {WorkCount} works from CollectionRuleEvaluator",
                    collection.DisplayName, collection.GroupByField, entityIds.Count);

                if (entityIds.Count == 0) continue;

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
                    grouped AS (
                        SELECT
                            cv_group.value                          AS group_name,
                            COUNT(DISTINCT wa.work_id)              AS work_count,
                            MIN(wa.asset_id)                        AS first_asset_id,
                            MIN(wa.root_work_id)                    AS first_root_work_id,
                            -- Count distinct albums for artist grouping (track_count = work_count)
                            COUNT(DISTINCT wa.root_work_id)         AS album_count
                        FROM work_assets wa
                        INNER JOIN canonical_values cv_group
                          ON (cv_group.entity_id = wa.asset_id
                              OR cv_group.entity_id = wa.root_work_id)
                        WHERE cv_group.key = @GroupField
                        GROUP BY cv_group.value
                    )
                    SELECT
                        g.group_name,
                        g.work_count,
                        '/stream/' || g.first_asset_id || '/cover' AS cover_url,
                        '/stream/' || g.first_asset_id || '/background' AS background_url,
                        '/stream/' || g.first_asset_id || '/banner' AS banner_url,
                        '/stream/' || g.first_asset_id || '/logo' AS logo_url,
                        COALESCE(
                            (
                                SELECT cv_creator.value
                                FROM canonical_values cv_creator
                                WHERE cv_creator.entity_id = g.first_root_work_id
                                  AND cv_creator.key IN ('artist','author','director')
                                LIMIT 1
                            ),
                            (
                                SELECT cv_creator2.value
                                FROM canonical_values cv_creator2
                                WHERE cv_creator2.entity_id = g.first_asset_id
                                  AND cv_creator2.key IN ('artist','author','director')
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

                // Collect rows first so we can close the reader before doing async person lookups.
                var rows = new List<(string GroupName, int WorkCount, string? CoverUrl, string? BackgroundUrl, string? BannerUrl, string? LogoUrl, string? Creator, string? Network, string? Year, string? Description, string? Tagline, int? SeasonCount, int AlbumCount)>();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var groupName = reader.IsDBNull(0) ? null : reader.GetString(0);
                        if (string.IsNullOrWhiteSpace(groupName)) continue;
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
                            reader.IsDBNull(11) ? null : (int?)reader.GetInt32(11),
                            reader.IsDBNull(12) ? 0 : reader.GetInt32(12)
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
                                    artistPhotoUrl = $"/persons/{person.Id}/headshot";
                            }
                        }
                        catch { /* best-effort — missing photo is fine */ }
                    }

                    result.Add(new ContentGroupDto
                    {
                        CollectionId            = collection.Id,
                        DisplayName      = row.GroupName,
                        WikidataQid      = null,
                        PrimaryMediaType = primaryMediaType,
                        WorkCount        = row.WorkCount,
                        CoverUrl         = row.CoverUrl,
                        BackgroundUrl    = row.BackgroundUrl,
                        BannerUrl        = row.BannerUrl,
                        LogoUrl          = row.LogoUrl,
                        Description      = row.Description,
                        Tagline          = row.Tagline,
                        Creator          = row.Creator,
                        UniverseStatus   = "Complete",
                        CreatedAt        = collection.CreatedAt,
                        ArtistPhotoUrl   = artistPhotoUrl,
                        ArtistPersonId   = artistPersonId,
                        Network          = row.Network,
                        Year             = row.Year,
                        SeasonCount      = row.SeasonCount,
                        AlbumCount       = row.AlbumCount > 0 ? row.AlbumCount : null,
                    });
                }
            }

            log.LogInformation("[ByAlbum] Returning {Total} content groups for mediaType={MediaType} groupField={GroupField}",
                result.Count, mediaType ?? "(none)", groupField ?? "(none)");

            return Results.Ok(result.OrderBy(r => r.DisplayName).ToList());
        })
        .WithName("GetSystemViewGroups")
        .WithSummary("Resolves built-in browse views (By Show, By Artist, By Album) as dynamic content groups for the Vault container views.")
        .Produces<List<ContentGroupDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // ── Managed Collection endpoints (Vault Collections tab) ──────────────────────────────

        // GET /collections/managed — all non-Universe collections for the Vault Collections tab.
        group.MapGet("/managed", async (
            Guid? profileId,
            ICollectionRepository collectionRepo,
            IProfileRepository profileRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var collections = await collectionRepo.GetManagedCollectionsAsync(ct);
            var dtos = new List<ManagedCollectionDto>();
            foreach (var collection in collections.Where(collection => CollectionAccessPolicy.CanAccess(collection, activeProfile)))
            {
                var count = await GetManagedCollectionItemCountAsync(collection, collectionRepo, db, ct);
                dtos.Add(ManagedCollectionDto.FromDomain(collection, count, activeProfile));
            }

            return Results.Ok(dtos);
        })
        .WithName("GetManagedCollections")
        .WithSummary("List authored collections accessible to the active profile.")
        .Produces<List<ManagedCollectionDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // GET /collections/managed/counts — type → count for stats bar.
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
        group.MapGet("/{id:guid}/items", async (
            Guid id,
            int? limit,
            ICollectionRepository collectionRepo,
            IProfileRepository profileRepo,
            Guid? profileId,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var collection = await collectionRepo.GetByIdAsync(id, ct);
            if (collection is null) return Results.NotFound();
            if (!CollectionAccessPolicy.CanAccess(collection, activeProfile))
                return Results.Forbid();

            int take = limit is > 0 ? limit.Value : 20;
            var items = await collectionRepo.GetCollectionItemsAsync(id, take, ct);

            // Resolve work metadata from canonical_values
            var dtos = new List<CollectionItemDto>();
            if (items.Count > 0)
            {
                using var conn = db.CreateConnection();
                var visibleWorkPredicate = HomeVisibilitySql.VisibleWorkPredicate("w.id", "w.curator_state", "w.is_catalog_only");
                var visibleWorkIds = (await conn.QueryAsync<Guid>(
                    $"""
                    SELECT w.id
                    FROM works w
                    WHERE w.id IN @ids
                      AND {visibleWorkPredicate}
                    """,
                    new { ids = items.Select(item => item.WorkId.ToString()).ToArray() }))
                    .ToHashSet();

                foreach (var item in items)
                {
                    if (!visibleWorkIds.Contains(item.WorkId))
                        continue;

                    string? title = null, creator = null, mediaType = null, cover = null;
                    using var cmd = conn.CreateCommand();
                    var visibleAssetPredicate = HomeVisibilitySql.VisibleAssetPathPredicate("ma.file_path_root");
                    cmd.CommandText = $"""
                        SELECT cv.key, cv.value
                        FROM canonical_values cv
                        WHERE cv.entity_id = @WorkId
                          AND cv.key IN ('title', 'author')
                        UNION ALL
                        SELECT 'media_type', w.media_type
                        FROM works w WHERE w.id = @WorkId
                        UNION ALL
                        SELECT '_asset_id', MIN(ma.id)
                        FROM editions e
                        INNER JOIN media_assets ma ON ma.edition_id = e.id
                        WHERE e.work_id = @WorkId
                          AND {visibleAssetPredicate}
                        """;
                    var p = cmd.CreateParameter();
                    p.ParameterName = "@WorkId";
                    p.Value = item.WorkId.ToString();
                    cmd.Parameters.Add(p);

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var key = reader.GetString(0);
                        var val = reader.IsDBNull(1) ? null : reader.GetString(1);
                        if (string.IsNullOrEmpty(val)) continue;
                        switch (key)
                        {
                            case "title": title = val; break;
                            case "author": creator = val; break;
                            case "_asset_id": cover = $"/stream/{val}/cover"; break;
                            case "media_type": mediaType = val; break;
                        }
                    }

                    dtos.Add(new CollectionItemDto
                    {
                        Id        = item.Id,
                        WorkId    = item.WorkId,
                        Title     = title ?? $"Work {item.WorkId.ToString("N")[..8]}",
                        Creator   = creator,
                        MediaType = mediaType ?? "Unknown",
                        CoverUrl  = cover,
                        SortOrder = item.SortOrder,
                    });
                }
            }

            return Results.Ok(dtos);
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
            CancellationToken ct) =>
        {
            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            var collection = await collectionRepo.GetByIdAsync(id, ct);
            if (collection is null) return Results.NotFound();
            if (!CollectionAccessPolicy.CanEdit(collection, activeProfile))
                return Results.Forbid();
            if (!CollectionAccessPolicy.IsManagedCollectionType(collection.CollectionType)
                || !string.Equals(collection.Resolution, "materialized", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest("Only saved/manual collections support direct item membership.");
            }
            if (body.WorkId == Guid.Empty)
                return Results.BadRequest("work_id is required.");

            var existingItems = await collectionRepo.GetCollectionItemsAsync(id, 1000, ct);
            if (existingItems.Any(item => item.WorkId == body.WorkId))
                return Results.Ok();

            var nextSortOrder = existingItems.Count == 0
                ? 1
                : existingItems.Max(item => item.SortOrder) + 1;

            await collectionRepo.AddCollectionItemAsync(new CollectionItem
            {
                Id = Guid.NewGuid(),
                CollectionId = id,
                WorkId = body.WorkId,
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
            if (collection is null) return Results.NotFound();
            if (!CollectionAccessPolicy.CanEdit(collection, activeProfile))
                return Results.Forbid();
            if (!CollectionAccessPolicy.IsManagedCollectionType(collection.CollectionType)
                || !string.Equals(collection.Resolution, "materialized", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest("Only saved/manual collections support direct item membership.");
            }

            var existingItems = await collectionRepo.GetCollectionItemsAsync(id, 1000, ct);
            if (!existingItems.Any(item => item.Id == itemId))
                return Results.NotFound();

            await collectionRepo.RemoveCollectionItemAsync(itemId, ct);
            return Results.Ok();
        })
        .WithName("RemoveCollectionItem")
        .WithSummary("Removes a work from a saved/manual collection.")
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
            if (collection is null) return Results.NotFound();
            if (!CollectionAccessPolicy.CanEdit(collection, activeProfile))
                return Results.Forbid();

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
            if (collection is null) return Results.NotFound();
            if (!CollectionAccessPolicy.CanEdit(collection, activeProfile))
                return Results.Forbid();

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
            if (collection is null) return Results.NotFound();

            // For materialized collections, return works directly
            if (collection.Resolution == "materialized")
            {
                var collectionWithWorks = await collectionRepo.GetCollectionWithWorksAsync(id, ct);
                if (collectionWithWorks is null) return Results.NotFound();

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
            if (predicates.Count == 0) return Results.Ok(new List<CollectionResolvedItemDto>());

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
        // Unlike /registry/items, this path bypasses the registry visibility filter so
        // items that are still in the pipeline (no QID, no review) are included.
        // Used by the Vault flat views (All Songs) to show music even before the
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
                return Results.BadRequest("name parameter is required");

            var definition = BuiltInBrowseCollectionCatalog.FindByName(name);
            var collection = definition?.ToCollection();

            if (collection is null)
                return Results.NotFound($"No dynamic browse view found with name '{name}'");

            var predicates = CollectionRuleEvaluator.ParseRules(collection.RuleJson);
            if (predicates.Count == 0)
                return Results.Ok(new List<CollectionResolvedItemDto>());

            var evaluator = new CollectionRuleEvaluator(db);
            var entityIds = evaluator.Evaluate(
                predicates, collection.MatchMode, collection.SortField, collection.SortDirection, limit ?? 200);

            var resolved = ResolveEntityMetadataWithLineage(db, entityIds);
            return Results.Ok(resolved);
        })
        .WithName("ResolveCollectionByName")
        .WithSummary("Resolves a System collection by display name and returns items, reading both asset-level and parent-Work-level canonical values. Bypasses the registry visibility filter so in-flight items are included.")
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
                if (collection is null || !collection.IsEnabled) continue;

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
            if (body.Rules.Count == 0) return Results.Ok(new { count = 0, items = new List<CollectionResolvedItemDto>() });

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
                return Results.BadRequest("Collection name is required.");

            if (!CollectionAccessPolicy.IsManagedCollectionType(body.CollectionType))
                return Results.BadRequest($"Collection type '{body.CollectionType}' is reserved for browse-only system data.");

            var activeProfile = await ResolveActiveProfileAsync(profileId, profileRepo, ct);
            if (activeProfile is null)
                return Results.BadRequest("profileId is required to create a collection.");

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
            if (collection is null) return Results.NotFound();
            if (!CollectionAccessPolicy.IsManagedCollectionType(collection.CollectionType))
                return Results.BadRequest($"Collection type '{collection.CollectionType}' is browse-only and cannot be edited here.");
            if (!CollectionAccessPolicy.CanEdit(collection, activeProfile))
                return Results.Forbid();

            if (body.Name is not null) collection.DisplayName = body.Name;
            if (body.Description is not null) collection.Description = body.Description;
            if (body.IconName is not null) collection.IconName = body.IconName;
            if (body.MatchMode is not null) collection.MatchMode = body.MatchMode;
            if (body.SortField is not null) collection.SortField = body.SortField;
            if (body.SortDirection is not null) collection.SortDirection = body.SortDirection;
            if (body.LiveUpdating.HasValue) collection.LiveUpdating = body.LiveUpdating.Value;
            if (body.IsEnabled.HasValue) collection.IsEnabled = body.IsEnabled.Value;
            if (body.IsFeatured.HasValue) collection.IsFeatured = body.IsFeatured.Value;
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
                collection.LiveUpdating = false;

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
            if (collection is null) return Results.NotFound();
            if (!CollectionAccessPolicy.IsManagedCollectionType(collection.CollectionType))
                return Results.BadRequest($"Collection type '{collection.CollectionType}' is browse-only and cannot be deleted here.");
            if (collection.CollectionType == "System") return Results.BadRequest("System collections cannot be deleted.");
            if (!CollectionAccessPolicy.CanEdit(collection, activeProfile))
                return Results.Forbid();

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
                values.Add(reader.GetString(0));

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
            return null;

        return await profileRepo.GetByIdAsync(profileId.Value, ct);
    }

    private static async Task<int> GetManagedCollectionItemCountAsync(
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
                return 0;

            var evaluator = new CollectionRuleEvaluator(db);
            return evaluator.Evaluate(
                predicates,
                collection.MatchMode,
                collection.SortField,
                collection.SortDirection).Count;
        }

        return await collectionRepo.GetCollectionItemCountAsync(collection.Id, ct);
    }

    private static bool WorkMatchesQuery(WorkDto w, string query) =>
        w.CanonicalValues.Any(cv =>
            cv.Value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static HashSet<string> MultiValuedKeys => MetadataFieldConstants.MultiValuedKeys;

    private static string? GetCanonical(WorkDto? w, string key)
    {
        var raw = w?.CanonicalValues
            .FirstOrDefault(cv => cv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?.Value;
        if (raw is not null && raw.Contains("|||", StringComparison.Ordinal) && !MultiValuedKeys.Contains(key))
            return raw.Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return raw;
    }

    /// <summary>
    /// Builds the preferred cover URL from a Work's canonical values.
    /// </summary>
    private static string? BuildCoverStreamUrl(Work? w)
    {
        if (w is null) return null;
        return w.CanonicalValues
            .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.CoverUrl, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static string? BuildBackgroundStreamUrl(Work? w)
    {
        if (w is null) return null;
        return w.CanonicalValues
            .FirstOrDefault(c => string.Equals(c.Key, "background", StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static string? BuildBannerStreamUrl(Work? w)
    {
        if (w is null) return null;
        return w.CanonicalValues
            .FirstOrDefault(c => string.Equals(c.Key, "banner", StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static string? BuildLogoStreamUrl(Work? w)
    {
        if (w is null) return null;

        var canonicalLogo = w.CanonicalValues
            .FirstOrDefault(c => string.Equals(c.Key, "logo", StringComparison.OrdinalIgnoreCase))
            ?.Value;
        if (!string.IsNullOrWhiteSpace(canonicalLogo))
            return canonicalLogo;

        var assetId = w.CanonicalValues
            .Select(c => c.EntityId)
            .FirstOrDefault(id => id != Guid.Empty);
        return assetId != Guid.Empty ? $"/stream/{assetId}/logo" : null;
    }

    private static string? BuildHeroStreamUrl(Work? w)
    {
        if (w is null) return null;
        var assetId = w.CanonicalValues
            .Select(c => c.EntityId)
            .FirstOrDefault(id => id != Guid.Empty);
        return assetId != Guid.Empty ? $"/stream/{assetId}/hero" : null;
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

    private static async Task<List<CollectionGroupPersonDto>> BuildCharacterAwareCastAsync(
        string? rootWorkQid,
        Guid rootParentWorkId,
        ICanonicalValueArrayRepository canonicalArrayRepo,
        IPersonRepository personRepo,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(rootWorkQid))
        {
            using var conn = db.CreateConnection();
            var rows = (await conn.QueryAsync<CharacterCastRow>(
                """
                SELECT fe.id                AS CharacterId,
                       fe.label             AS CharacterName,
                       fe.wikidata_qid      AS CharacterQid,
                       fe.image_url         AS CharacterImageUrl,
                       p.id                 AS ActorPersonId,
                       p.name               AS ActorName,
                       p.wikidata_qid       AS ActorQid,
                       p.headshot_url       AS ActorHeadshotUrl,
                       p.local_headshot_path AS ActorLocalHeadshotPath,
                       cp.image_url         AS PortraitImageUrl,
                       cp.local_image_path  AS PortraitLocalImagePath,
                       cp.is_default        AS PortraitIsDefault
                FROM fictional_entities fe
                INNER JOIN fictional_entity_work_links fewl
                    ON fewl.entity_id = fe.id
                LEFT JOIN character_performer_links cpl
                    ON cpl.fictional_entity_id = fe.id
                   AND (cpl.work_qid = @workQid OR cpl.work_qid IS NULL)
                LEFT JOIN persons p
                    ON p.id = cpl.person_id
                LEFT JOIN character_portraits cp
                    ON cp.fictional_entity_id = fe.id
                   AND cp.person_id = p.id
                WHERE fewl.work_qid = @workQid
                  AND fe.entity_sub_type = 'Character'
                ORDER BY fe.label, p.name, cp.is_default DESC
                """,
                new { workQid = rootWorkQid })).ToList();

            var cast = rows
                .Where(row => row.ActorPersonId.HasValue && !string.IsNullOrWhiteSpace(row.ActorName))
                .GroupBy(row => new
                {
                    row.ActorPersonId,
                    row.ActorName,
                    row.ActorQid,
                    row.CharacterId,
                    row.CharacterName,
                    row.CharacterQid,
                })
                .Select(group =>
                {
                    var preferred = group
                        .OrderByDescending(row => row.PortraitIsDefault)
                        .ThenByDescending(row => !string.IsNullOrWhiteSpace(row.PortraitImageUrl))
                        .First();

                    var headshotUrl = !string.IsNullOrWhiteSpace(preferred.ActorLocalHeadshotPath) && preferred.ActorPersonId.HasValue
                        ? $"/stream/person/{preferred.ActorPersonId.Value}/headshot-thumb"
                        : preferred.ActorHeadshotUrl;
                    var characterImageUrl = preferred.PortraitImageUrl
                        ?? preferred.CharacterImageUrl;

                    return new CollectionGroupPersonDto
                    {
                        PersonId = preferred.ActorPersonId,
                        Name = preferred.ActorName ?? group.Key.ActorName ?? "Unknown",
                        ActorPersonId = preferred.ActorPersonId,
                        ActorName = preferred.ActorName,
                        WikidataQid = preferred.ActorQid,
                        HeadshotUrl = headshotUrl,
                        ActorHeadshotUrl = headshotUrl,
                        CharacterName = group.Key.CharacterName,
                        CharacterQid = group.Key.CharacterQid,
                        CharacterImageUrl = characterImageUrl,
                    };
                })
                .OrderBy(entry => entry.ActorName ?? entry.Name, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();

            if (cast.Count > 0)
                return cast;
        }

        var fallback = new List<CollectionGroupPersonDto>();
        var castEntries = await canonicalArrayRepo.GetValuesAsync(rootParentWorkId, "cast_member", ct);
        foreach (var entry in castEntries.OrderBy(e => e.Ordinal).Take(10))
        {
            if (string.IsNullOrWhiteSpace(entry.Value))
                continue;

            Person? person = null;
            if (!string.IsNullOrWhiteSpace(entry.ValueQid))
                person = await personRepo.FindByQidAsync(entry.ValueQid, ct);
            person ??= await personRepo.FindByNameAsync(entry.Value, ct);

            var headshotUrl = !string.IsNullOrEmpty(person?.LocalHeadshotPath)
                ? $"/stream/person/{person.Id}/headshot-thumb"
                : person?.HeadshotUrl;

            fallback.Add(new CollectionGroupPersonDto
            {
                PersonId = person?.Id,
                Name = person?.Name ?? entry.Value,
                ActorPersonId = person?.Id,
                ActorName = person?.Name ?? entry.Value,
                WikidataQid = entry.ValueQid ?? person?.WikidataQid,
                HeadshotUrl = headshotUrl,
                ActorHeadshotUrl = headshotUrl,
            });
        }

        return fallback;
    }

    private static string? NormalizeReleaseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTimeOffset.TryParse(value, out var parsed))
            return parsed.ToString("MMMM d, yyyy");

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
            return null;

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
            return null;

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
            return null;

        if (languages.Count == 1)
            return languages[0];

        return $"{languages[0]} + {languages.Count - 1} more";
    }

    private static string? NormalizeCodec(string? codec)
    {
        if (string.IsNullOrWhiteSpace(codec))
            return null;

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
            return ownedTracks
                .OrderBy(t => int.TryParse(t.TrackNumber, out var n) ? n : t.Ordinal ?? int.MaxValue)
                .ToList();
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(childEntitiesJson);
            if (!doc.RootElement.TryGetProperty("tracks", out var tracksArr) ||
                tracksArr.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return ownedTracks
                    .OrderBy(t => int.TryParse(t.TrackNumber, out var n) ? n : t.Ordinal ?? int.MaxValue)
                    .ToList();
            }

            // Build a lookup of owned tracks by normalized title for matching.
            var ownedByTitle = ownedTracks
                .Where(t => !string.IsNullOrWhiteSpace(t.Title))
                .GroupBy(t => t.Title.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var merged = new List<CollectionGroupWorkDto>();
            var seenOwned = new HashSet<Guid>();
            int wikidataOrdinal = 0;

            foreach (var trackEl in tracksArr.EnumerateArray())
            {
                wikidataOrdinal++;
                var title = trackEl.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == System.Text.Json.JsonValueKind.String
                    ? titleEl.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(title)) continue;

                var ordinal = trackEl.TryGetProperty("ordinal", out var ordEl) && ordEl.ValueKind == System.Text.Json.JsonValueKind.Number
                    ? ordEl.GetInt32()
                    : wikidataOrdinal;

                var key = title.Trim().ToLowerInvariant();
                if (ownedByTitle.TryGetValue(key, out var owned))
                {
                    // Owned — keep the local row but normalise the track number from Wikidata.
                    if (string.IsNullOrWhiteSpace(owned.TrackNumber))
                    {
                        merged.Add(new CollectionGroupWorkDto
                        {
                            WorkId        = owned.WorkId,
                            Title         = owned.Title,
                            Ordinal = ordinal,
                            Year          = owned.Year,
                            Duration      = owned.Duration,
                            CoverUrl      = owned.CoverUrl ?? albumCover,
                            WikidataQid   = owned.WikidataQid,
                            TrackNumber   = ordinal.ToString(),
                            Status        = owned.Status,
                            IsOwned       = true,
                        });
                    }
                    else
                    {
                        merged.Add(owned);
                    }
                    seenOwned.Add(owned.WorkId);
                }
                else
                {
                    // Unowned — synthesize a row from Wikidata data.
                    merged.Add(new CollectionGroupWorkDto
                    {
                        WorkId        = Guid.Empty,
                        Title         = title,
                        Ordinal = ordinal,
                        TrackNumber   = ordinal.ToString(),
                        CoverUrl      = albumCover,
                        Status        = "Unowned",
                        IsOwned       = false,
                    });
                }
            }

            // Append any owned tracks that didn't match a Wikidata title (rare — bonus tracks, mislabeled).
            foreach (var t in ownedTracks)
            {
                if (!seenOwned.Contains(t.WorkId))
                    merged.Add(t);
            }

            return merged
                .OrderBy(t => int.TryParse(t.TrackNumber, out var n) ? n : t.Ordinal ?? int.MaxValue)
                .ToList();
        }
        catch
        {
            // Malformed JSON — fall back to owned-only.
            return ownedTracks
                .OrderBy(t => int.TryParse(t.TrackNumber, out var n) ? n : t.Ordinal ?? int.MaxValue)
                .ToList();
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
                "album"     => ["tracks"],
                "series"    => ["issues"],
                _           => null,
            };

            if (arrayKeys is null) return;

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
                        continue;

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
                .Select(w => w.Title.Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!sectionMap.ContainsKey(sectionKey))
            sectionMap[sectionKey] = [];

        int wikiOrdinal = 0;
        foreach (var el in childArray.EnumerateArray())
        {
            wikiOrdinal++;
            var title = el.TryGetProperty("title", out var tEl)
                && tEl.ValueKind == System.Text.Json.JsonValueKind.String
                ? tEl.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(title)) continue;

            // Skip if an owned row with the same title is already in this section.
            if (ownedTitles.Contains(title.Trim().ToLowerInvariant())) continue;

            var ordinal = el.TryGetProperty("ordinal", out var oEl)
                && oEl.ValueKind == System.Text.Json.JsonValueKind.Number
                ? oEl.GetInt32()
                : wikiOrdinal;

            var episodeNumStr = isEpisode
                ? (el.TryGetProperty("episode_number", out var enEl)
                   && enEl.ValueKind == System.Text.Json.JsonValueKind.Number
                       ? enEl.GetInt32().ToString()
                       : ordinal.ToString())
                : null;

            sectionMap[sectionKey].Add(new CollectionGroupWorkDto
            {
                WorkId      = Guid.Empty,
                Title       = title,
                Ordinal     = ordinal,
                Episode     = episodeNumStr,
                TrackNumber = isEpisode ? null : ordinal.ToString(),
                CoverUrl    = fallbackCover,
                Status      = "Unowned",
                IsOwned     = false,
            });
        }
    }

    private sealed class CharacterCastRow
    {
        public Guid CharacterId { get; init; }
        public string? CharacterName { get; init; }
        public string? CharacterQid { get; init; }
        public string? CharacterImageUrl { get; init; }
        public Guid? ActorPersonId { get; init; }
        public string? ActorName { get; init; }
        public string? ActorQid { get; init; }
        public string? ActorHeadshotUrl { get; init; }
        public string? ActorLocalHeadshotPath { get; init; }
        public string? PortraitImageUrl { get; init; }
        public string? PortraitLocalImagePath { get; init; }
        public bool PortraitIsDefault { get; init; }
    }

    private static List<CollectionResolvedItemDto> ResolveEntityMetadata(IDatabaseConnection db, IReadOnlyList<Guid> entityIds)
    {
        if (entityIds.Count == 0) return [];

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
                if (string.IsNullOrEmpty(val)) continue;
                switch (key)
                {
                    case "title": title = val; break;
                    case "author" when creator is null: creator = val; break;
                    case "director" when creator is null: creator = val; break;
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

    /// <summary>
    /// Lineage-aware variant of <see cref="ResolveEntityMetadata"/> used by the
    /// <c>/resolve/by-name</c> endpoint.  For each Work this reads canonical values
    /// from both the asset row (Self-scoped fields: title, track_number) and from
    /// the topmost parent Work row (Parent-scoped fields: artist, album, genre,
    /// year).  Cover art is resolved via <c>/stream/{assetId}/cover</c> from the
    /// asset ID rather than canonical_values.  This mirrors the RegistryRepository
    /// pattern so that music items have correct artist/album/cover values even
    /// after the lineage-aware write splits them onto the album Work's entity_id.
    /// </summary>
    private static List<CollectionResolvedItemDto> ResolveEntityMetadataWithLineage(
        IDatabaseConnection db,
        IReadOnlyList<Guid> entityIds)
    {
        if (entityIds.Count == 0) return [];

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
                if (string.IsNullOrEmpty(val)) continue;
                // First occurrence of each key wins (Union order = Self then Parent).
                if (!seen.ContainsKey(key))
                    seen[key] = val;
            }

            seen.TryGetValue("title",      out var title);
            seen.TryGetValue("author",     out var author);
            seen.TryGetValue("director",   out var director);
            seen.TryGetValue("artist",     out var artist);
            seen.TryGetValue("year",       out var year);
            seen.TryGetValue("media_type", out var mediaType);

            string? cover = null;
            if (seen.TryGetValue("_asset_id", out var assetId))
                cover = $"/stream/{assetId}/cover";

            var creator = artist ?? author ?? director;

            result.Add(new CollectionResolvedItemDto
            {
                EntityId  = entityId,
                Title     = !string.IsNullOrEmpty(title) ? title : "Unknown",
                Creator   = creator,
                MediaType = !string.IsNullOrEmpty(mediaType) ? mediaType : "Unknown",
                CoverUrl  = cover,
                Year      = year,
            });
        }

        return result;
    }
}
