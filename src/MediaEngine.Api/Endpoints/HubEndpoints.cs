using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
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
                    string? title       = GetCanonical(WorkDto.FromDomain(w), "title") ?? $"Work {w.Id.ToString("N")[..8]}";
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
            string? hubCreator = GetCanonical(firstWorkDto, "author")
                                 ?? GetCanonical(firstWorkDto, "director")
                                 ?? GetCanonical(firstWorkDto, "artist");
            string? hubGenre   = GetCanonical(firstWorkDto, "genre");
            string? hubCover   = GetCanonical(firstWorkDto, "cover");

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
}
