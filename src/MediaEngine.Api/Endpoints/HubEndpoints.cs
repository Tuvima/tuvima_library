using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

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

            // Build DTOs for library-only works, keeping a cover URL map from domain objects.
            var filtered = new List<HubDto>();
            var coverMap = new Dictionary<Guid, string>();
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
                foreach (var w in libraryWorks)
                {
                    filteredHub.Works.Add(w);
                    var url = BuildCoverStreamUrl(w);
                    if (url is not null) coverMap[w.Id] = url;
                }
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
                        CoverUrl        = coverMap.GetValueOrDefault(w.Id),
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
            ICanonicalValueRepository canonicalRepo,
            ICanonicalValueArrayRepository canonicalArrayRepo,
            IPersonRepository personRepo,
            IDatabaseConnection db,
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

            // Phase 4 — resolve the topmost Work id for the hub by walking the
            // parent_work_id chain from any of the hub's works (they all share
            // the same root parent in a ContentGroup hub). Parent-scope canonical
            // values (author, cover, genre, network, year) live on this row.
            Guid? rootParentWorkId = null;
            IReadOnlyList<CanonicalValue> parentCvs = [];
            if (hub.Works.Count > 0)
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
                idParam.Value = hub.Works[0].Id.ToString();
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

            // Build per-work DTOs.
            var workDtos = hub.Works
                .OrderBy(w => w.Ordinal ?? int.MaxValue)
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
                    string? coverUrl    = BuildCoverStreamUrl(w);
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
                        Ordinal = w.Ordinal,
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

            // Hub-level header canonical values come from the topmost Work row.
            // Phase 4 — parent-scoped fields (author, director, artist, genre, cover,
            // network) live on the root parent Work, not on individual child works.
            string? hubCreator  = ParentCv("author") ?? ParentCv("director") ?? ParentCv("artist");
            string? hubGenre    = ParentCv("genre");
            string? hubNetwork  = isTv ? ParentCv("network") : null;

            // Resolve cover URL as a /stream/ endpoint. Cover art is downloaded
            // to disk by CoverArtWorker and served via StreamEndpoints. We need
            // the root parent work's asset_id to build the URL.
            string? hubCover = null;
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
                    hubCover = $"/stream/{rootAssetStr}/cover";
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
                        Episodes     = g.OrderBy(e => int.TryParse(e.Episode, out var en) ? en : e.Ordinal ?? int.MaxValue).ToList(),
                    })
                    .ToList();
            }
            else if (isMusic && workDtos.Count > 1)
            {
                // Music: tracks are already within one album hub, show as flat list with track ordering
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
            var topCast = new List<HubGroupPersonDto>();
            bool hasCast = (isTv || string.Equals(primaryMediaType, "Movies", StringComparison.OrdinalIgnoreCase))
                           && rootParentWorkId.HasValue;
            if (hasCast)
            {
                var castEntries = await canonicalArrayRepo.GetValuesAsync(
                    rootParentWorkId!.Value, "cast_member", ct);
                foreach (var entry in castEntries.OrderBy(e => e.Ordinal).Take(10))
                {
                    if (string.IsNullOrWhiteSpace(entry.Value)) continue;

                    Person? person = null;
                    if (!string.IsNullOrWhiteSpace(entry.ValueQid))
                        person = await personRepo.FindByQidAsync(entry.ValueQid, ct);
                    person ??= await personRepo.FindByNameAsync(entry.Value, ct);

                    topCast.Add(new HubGroupPersonDto
                    {
                        PersonId     = person?.Id,
                        Name         = person?.Name ?? entry.Value,
                        WikidataQid  = entry.ValueQid ?? person?.WikidataQid,
                        HeadshotUrl  = !string.IsNullOrEmpty(person?.LocalHeadshotPath)
                                       ? $"/stream/person/{person.Id}/headshot-thumb"
                                       : person?.HeadshotUrl,
                    });
                }
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
                SeasonCount      = isTv ? seasons.Count : null,
                TopCast          = topCast,
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
            IPersonRepository personRepo,
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
            string? combinedGenre = null;
            int totalItems = 0;
            var allYears = new List<string>();

            int albumIndex = 0;
            foreach (var hubId in hubIds)
            {
                var hub = await hubRepo.GetHubWithWorksAsync(hubId, ct);
                if (hub is null) continue;

                // Build owned track DTOs from hub.Works.
                var ownedTracks = hub.Works
                    .OrderBy(w => w.Ordinal ?? int.MaxValue)
                    .ThenBy(w => w.Id)
                    .Select(w =>
                    {
                        var wDto = WorkDto.FromDomain(w);
                        return new HubGroupWorkDto
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

                // Per-album cover, year, and child_entities_json from this hub's first work.
                string? albumCover = null;
                string? albumYear = null;
                string? childJson = null;
                if (hub.Works.Count > 0)
                {
                    var firstWorkDto = WorkDto.FromDomain(hub.Works[0]);
                    combinedCreator ??= GetCanonical(firstWorkDto, "artist")
                                       ?? GetCanonical(firstWorkDto, "author");
                    combinedGenre ??= GetCanonical(firstWorkDto, "genre");
                    albumCover = BuildCoverStreamUrl(hub.Works[0]);
                    albumYear = GetCanonical(firstWorkDto, "release_year") ?? GetCanonical(firstWorkDto, "year");

                    // child_entities_json may be on any track in the album (album-level claim attached
                    // to whichever track was being processed when Stage 2 ran). Try each in order.
                    foreach (var w in hub.Works)
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

                allSeasons.Add(new HubGroupSeasonDto
                {
                    SeasonNumber = albumIndex,
                    SeasonLabel  = hub.DisplayName ?? $"Album {albumIndex + 1}",
                    CoverUrl     = albumCover,
                    AlbumHubId   = hub.Id,
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

            var response = new HubGroupDetailDto
            {
                HubId            = hubIds[0],
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
        .WithSummary("Returns combined multi-hub detail for artist-level drill-down in the Music tab. Each hub becomes an album 'season'.")
        .Produces<HubGroupDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAnyRole();

        // GET /hubs/artist-detail-by-name?artistName=X — Artist drill-down for system-view mode.
        // Queries works directly from canonical_values, grouped by album, returning the same HubGroupDetailDto shape.
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
            var albumMap = new Dictionary<string, List<HubGroupWorkDto>>(StringComparer.OrdinalIgnoreCase);
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

                tracks.Add(new HubGroupWorkDto
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
                return new HubGroupSeasonDto
                {
                    SeasonNumber = idx,
                    SeasonLabel  = albumKey,
                    CoverUrl     = albumCover,
                    Year         = albumYear,
                    AlbumHubId   = null, // by-name lookup has no concrete hub id
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

            var response = new HubGroupDetailDto
            {
                HubId            = Guid.Empty,
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
            // sectionKey → owned HubGroupWorkDtos. Unowned items are merged after
            // the reader loop using child_entities_json from the parent.
            var sectionMap = new Dictionary<string, List<HubGroupWorkDto>>(StringComparer.OrdinalIgnoreCase);
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

                items.Add(new HubGroupWorkDto
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
            List<HubGroupSeasonDto> seasons;
            List<HubGroupWorkDto> flatWorks;

            // Recalculate totalItems to include unowned rows added during merge.
            totalItems = sectionMap.Values.Sum(v => v.Count);

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

                // Cover from the first work that has a media asset.
                string? cover = null;
                foreach (var w in h.Works)
                {
                    cover = BuildCoverStreamUrl(w);
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
            IPersonRepository personRepo,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("HubEndpoints.SystemViews");

            log.LogInformation("[ByAlbum] system-views called — mediaType={MediaType} groupField={GroupField}",
                mediaType ?? "(none)", groupField ?? "(none)");

            // Load all System hubs that have a group_by_field (these are the view hubs seeded by HubSeeder)
            var systemHubs = await hubRepo.GetByTypeAsync("System", ct);
            var viewHubs = systemHubs
                .Where(h => !string.IsNullOrWhiteSpace(h.GroupByField) && h.Resolution == "query")
                .ToList();

            log.LogInformation("[ByAlbum] Found {Total} System hubs with a group_by_field; filtering for mediaType/groupField",
                viewHubs.Count);

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

            log.LogInformation("[ByAlbum] After filter: {Count} matching view hubs", viewHubs.Count);

            if (viewHubs.Count == 0)
            {
                log.LogWarning("[ByAlbum] No matching System view hubs — check HubSeeder seeded 'Music by Album' with group_by_field='album' and media_type predicate 'Music'");
                return Results.Ok(new List<ContentGroupDto>());
            }

            var evaluator = new HubRuleEvaluator(db);
            var result = new List<ContentGroupDto>();

            using var conn = db.CreateConnection();

            foreach (var hub in viewHubs)
            {
                var predicates = HubRuleEvaluator.ParseRules(hub.RuleJson);
                if (predicates.Count == 0) continue;

                // Evaluate hub rules to get entity_ids
                var entityIds = evaluator.Evaluate(predicates, hub.MatchMode, hub.SortField, hub.SortDirection);

                log.LogInformation("[ByAlbum] Hub '{HubName}' (groupByField={GroupByField}) matched {WorkCount} works from HubRuleEvaluator",
                    hub.DisplayName, hub.GroupByField, entityIds.Count);

                if (entityIds.Count == 0) continue;

                var groupByField = hub.GroupByField!;

                // Determine primary media type from the hub's media_type predicate
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
                var rows = new List<(string GroupName, int WorkCount, string? CoverUrl, string? Creator, string? Network, string? Year, int? SeasonCount, int AlbumCount)>();
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
                            reader.IsDBNull(6) ? null : (int?)reader.GetInt32(6),
                            reader.IsDBNull(7) ? 0 : reader.GetInt32(7)
                        ));
                    }
                }

                log.LogInformation("[ByAlbum] SQL grouping query for hub '{HubName}' (groupByField={GroupByField}) returned {RowCount} distinct group(s)",
                    hub.DisplayName, groupByField, rows.Count);

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
                        HubId            = hub.Id,
                        DisplayName      = row.GroupName,
                        WikidataQid      = null,
                        PrimaryMediaType = primaryMediaType,
                        WorkCount        = row.WorkCount,
                        CoverUrl         = row.CoverUrl,
                        Creator          = row.Creator,
                        UniverseStatus   = "Complete",
                        CreatedAt        = hub.CreatedAt,
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
                          AND cv.key IN ('title', 'author')
                        UNION ALL
                        SELECT 'media_type', w.media_type
                        FROM works w WHERE w.id = @WorkId
                        UNION ALL
                        SELECT '_asset_id', MIN(ma.id)
                        FROM editions e
                        INNER JOIN media_assets ma ON ma.edition_id = e.id
                        WHERE e.work_id = @WorkId
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
                        CoverUrl = BuildCoverStreamUrl(w),
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

        // GET /hubs/resolve/by-name?name=All%20Songs&limit=200
        // Resolves a System hub by display name and returns matching items.
        // Unlike /registry/items, this path bypasses the registry visibility filter so
        // items that are still in the pipeline (no QID, no review) are included.
        // Used by the Vault flat views (All Songs) to show music even before the
        // retail/Wikidata pipeline completes.  Fields are read from both the asset-level
        // and the root parent Work-level canonical_values rows so that parent-scoped
        // fields (artist, album, cover_url) are correctly resolved.
        group.MapGet("/resolve/by-name", async (
            string? name,
            int? limit,
            IHubRepository hubRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest("name parameter is required");

            // Find the first System hub with this display name (case-insensitive).
            var systemHubs = await hubRepo.GetByTypeAsync("System", ct);
            var hub = systemHubs.FirstOrDefault(h =>
                string.Equals(h.DisplayName, name, StringComparison.OrdinalIgnoreCase));

            if (hub is null)
                return Results.NotFound($"No System hub found with name '{name}'");

            var predicates = HubRuleEvaluator.ParseRules(hub.RuleJson);
            if (predicates.Count == 0)
                return Results.Ok(new List<HubResolvedItemDto>());

            var evaluator = new HubRuleEvaluator(db);
            var entityIds = evaluator.Evaluate(
                predicates, hub.MatchMode, hub.SortField, hub.SortDirection, limit ?? 200);

            var resolved = ResolveEntityMetadataWithLineage(db, entityIds);
            return Results.Ok(resolved);
        })
        .WithName("ResolveHubByName")
        .WithSummary("Resolves a System hub by display name and returns items, reading both asset-level and parent-Work-level canonical values. Bypasses the registry visibility filter so in-flight items are included.")
        .Produces<List<HubResolvedItemDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
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

    /// <summary>
    /// Builds a <c>/stream/{assetId}/cover</c> URL from a Work's first media asset ID.
    /// Canonical values loaded via hub/work repository queries are keyed on <c>media_assets.id</c>,
    /// so any canonical value's <see cref="CanonicalValue.EntityId"/> is the asset GUID.
    /// Returns null when the work has no canonical values (and thus no known asset).
    /// </summary>
    private static string? BuildCoverStreamUrl(Work? w)
    {
        if (w is null) return null;
        var assetId = w.CanonicalValues
            .Select(c => c.EntityId)
            .FirstOrDefault(id => id != Guid.Empty);
        return assetId != Guid.Empty ? $"/stream/{assetId}/cover" : null;
    }

    /// <summary>
    /// Merges Wikidata-discovered tracks (from <c>child_entities_json</c>) into the owned-track list,
    /// flagging those without a matching local file as <c>IsOwned = false</c>. Owned tracks are matched
    /// to Wikidata tracks by case-insensitive title.
    /// </summary>
    private static List<HubGroupWorkDto> MergeUnownedMusicTracks(
        List<HubGroupWorkDto> ownedTracks,
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

            var merged = new List<HubGroupWorkDto>();
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
                        merged.Add(new HubGroupWorkDto
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
                    merged.Add(new HubGroupWorkDto
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
        Dictionary<string, List<HubGroupWorkDto>> sectionMap,
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
        Dictionary<string, List<HubGroupWorkDto>> sectionMap,
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

            sectionMap[sectionKey].Add(new HubGroupWorkDto
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
    private static List<HubResolvedItemDto> ResolveEntityMetadataWithLineage(
        IDatabaseConnection db,
        IReadOnlyList<Guid> entityIds)
    {
        if (entityIds.Count == 0) return [];

        using var conn = db.CreateConnection();
        var result = new List<HubResolvedItemDto>(entityIds.Count);

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

            result.Add(new HubResolvedItemDto
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
