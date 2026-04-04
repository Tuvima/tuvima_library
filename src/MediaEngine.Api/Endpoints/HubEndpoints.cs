using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

public static class HubEndpoints
{
    public static IEndpointRouteBuilder MapHubEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/hubs")
                       .WithTags("Hubs");

        group.MapGet("/", async (
            IHubRepository hubRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var hubs = await hubRepo.GetAllAsync(ct);

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

            // Filter hubs: only include works that are in the library (not staging)
            var filtered = new List<HubDto>();
            foreach (var hub in hubs)
            {
                var libraryWorks = hub.Works.Where(w => libraryWorkIds.Contains(w.Id)).ToList();
                if (libraryWorks.Count == 0) continue;

                var filteredHub = new Hub
                {
                    Id             = hub.Id,
                    UniverseId     = hub.UniverseId,
                    DisplayName    = hub.DisplayName,
                    CreatedAt      = hub.CreatedAt,
                    UniverseStatus = hub.UniverseStatus,
                    ParentHubId    = hub.ParentHubId,
                    WikidataQid    = hub.WikidataQid,
                };
                foreach (var w in libraryWorks)         filteredHub.Works.Add(w);
                foreach (var r in hub.Relationships)    filteredHub.Relationships.Add(r);

                filtered.Add(HubDto.FromDomain(filteredHub));
            }

            return Results.Ok(filtered);
        })
        .WithName("GetAllHubs")
        .WithSummary("List all media hubs with their works and canonical metadata values.")
        .Produces<List<HubDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapGet("/search", async (
            string? q,
            IHubRepository hubRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Results.Ok(Array.Empty<SearchResultDto>());

            var query = q.Trim();
            var hubs  = await hubRepo.GetAllAsync(ct);

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

            // Build DTOs for library-only works
            var filtered = new List<HubDto>();
            foreach (var hub in hubs)
            {
                var libraryWorks = hub.Works.Where(w => libraryWorkIds.Contains(w.Id)).ToList();
                if (libraryWorks.Count == 0) continue;

                var filteredHub = new Hub
                {
                    Id             = hub.Id,
                    UniverseId     = hub.UniverseId,
                    DisplayName    = hub.DisplayName,
                    CreatedAt      = hub.CreatedAt,
                    UniverseStatus = hub.UniverseStatus,
                    ParentHubId    = hub.ParentHubId,
                    WikidataQid    = hub.WikidataQid,
                };
                foreach (var w in libraryWorks)         filteredHub.Works.Add(w);
                foreach (var r in hub.Relationships)    filteredHub.Relationships.Add(r);

                filtered.Add(HubDto.FromDomain(filteredHub));
            }

            var dtos = filtered;

            var results = dtos
                .SelectMany(hub => hub.Works
                    .Where(w => WorkMatchesQuery(w, query))
                    .Select(w => new SearchResultDto
                    {
                        WorkId          = w.Id,
                        HubId           = hub.Id,
                        Title           = GetCanonical(w, "title")   ?? $"Work {w.Id}",
                        Author          = GetCanonical(w, "author"),
                        MediaType       = w.MediaType,
                        HubDisplayName  = GetCanonical(hub.Works.FirstOrDefault()!, "title")
                                          ?? hub.Id.ToString("N")[..8],
                        CoverUrl        = GetCanonical(w, "cover"),
                    }))
                .Take(20)
                .ToList();

            return Results.Ok(results);
        })
        .WithName("SearchHubs")
        .WithSummary("Full-text search across all works. Returns up to 20 matching results.")
        .Produces<List<SearchResultDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();


        // GET /hubs/{id}/related?limit= — cascading related hubs: series → author → genre → explore.
        // GET /hubs/parents — list all Parent Hubs for top-level franchise navigation.
        // IMPORTANT: registered before /{id:guid} routes to avoid route conflicts.
        group.MapGet("/parents", async (IHubRepository hubRepo, CancellationToken ct) =>
        {
            var allHubs = await hubRepo.GetAllAsync(ct);

            var parentIds = allHubs
                .Where(h => h.ParentHubId.HasValue)
                .Select(h => h.ParentHubId!.Value)
                .Distinct()
                .ToHashSet();

            var parents = allHubs
                .Where(h => parentIds.Contains(h.Id))
                .Select(h =>
                {
                    var children = allHubs.Where(c => c.ParentHubId == h.Id).ToList();
                    // Aggregate media types across all works in child hubs
                    var mediaTypes = children
                        .SelectMany(c => c.Works)
                        .Select(w => w.MediaType.ToString())
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(t => t)
                        .ToList();

                    return new ParentHubDto
                    {
                        Id             = h.Id,
                        UniverseId     = h.UniverseId,
                        DisplayName    = h.DisplayName,
                        Description    = h.Description,
                        WikidataQid    = h.WikidataQid,
                        ParentHubId    = null,
                        UniverseStatus = h.UniverseStatus,
                        CreatedAt      = h.CreatedAt,
                        ChildHubCount  = children.Count,
                        MediaTypes     = string.Join(", ", mediaTypes),
                        TotalWorks     = children.Sum(c => c.Works.Count),
                    };
                })
                .OrderBy(h => h.DisplayName)
                .ToList();

            return Results.Ok(parents);
        })
        .WithName("GetParentHubs")
        .WithSummary("Returns all Parent Hubs (franchise-level groupings).")
        .Produces<List<ParentHubDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // GET /hubs/{id}/children — returns child Hubs of a given parent.
        group.MapGet("/{id:guid}/children", async (Guid id, IHubRepository hubRepo, CancellationToken ct) =>
        {
            var children = await hubRepo.GetChildHubsAsync(id, ct);
            var result = children.Select(h => new
            {
                id             = h.Id,
                displayName    = h.DisplayName,
                parentHubId    = h.ParentHubId,
                createdAt      = h.CreatedAt,
                universeStatus = h.UniverseStatus,
            }).ToList();

            return Results.Ok(result);
        })
        .WithName("GetHubChildren")
        .WithSummary("Returns child Hubs of the given Parent Hub.")
        .RequireAnyRole();

        // GET /hubs/{id}/parent — returns the parent Hub of a given Hub (if any).
        group.MapGet("/{id:guid}/parent", async (Guid id, IHubRepository hubRepo, CancellationToken ct) =>
        {
            var hub = await hubRepo.GetByIdAsync(id, ct);
            if (hub is null)
                return Results.NotFound();

            if (!hub.ParentHubId.HasValue)
                return Results.Ok(new { parentHub = (object?)null });

            var parent = await hubRepo.GetByIdAsync(hub.ParentHubId.Value, ct);
            if (parent is null)
                return Results.Ok(new { parentHub = (object?)null });

            return Results.Ok(new
            {
                parentHub = new
                {
                    id             = parent.Id,
                    displayName    = parent.DisplayName,
                    createdAt      = parent.CreatedAt,
                    universeStatus = parent.UniverseStatus,
                }
            });
        })
        .WithName("GetHubParent")
        .WithSummary("Returns the Parent Hub of the given Hub, if any.")
        .RequireAnyRole();

        group.MapGet("/{id:guid}/related", async (
            Guid id,
            int? limit,
            IHubRepository hubRepo,
            CancellationToken ct) =>
        {
            var allHubs = await hubRepo.GetAllAsync(ct);
            var dtos    = allHubs.Select(HubDto.FromDomain).ToList();

            var target = dtos.FirstOrDefault(h => h.Id == id);
            if (target is null)
                return Results.NotFound($"Hub '{id}' not found.");

            int take = limit is > 0 ? limit.Value : 20;

            var targetSeries = GetCanonical(target.Works.FirstOrDefault(), "series");
            var targetAuthor = GetCanonical(target.Works.FirstOrDefault(), "author");
            var targetGenre  = GetCanonical(target.Works.FirstOrDefault(), "genre");

            var result   = new List<HubDto>();
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

            return Results.Ok(new RelatedHubsResponse
            {
                SectionTitle = title,
                Reason       = reason,
                Hubs         = result,
            });
        })
        .WithName("GetRelatedHubs")
        .WithSummary("Related hubs via cascade: series → author → genre → explore.")
        .Produces<RelatedHubsResponse>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // ── Group Detail ─────────────────────────────────────────────────────────

        // GET /hubs/{hubId}/group-detail — hub header + child works for sub-page rendering.
        group.MapGet("/{hubId:guid}/group-detail", async (
            Guid hubId,
            IHubRepository hubRepo,
            CancellationToken ct) =>
        {
            var hub = await hubRepo.GetHubWithWorksAsync(hubId, ct);
            if (hub is null)
                return Results.NotFound();

            // Determine primary media type from the works.
            var primaryMediaType = hub.Works
                .GroupBy(w => w.MediaType.ToString())
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key;

            bool isTv = string.Equals(primaryMediaType, "TV", StringComparison.OrdinalIgnoreCase);

            // Build per-work DTOs.
            var workDtos = hub.Works
                .OrderBy(w => w.SequenceIndex ?? int.MaxValue)
                .ThenBy(w => w.Id)
                .Select(w =>
                {
                    string? title       = (isTv ? GetCanonical(WorkDto.FromDomain(w), "episode_title") : null)
                                         ?? GetCanonical(WorkDto.FromDomain(w), "title")
                                         ?? $"Work {w.Id.ToString("N")[..8]}";
                    string? year        = GetCanonical(WorkDto.FromDomain(w), "release_year")
                                         ?? GetCanonical(WorkDto.FromDomain(w), "year");
                    string? duration    = GetCanonical(WorkDto.FromDomain(w), "duration")
                                         ?? GetCanonical(WorkDto.FromDomain(w), "runtime");
                    string? coverUrl    = GetCanonical(WorkDto.FromDomain(w), "cover");
                    string? season      = GetCanonical(WorkDto.FromDomain(w), "season_number");
                    string? episode     = GetCanonical(WorkDto.FromDomain(w), "episode_number");
                    string? trackNumber = GetCanonical(WorkDto.FromDomain(w), "track_number");

                    // Derive a display status from wikidata_status / match_level.
                    string status = w.WikidataStatus switch
                    {
                        "confirmed" => "Verified",
                        "skipped"   => "Unlinked",
                        _           => "Provisional",
                    };

                    // Pipeline stage stubs — state is derived from match/wikidata status.
                    var stage1 = new VaultPipelineStageDto
                    {
                        State = w.MatchLevel is "retail_only" or "work" or "edition" ? "done" : "pending",
                        Label = "Retail",
                    };
                    var stage2 = new VaultPipelineStageDto
                    {
                        State = w.WikidataStatus == "confirmed" ? "done" : "pending",
                        Label = "Wikidata",
                    };
                    var stage3 = new VaultPipelineStageDto
                    {
                        State = "pending",
                        Label = "Universe",
                    };

                    return new HubGroupWorkDto
                    {
                        WorkId        = w.Id,
                        Title         = title,
                        SequenceIndex = w.SequenceIndex,
                        Year          = year,
                        Duration      = duration,
                        CoverUrl      = coverUrl,
                        WikidataQid   = w.WikidataQid,
                        Season        = season,
                        Episode       = episode,
                        TrackNumber   = trackNumber,
                        Status        = status,
                        Stage1        = stage1,
                        Stage2        = stage2,
                        Stage3        = stage3,
                    };
                })
                .ToList();

            // Hub-level header canonical values (use first work as proxy).
            var firstWorkDto = hub.Works.Count > 0 ? WorkDto.FromDomain(hub.Works[0]) : null;
            string? hubCreator  = GetCanonical(firstWorkDto, "author")
                                 ?? GetCanonical(firstWorkDto, "director")
                                 ?? GetCanonical(firstWorkDto, "artist");
            string? hubGenre    = GetCanonical(firstWorkDto, "genre");
            string? hubCover    = GetCanonical(firstWorkDto, "cover");
            string? hubNetwork  = isTv ? GetCanonical(firstWorkDto, "network") : null;

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
            List<HubGroupSeasonDto> seasons = [];
            List<HubGroupWorkDto>   flatWorks = [];

            bool isMusic = string.Equals(primaryMediaType, "Music", StringComparison.OrdinalIgnoreCase);

            if (isTv)
            {
                seasons = workDtos
                    .GroupBy(w => int.TryParse(w.Season, out var sn) ? sn : 0)
                    .OrderBy(g => g.Key)
                    .Select(g => new HubGroupSeasonDto
                    {
                        SeasonNumber = g.Key,
                        SeasonLabel  = $"Season {g.Key}",
                        Episodes     = g.OrderBy(e => int.TryParse(e.Episode, out var en) ? en : e.SequenceIndex ?? int.MaxValue).ToList(),
                    })
                    .ToList();
            }
            else if (isMusic && workDtos.Count > 1)
            {
                // Music: tracks are already within one album hub, show as flat list with track ordering
                flatWorks = workDtos
                    .OrderBy(w => int.TryParse(w.TrackNumber, out var tn) ? tn : w.SequenceIndex ?? int.MaxValue)
                    .ToList();
            }
            else
            {
                flatWorks = workDtos;
            }

            var response = new HubGroupDetailDto
            {
                HubId            = hub.Id,
                DisplayName      = hub.DisplayName ?? $"Hub {hub.Id.ToString("N")[..8]}",
                WikidataQid      = hub.WikidataQid,
                PrimaryMediaType = primaryMediaType,
                CoverUrl         = hubCover,
                Creator          = hubCreator,
                YearRange        = yearRange,
                Genre            = hubGenre,
                Network          = hubNetwork,
                TotalItems       = hub.Works.Count,
                Seasons          = seasons,
                Works            = flatWorks,
            };

            return Results.Ok(response);
        })
        .WithName("GetHubGroupDetail")
        .WithSummary("Returns hub header metadata and child works sorted by sequence for sub-page rendering. TV works are grouped by season.")
        .Produces<HubGroupDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        // GET /hubs/artist-group-detail?hub_ids=id1,id2,... — combined multi-hub detail for artist-level drill-down.
        group.MapGet("/artist-group-detail", async (
            [Microsoft.AspNetCore.Mvc.FromQuery(Name = "hub_ids")] string hubIdsParam,
            IHubRepository hubRepo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(hubIdsParam))
                return Results.BadRequest("hub_ids parameter is required");

            var hubIds = hubIdsParam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Guid.TryParse(s, out var id) ? id : (Guid?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            if (hubIds.Count == 0)
                return Results.BadRequest("No valid hub IDs provided");

            // Load all hubs and build album-based seasons
            var allSeasons = new List<HubGroupSeasonDto>();
            string? combinedCreator = null;
            string? combinedCover = null;
            string? combinedGenre = null;
            int totalItems = 0;
            var allYears = new List<string>();

            int albumIndex = 0;
            foreach (var hubId in hubIds)
            {
                var hub = await hubRepo.GetHubWithWorksAsync(hubId, ct);
                if (hub is null) continue;

                var workDtos = hub.Works
                    .OrderBy(w => w.SequenceIndex ?? int.MaxValue)
                    .ThenBy(w => w.Id)
                    .Select(w =>
                    {
                        var wDto = WorkDto.FromDomain(w);
                        return new HubGroupWorkDto
                        {
                            WorkId        = w.Id,
                            Title         = GetCanonical(wDto, "title") ?? $"Track {w.Id.ToString("N")[..8]}",
                            SequenceIndex = w.SequenceIndex,
                            Year          = GetCanonical(wDto, "release_year") ?? GetCanonical(wDto, "year"),
                            Duration      = GetCanonical(wDto, "duration") ?? GetCanonical(wDto, "runtime"),
                            CoverUrl      = GetCanonical(wDto, "cover"),
                            WikidataQid   = w.WikidataQid,
                            TrackNumber   = GetCanonical(wDto, "track_number"),
                            Status        = w.WikidataStatus switch
                            {
                                "confirmed" => "Verified",
                                "skipped"   => "Unlinked",
                                _           => "Provisional",
                            },
                        };
                    })
                    .OrderBy(w => int.TryParse(w.TrackNumber, out var tn) ? tn : w.SequenceIndex ?? int.MaxValue)
                    .ToList();

                if (workDtos.Count > 0)
                {
                    var firstWorkDto = WorkDto.FromDomain(hub.Works[0]);
                    combinedCreator ??= GetCanonical(firstWorkDto, "artist")
                                       ?? GetCanonical(firstWorkDto, "author");
                    combinedCover ??= GetCanonical(firstWorkDto, "cover");
                    combinedGenre ??= GetCanonical(firstWorkDto, "genre");

                    var yearValues = workDtos.Where(w => !string.IsNullOrWhiteSpace(w.Year)).Select(w => w.Year!);
                    allYears.AddRange(yearValues);
                }

                allSeasons.Add(new HubGroupSeasonDto
                {
                    SeasonNumber = albumIndex,
                    SeasonLabel  = hub.DisplayName ?? $"Album {albumIndex + 1}",
                    Episodes     = workDtos,
                });

                totalItems += workDtos.Count;
                albumIndex++;
            }

            var years = allYears.Distinct().OrderBy(y => y).ToList();
            string? yearRange = years.Count switch
            {
                0 => null,
                1 => years[0],
                _ => $"{years[0]}–{years[^1]}",
            };

            var response = new HubGroupDetailDto
            {
                HubId            = hubIds[0],
                DisplayName      = combinedCreator ?? "Unknown Artist",
                PrimaryMediaType = "Music",
                CoverUrl         = combinedCover,
                Creator          = combinedCreator,
                YearRange        = yearRange,
                Genre            = combinedGenre,
                TotalItems       = totalItems,
                Seasons          = allSeasons,
                Works            = [],
            };

            return Results.Ok(response);
        })
        .WithName("GetArtistGroupDetail")
        .WithSummary("Returns combined multi-hub detail for artist-level drill-down in the Music tab. Each hub becomes an album 'season'.")
        .Produces<HubGroupDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAnyRole();

        // GET /hubs/artist-detail-by-name?artistName=X — Artist drill-down for system-view mode.
        // Queries works directly from canonical_values, grouped by album, returning the same HubGroupDetailDto shape.
        group.MapGet("/artist-detail-by-name", async (
            [Microsoft.AspNetCore.Mvc.FromQuery(Name = "artistName")] string? artistName,
            IDatabaseConnection db,
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
                        MAX(CASE WHEN cv.key = 'cover' THEN cv.value END) AS cover,
                        MAX(CASE WHEN cv.key = 'genre' THEN cv.value END) AS genre
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
            var albumMap = new Dictionary<string, List<HubGroupWorkDto>>(StringComparer.OrdinalIgnoreCase);
            var albumCovers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            string? combinedCreator = null;
            string? combinedCover = null;
            string? combinedGenre = null;
            var allYears = new List<string>();
            int totalItems = 0;

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

                combinedCreator ??= artistVal;
                combinedCover ??= cover;
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

                tracks.Add(new HubGroupWorkDto
                {
                    WorkId      = workId,
                    Title       = title ?? $"Track {workId.ToString("N")[..8]}",
                    Year        = year,
                    Duration    = duration ?? runtime,
                    CoverUrl    = cover,
                    TrackNumber = trackNum,
                    Status      = "Provisional",
                });

                totalItems++;
            }

            var years = allYears.Distinct().OrderBy(y => y).ToList();
            string? yearRange = years.Count switch
            {
                0 => null,
                1 => years[0],
                _ => $"{years[0]}–{years[^1]}",
            };

            var seasons = albumMap.Select((kvp, idx) => new HubGroupSeasonDto
            {
                SeasonNumber = idx,
                SeasonLabel  = kvp.Key,
                Episodes     = kvp.Value
                    .OrderBy(w => int.TryParse(w.TrackNumber, out var tn) ? tn : int.MaxValue)
                    .ToList(),
            }).ToList();

            var response = new HubGroupDetailDto
            {
                HubId            = Guid.Empty,
                DisplayName      = artistName,
                PrimaryMediaType = "Music",
                CoverUrl         = combinedCover,
                Creator          = combinedCreator,
                YearRange        = yearRange,
                Genre            = combinedGenre,
                TotalItems       = totalItems,
                Seasons          = seasons,
                Works            = [],
            };

            return Results.Ok(response);
        })
        .WithName("GetArtistDetailByName")
        .WithSummary("Returns artist drill-down detail by artist name, querying directly from canonical values. Used when system-view hubs are active and ContentGroup hubs are unavailable.")
        .Produces<HubGroupDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAnyRole();

        // GET /hubs/system-view-detail?groupField=show_name&groupValue=Breaking Bad&mediaType=TV
        // Generic system-view drill-down that works for any group field (show_name, series, album, artist).
        // Returns a HubGroupDetailDto with seasons/sections grouped by a secondary field when available.
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
                        MAX(CASE WHEN cv.key = 'title' THEN cv.value END) AS title,
                        MAX(CASE WHEN cv.key = 'episode_title' THEN cv.value END) AS episode_title,
                        MAX(CASE WHEN cv.key = 'show_name' THEN cv.value END) AS show_name,
                        MAX(CASE WHEN cv.key = 'season_number' THEN cv.value END) AS season_number,
                        MAX(CASE WHEN cv.key = 'episode_number' THEN cv.value END) AS episode_number,
                        MAX(CASE WHEN cv.key = 'series' THEN cv.value END) AS series,
                        MAX(CASE WHEN cv.key = 'series_index' THEN cv.value END) AS series_index,
                        MAX(CASE WHEN cv.key = 'album' THEN cv.value END) AS album,
                        MAX(CASE WHEN cv.key = 'artist' THEN cv.value END) AS artist,
                        MAX(CASE WHEN cv.key = 'author' THEN cv.value END) AS author,
                        MAX(CASE WHEN cv.key = 'director' THEN cv.value END) AS director,
                        MAX(CASE WHEN cv.key = 'track_number' THEN cv.value END) AS track_number,
                        MAX(CASE WHEN cv.key = 'release_year' THEN cv.value END) AS release_year,
                        MAX(CASE WHEN cv.key = 'year' THEN cv.value END) AS year_val,
                        MAX(CASE WHEN cv.key = 'duration' THEN cv.value END) AS duration,
                        MAX(CASE WHEN cv.key = 'runtime' THEN cv.value END) AS runtime,
                        MAX(CASE WHEN cv.key = 'cover' THEN cv.value END) AS cover,
                        MAX(CASE WHEN cv.key = 'genre' THEN cv.value END) AS genre,
                        MAX(CASE WHEN cv.key = 'network' THEN cv.value END) AS network
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
            var sectionMap = new Dictionary<string, List<HubGroupWorkDto>>(StringComparer.OrdinalIgnoreCase);
            string? combinedCreator = null;
            string? combinedCover = null;
            string? combinedGenre = null;
            string? combinedNetwork = null;
            var allYears = new List<string>();
            int totalItems = 0;

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

                items.Add(new HubGroupWorkDto
                {
                    WorkId       = workId,
                    Title        = episodeTitle ?? title ?? $"Item {workId.ToString("N")[..8]}",
                    Year         = year,
                    Duration     = duration ?? runtime,
                    CoverUrl     = cover,
                    Episode      = episodeNum,
                    TrackNumber  = trackNum,
                    SequenceIndex = int.TryParse(seqIndex, out var si) ? si : null,
                    Status       = "Provisional",
                });

                totalItems++;
            }

            var years = allYears.Distinct().OrderBy(y => y).ToList();
            string? yearRange = years.Count switch
            {
                0 => null,
                1 => years[0],
                _ => $"{years[0]}–{years[^1]}",
            };

            // Build seasons/sections if we have a secondary group
            List<HubGroupSeasonDto> seasons;
            List<HubGroupWorkDto> flatWorks;

            if (secondaryGroup is not null && sectionMap.Count > 0 && !sectionMap.ContainsKey("_flat"))
            {
                seasons = sectionMap
                    .OrderBy(kvp => int.TryParse(kvp.Key, out var n) ? n : int.MaxValue)
                    .ThenBy(kvp => kvp.Key)
                    .Select((kvp, idx) => new HubGroupSeasonDto
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

            var response = new HubGroupDetailDto
            {
                HubId            = Guid.Empty,
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
        .Produces<HubGroupDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAnyRole();

        // ── Content Groups ───────────────────────────────────────────────────────

        // GET /hubs/content-groups — Universe hubs that have child works (albums, TV series, book series, movie series).
        group.MapGet("/content-groups", async (IHubRepository hubRepo, CancellationToken ct) =>
        {
            var hubs = await hubRepo.GetContentGroupsAsync(ct);

            var dtos = hubs.Select(h =>
            {
                // Primary media type is whichever appears most among this hub's works.
                var primaryMediaType = h.Works
                    .GroupBy(w => w.MediaType.ToString())
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key ?? "Unknown";

                // Cover from the first work that has one.
                string? cover = null;
                foreach (var w in h.Works)
                {
                    var dto = WorkDto.FromDomain(w);
                    cover = GetCanonical(dto, "cover");
                    if (cover is not null) break;
                }

                // Creator from first work.
                var firstDto = h.Works.Count > 0 ? WorkDto.FromDomain(h.Works[0]) : null;
                string? creator = GetCanonical(firstDto, "author")
                                  ?? GetCanonical(firstDto, "director")
                                  ?? GetCanonical(firstDto, "artist");

                return new ContentGroupDto
                {
                    HubId            = h.Id,
                    DisplayName      = h.DisplayName ?? $"Hub {h.Id.ToString("N")[..8]}",
                    WikidataQid      = h.WikidataQid,
                    PrimaryMediaType = primaryMediaType,
                    WorkCount        = h.Works.Count,
                    CoverUrl         = cover,
                    Creator          = creator,
                    UniverseStatus   = h.UniverseStatus,
                    CreatedAt        = h.CreatedAt,
                };
            })
            .OrderBy(d => d.DisplayName)
            .ToList();

            return Results.Ok(dtos);
        })
        .WithName("GetContentGroups")
        .WithSummary("Returns Universe-type hubs that contain works (albums, TV series, book series, movie series), grouped by primary media type.")
        .Produces<List<ContentGroupDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // GET /hubs/system-views?mediaType=&groupField= — System view hubs resolved as content groups.
        // Used by Vault container views (By Show, By Artist, By Album) that are driven by System hubs
        // rather than ContentGroup hubs.
        group.MapGet("/system-views", async (
            string? mediaType,
            string? groupField,
            IHubRepository hubRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            // Load all System hubs that have a group_by_field (these are the view hubs seeded by HubSeeder)
            var systemHubs = await hubRepo.GetByTypeAsync("System", ct);
            var viewHubs = systemHubs
                .Where(h => !string.IsNullOrWhiteSpace(h.GroupByField) && h.Resolution == "query")
                .ToList();

            // Filter by mediaType and groupField if provided
            if (!string.IsNullOrWhiteSpace(mediaType))
            {
                viewHubs = viewHubs.Where(h =>
                {
                    var predicates = HubRuleEvaluator.ParseRules(h.RuleJson);
                    return predicates.Any(p =>
                        p.Field.Equals("media_type", StringComparison.OrdinalIgnoreCase) &&
                        p.Value?.Equals(mediaType, StringComparison.OrdinalIgnoreCase) == true);
                }).ToList();
            }

            if (!string.IsNullOrWhiteSpace(groupField))
            {
                viewHubs = viewHubs
                    .Where(h => h.GroupByField!.Equals(groupField, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (viewHubs.Count == 0)
                return Results.Ok(new List<ContentGroupDto>());

            var evaluator = new HubRuleEvaluator(db);
            var result = new List<ContentGroupDto>();

            using var conn = db.CreateConnection();

            foreach (var hub in viewHubs)
            {
                var predicates = HubRuleEvaluator.ParseRules(hub.RuleJson);
                if (predicates.Count == 0) continue;

                // Evaluate hub rules to get entity_ids
                var entityIds = evaluator.Evaluate(predicates, hub.MatchMode, hub.SortField, hub.SortDirection);
                if (entityIds.Count == 0) continue;

                var groupByField = hub.GroupByField!;

                // Determine primary media type from the hub's media_type predicate
                var primaryMediaType = predicates
                    .FirstOrDefault(p => p.Field.Equals("media_type", StringComparison.OrdinalIgnoreCase))
                    ?.Value ?? "Unknown";

                // Build IN clause for entity IDs
                var idList = string.Join(",", entityIds.Select(id => $"'{id}'"));

                // Group entity_ids by the group_by_field from canonical_values
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    WITH work_assets AS (
                        SELECT e.work_id, ma.id AS asset_id
                        FROM editions e
                        INNER JOIN media_assets ma ON ma.edition_id = e.id
                        WHERE e.work_id IN ({idList})
                    ),
                    grouped AS (
                        SELECT
                            cv_group.value                          AS group_name,
                            COUNT(DISTINCT wa.work_id)              AS work_count,
                            MIN(wa.asset_id)                        AS first_asset_id
                        FROM work_assets wa
                        INNER JOIN canonical_values cv_group ON cv_group.entity_id = wa.asset_id
                        WHERE cv_group.key = @GroupField
                        GROUP BY cv_group.value
                    )
                    SELECT
                        g.group_name,
                        g.work_count,
                        (
                            SELECT cv_cover.value
                            FROM canonical_values cv_cover
                            WHERE cv_cover.entity_id = g.first_asset_id
                              AND cv_cover.key = 'cover'
                            LIMIT 1
                        )                                           AS cover_url,
                        (
                            SELECT cv_creator.value
                            FROM canonical_values cv_creator
                            WHERE cv_creator.entity_id = g.first_asset_id
                              AND cv_creator.key IN ('artist','author','director')
                            LIMIT 1
                        )                                           AS creator
                    FROM grouped g
                    ORDER BY g.group_name
                    """;

                var gp = cmd.CreateParameter();
                gp.ParameterName = "@GroupField";
                gp.Value = groupByField;
                cmd.Parameters.Add(gp);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var groupName = reader.IsDBNull(0) ? null : reader.GetString(0);
                    if (string.IsNullOrWhiteSpace(groupName)) continue;

                    result.Add(new ContentGroupDto
                    {
                        HubId            = hub.Id,
                        DisplayName      = groupName,
                        WikidataQid      = null,
                        PrimaryMediaType = primaryMediaType,
                        WorkCount        = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                        CoverUrl         = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Creator          = reader.IsDBNull(3) ? null : reader.GetString(3),
                        UniverseStatus   = "Complete",
                        CreatedAt        = hub.CreatedAt,
                    });
                }
            }

            return Results.Ok(result.OrderBy(r => r.DisplayName).ToList());
        })
        .WithName("GetSystemViewGroups")
        .WithSummary("Resolves System view hubs (By Show, By Artist, By Album) as content groups for the Vault container views.")
        .Produces<List<ContentGroupDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // ── Managed Hub endpoints (Vault Hubs tab) ──────────────────────────────

        // GET /hubs/managed — all non-Universe hubs for the Vault Hubs tab.
        group.MapGet("/managed", async (
            IHubRepository hubRepo,
            CancellationToken ct) =>
        {
            var hubs = await hubRepo.GetManagedHubsAsync(ct);
            var dtos = new List<ManagedHubDto>();
            foreach (var hub in hubs)
            {
                // For Smart hubs, item count is from works.hub_id; for others, from hub_items
                int count = hub.HubType == "Smart"
                    ? hub.Works.Count
                    : await hubRepo.GetHubItemCountAsync(hub.Id, ct);
                dtos.Add(ManagedHubDto.FromDomain(hub, count));
            }
            return Results.Ok(dtos);
        })
        .WithName("GetManagedHubs")
        .WithSummary("List all non-Universe hubs (Smart, System, Mix, Playlist) for the Vault Hubs tab.")
        .Produces<List<ManagedHubDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // GET /hubs/managed/counts — type → count for stats bar.
        group.MapGet("/managed/counts", async (
            IHubRepository hubRepo,
            CancellationToken ct) =>
        {
            var counts = await hubRepo.GetCountsByTypeAsync(ct);
            return Results.Ok(counts);
        })
        .WithName("GetManagedHubCounts")
        .WithSummary("Returns hub count grouped by type for the Vault stats bar.")
        .Produces<Dictionary<string, int>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // GET /hubs/{id}/items?limit=20 — curated item preview.
        group.MapGet("/{id:guid}/items", async (
            Guid id,
            int? limit,
            IHubRepository hubRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            int take = limit is > 0 ? limit.Value : 20;
            var items = await hubRepo.GetHubItemsAsync(id, take, ct);

            // Resolve work metadata from canonical_values
            var dtos = new List<HubItemDto>();
            if (items.Count > 0)
            {
                using var conn = db.CreateConnection();
                foreach (var item in items)
                {
                    string? title = null, creator = null, mediaType = null, cover = null;
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = """
                        SELECT cv.key, cv.value
                        FROM canonical_values cv
                        WHERE cv.entity_id = @WorkId
                          AND cv.key IN ('title', 'author', 'cover')
                        UNION ALL
                        SELECT 'media_type', w.media_type
                        FROM works w WHERE w.id = @WorkId
                        """;
                    var p = cmd.CreateParameter();
                    p.ParameterName = "@WorkId";
                    p.Value = item.WorkId.ToString();
                    cmd.Parameters.Add(p);

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var key = reader.GetString(0);
                        var val = reader.GetString(1);
                        switch (key)
                        {
                            case "title": title = val; break;
                            case "author": creator = val; break;
                            case "cover": cover = val; break;
                            case "media_type": mediaType = val; break;
                        }
                    }

                    dtos.Add(new HubItemDto
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
        .WithName("GetHubItems")
        .WithSummary("Returns curated items for a hub with resolved work metadata.")
        .Produces<List<HubItemDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // PUT /hubs/{id}/enabled — toggle hub visibility.
        group.MapPut("/{id:guid}/enabled", async (
            Guid id,
            EnabledRequest body,
            IHubRepository hubRepo,
            CancellationToken ct) =>
        {
            await hubRepo.UpdateHubEnabledAsync(id, body.Enabled, ct);
            return Results.Ok();
        })
        .WithName("UpdateHubEnabled")
        .WithSummary("Toggle a hub's enabled state.")
        .RequireAnyRole();

        // PUT /hubs/{id}/featured — toggle hub featured state.
        group.MapPut("/{id:guid}/featured", async (
            Guid id,
            FeaturedRequest body,
            IHubRepository hubRepo,
            CancellationToken ct) =>
        {
            await hubRepo.UpdateHubFeaturedAsync(id, body.Featured, ct);
            return Results.Ok();
        })
        .WithName("UpdateHubFeatured")
        .WithSummary("Toggle a hub's featured state.")
        .RequireAnyRole();

        // ── Parameterized Hub endpoints ─────────────────────────────────────────

        // GET /hubs/resolve/{id}?limit= — evaluate hub rules, return items
        group.MapGet("/resolve/{id:guid}", async (
            Guid id,
            int? limit,
            IHubRepository hubRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var hub = await hubRepo.GetByIdAsync(id, ct);
            if (hub is null) return Results.NotFound();

            // For materialized hubs, return works directly
            if (hub.Resolution == "materialized")
            {
                var hubWithWorks = await hubRepo.GetHubWithWorksAsync(id, ct);
                if (hubWithWorks is null) return Results.NotFound();

                var take = limit ?? 0;
                var works = take > 0 ? hubWithWorks.Works.Take(take).ToList() : hubWithWorks.Works;
                var items = works.Select(w =>
                {
                    var dto = WorkDto.FromDomain(w);
                    return new HubResolvedItemDto
                    {
                        EntityId = w.Id,
                        Title = GetCanonical(dto, "title") ?? $"Work {w.Id.ToString("N")[..8]}",
                        Creator = GetCanonical(dto, "author") ?? GetCanonical(dto, "director") ?? GetCanonical(dto, "artist"),
                        MediaType = w.MediaType.ToString(),
                        CoverUrl = GetCanonical(dto, "cover"),
                        Year = GetCanonical(dto, "year"),
                    };
                }).ToList();

                return Results.Ok(items);
            }

            // For query-resolved hubs, evaluate rules
            var predicates = HubRuleEvaluator.ParseRules(hub.RuleJson);
            if (predicates.Count == 0) return Results.Ok(new List<HubResolvedItemDto>());

            var evaluator = new HubRuleEvaluator(db);
            var entityIds = evaluator.Evaluate(predicates, hub.MatchMode, hub.SortField, hub.SortDirection, limit ?? 0);

            var resolved = ResolveEntityMetadata(db, entityIds);
            return Results.Ok(resolved);
        })
        .WithName("ResolveHub")
        .WithSummary("Evaluate a hub's rules and return matching items.")
        .Produces<List<HubResolvedItemDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        // GET /hubs/by-location/{location} — hubs placed at a location
        group.MapGet("/by-location/{location}", async (
            string location,
            IHubPlacementRepository placementRepo,
            IHubRepository hubRepo,
            CancellationToken ct) =>
        {
            var placements = await placementRepo.GetByLocationAsync(location, ct);
            var result = new List<object>();

            foreach (var p in placements)
            {
                var hub = await hubRepo.GetByIdAsync(p.HubId, ct);
                if (hub is null || !hub.IsEnabled) continue;

                result.Add(new
                {
                    hub_id = hub.Id,
                    name = hub.DisplayName ?? $"Hub {hub.Id.ToString("N")[..8]}",
                    hub_type = hub.HubType,
                    icon_name = hub.IconName,
                    location = p.Location,
                    position = p.Position,
                    display_limit = p.DisplayLimit,
                    display_mode = p.DisplayMode,
                });
            }

            return Results.Ok(result);
        })
        .WithName("GetHubsByLocation")
        .WithSummary("Returns all hubs placed at a specific UI location, ordered by position.")
        .RequireAnyRole();

        // POST /hubs/preview — evaluate rules without saving
        group.MapPost("/preview", (
            HubPreviewRequest body,
            IDatabaseConnection db) =>
        {
            if (body.Rules.Count == 0) return Results.Ok(new { count = 0, items = new List<HubResolvedItemDto>() });

            var evaluator = new HubRuleEvaluator(db);
            var entityIds = evaluator.Evaluate(body.Rules, body.MatchMode, limit: body.Limit > 0 ? body.Limit : 20);

            var resolved = ResolveEntityMetadata(db, entityIds);
            return Results.Ok(new { count = entityIds.Count, items = resolved });
        })
        .WithName("PreviewHub")
        .WithSummary("Evaluate hub rules and return matching items without saving.")
        .RequireAnyRole();

        // POST /hubs — create a new hub
        group.MapPost("/", async (
            HubCreateRequest body,
            IHubRepository hubRepo,
            IHubPlacementRepository placementRepo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Hub name is required.");

            var ruleJson = body.Rules.Count > 0
                ? System.Text.Json.JsonSerializer.Serialize(body.Rules)
                : null;

            var ruleHash = body.Rules.Count > 0
                ? HubRuleEvaluator.ComputeRuleHash(body.Rules)
                : null;

            var resolution = body.HubType is "Playlist" or "System" or "Mix"
                ? "materialized" : "query";

            var hub = new Hub
            {
                Id = Guid.NewGuid(),
                DisplayName = body.Name,
                Description = body.Description,
                IconName = body.IconName,
                HubType = body.HubType,
                Scope = body.HubType is "Playlist" or "Custom" ? "user" : "library",
                IsEnabled = true,
                MinItems = 0,
                RuleJson = ruleJson,
                Resolution = resolution,
                RuleHash = ruleHash,
                MatchMode = body.MatchMode,
                SortField = body.SortField,
                SortDirection = body.SortDirection,
                LiveUpdating = body.LiveUpdating,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            await hubRepo.UpsertAsync(hub, ct);

            // Create placements
            if (body.Placements is { Count: > 0 })
            {
                foreach (var p in body.Placements)
                {
                    await placementRepo.UpsertAsync(new HubPlacement
                    {
                        Id = Guid.NewGuid(),
                        HubId = hub.Id,
                        Location = p.Location,
                        Position = p.Position,
                        DisplayLimit = p.DisplayLimit,
                        DisplayMode = p.DisplayMode,
                        IsVisible = true,
                        CreatedAt = DateTimeOffset.UtcNow,
                    }, ct);
                }
            }

            return Results.Created($"/hubs/{hub.Id}", new { id = hub.Id, name = hub.DisplayName });
        })
        .WithName("CreateHub")
        .WithSummary("Create a new hub with rules and optional placements.")
        .RequireAnyRole();

        // PUT /hubs/{id} — update hub
        group.MapPut("/{id:guid}", async (
            Guid id,
            HubUpdateRequest body,
            IHubRepository hubRepo,
            CancellationToken ct) =>
        {
            var hub = await hubRepo.GetByIdAsync(id, ct);
            if (hub is null) return Results.NotFound();

            if (body.Name is not null) hub.DisplayName = body.Name;
            if (body.Description is not null) hub.Description = body.Description;
            if (body.IconName is not null) hub.IconName = body.IconName;
            if (body.MatchMode is not null) hub.MatchMode = body.MatchMode;
            if (body.SortField is not null) hub.SortField = body.SortField;
            if (body.SortDirection is not null) hub.SortDirection = body.SortDirection;
            if (body.LiveUpdating.HasValue) hub.LiveUpdating = body.LiveUpdating.Value;
            if (body.IsEnabled.HasValue) hub.IsEnabled = body.IsEnabled.Value;
            if (body.IsFeatured.HasValue) hub.IsFeatured = body.IsFeatured.Value;

            if (body.Rules is { Count: > 0 })
            {
                hub.RuleJson = System.Text.Json.JsonSerializer.Serialize(body.Rules);
                hub.RuleHash = HubRuleEvaluator.ComputeRuleHash(body.Rules);
            }

            hub.ModifiedAt = DateTimeOffset.UtcNow;
            await hubRepo.UpsertAsync(hub, ct);
            return Results.Ok();
        })
        .WithName("UpdateHub")
        .WithSummary("Update a hub's rules, settings, or metadata.")
        .RequireAnyRole();

        // DELETE /hubs/{id} — soft delete (disable)
        group.MapDelete("/{id:guid}", async (
            Guid id,
            IHubRepository hubRepo,
            CancellationToken ct) =>
        {
            var hub = await hubRepo.GetByIdAsync(id, ct);
            if (hub is null) return Results.NotFound();
            if (hub.HubType == "System") return Results.BadRequest("System hubs cannot be deleted.");

            await hubRepo.UpdateHubEnabledAsync(id, false, ct);
            return Results.Ok();
        })
        .WithName("DeleteHub")
        .WithSummary("Soft-delete a hub by disabling it.")
        .RequireAnyRole();

        // GET /hubs/field-values/{field} — distinct values for autocomplete
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
        .WithSummary("Returns distinct values for a metadata field (used for hub builder autocomplete).")
        .RequireAnyRole();

        // GET /hubs/{id}/placements
        group.MapGet("/{id:guid}/placements", async (
            Guid id,
            IHubPlacementRepository placementRepo,
            CancellationToken ct) =>
        {
            var placements = await placementRepo.GetByHubIdAsync(id, ct);
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
        .WithName("GetHubPlacements")
        .WithSummary("Returns placements for a hub.")
        .RequireAnyRole();

        // PUT /hubs/{id}/placements — replace all placements
        group.MapPut("/{id:guid}/placements", async (
            Guid id,
            List<PlacementRequest> body,
            IHubPlacementRepository placementRepo,
            CancellationToken ct) =>
        {
            await placementRepo.DeleteByHubIdAsync(id, ct);
            foreach (var p in body)
            {
                await placementRepo.UpsertAsync(new HubPlacement
                {
                    Id = Guid.NewGuid(),
                    HubId = id,
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
        .WithName("UpdateHubPlacements")
        .WithSummary("Replace all placements for a hub.")
        .RequireAnyRole();

        return app;
    }

    // ── Request bodies ────────────────────────────────────────────────────────

    public sealed record EnabledRequest(bool Enabled);
    public sealed record FeaturedRequest(bool Featured);

    // ── Private helpers ───────────────────────────────────────────────────────

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

    private static List<HubResolvedItemDto> ResolveEntityMetadata(IDatabaseConnection db, IReadOnlyList<Guid> entityIds)
    {
        if (entityIds.Count == 0) return [];

        using var conn = db.CreateConnection();
        var result = new List<HubResolvedItemDto>();

        foreach (var entityId in entityIds)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT cv.key, cv.value
                FROM editions e
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                INNER JOIN canonical_values cv ON cv.entity_id = ma.id
                WHERE e.work_id = @EntityId
                  AND cv.key IN ('title', 'author', 'director', 'artist', 'cover', 'year')
                UNION ALL
                SELECT 'media_type', w.media_type
                FROM works w WHERE w.id = @EntityId
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
                var val = reader.GetString(1);
                switch (key)
                {
                    case "title": title = val; break;
                    case "author" when creator is null: creator = val; break;
                    case "director" when creator is null: creator = val; break;
                    case "artist" when creator is null: creator = val; break;
                    case "cover": cover = val; break;
                    case "year": year = val; break;
                    case "media_type": mediaType = val; break;
                }
            }

            result.Add(new HubResolvedItemDto
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
}
