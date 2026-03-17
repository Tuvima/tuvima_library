using MediaEngine.Domain.Contracts;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// Universe graph API endpoints — serves Cytoscape.js-ready JSON for the
/// fictional universe relationship graph.
///
/// <list type="bullet">
/// <item><c>GET /universes</c> — list all known narrative roots</item>
/// <item><c>GET /universe/{qid}</c> — universe detail with entity counts</item>
/// <item><c>GET /universe/{qid}/graph</c> — full graph: nodes + edges</item>
/// </list>
/// </summary>
public static class UniverseGraphEndpoints
{
    public static IEndpointRouteBuilder MapUniverseGraphEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/")
                       .WithTags("Universe Graph");

        // GET /universes — list all known narrative roots.
        group.MapGet("/universes", async (
            INarrativeRootRepository rootRepo,
            CancellationToken ct) =>
        {
            var roots = await rootRepo.ListAllAsync(ct);
            return Results.Ok(roots.Select(r => new
            {
                qid   = r.Qid,
                label = r.Label,
                level = r.Level,
                parent_qid = r.ParentQid,
            }));
        });

        // GET /universe/{qid} — universe detail with entity counts.
        group.MapGet("/universe/{qid}", async (
            string qid,
            INarrativeRootRepository rootRepo,
            IFictionalEntityRepository entityRepo,
            IEntityRelationshipRepository relRepo,
            CancellationToken ct) =>
        {
            var root = await rootRepo.FindByQidAsync(qid, ct);
            if (root is null)
                return Results.NotFound($"Narrative root '{qid}' not found.");

            var entities = await entityRepo.GetByUniverseAsync(qid, ct);
            var entityQids = entities.Select(e => e.WikidataQid).ToHashSet();
            var relationships = await relRepo.GetByUniverseAsync(entityQids, ct);

            return Results.Ok(new
            {
                universe = new { qid = root.Qid, label = root.Label, level = root.Level },
                entity_count = entities.Count,
                character_count = entities.Count(e => e.EntitySubType == "Character"),
                location_count = entities.Count(e => e.EntitySubType == "Location"),
                organization_count = entities.Count(e => e.EntitySubType == "Organization"),
                relationship_count = relationships.Count,
            });
        });

        // GET /universe/{qid}/lore-delta — check for Wikidata revision changes.
        group.MapGet("/universe/{qid}/lore-delta", async (
            string qid,
            ILoreDeltaService loreDeltaService,
            CancellationToken ct) =>
        {
            var results = await loreDeltaService.CheckForUpdatesAsync(qid, ct);
            return Results.Ok(results);
        });

        // GET /universe/{qid}/graph — Cytoscape.js-ready JSON: { universe, nodes[], edges[] }
        group.MapGet("/universe/{qid}/graph", async (
            string qid,
            string? types,
            string? work,
            string? center,
            int? depth,
            int? timeline_year,
            INarrativeRootRepository rootRepo,
            IFictionalEntityRepository entityRepo,
            IEntityRelationshipRepository relRepo,
            IPersonRepository personRepo,
            IEraActorResolverService eraActorResolver,
            CancellationToken ct) =>
        {
            var root = await rootRepo.FindByQidAsync(qid, ct);
            if (root is null)
                return Results.NotFound($"Narrative root '{qid}' not found.");

            // Load all entities in this universe.
            var allEntities = await entityRepo.GetByUniverseAsync(qid, ct);

            // Apply type filter if specified (e.g. ?types=Character,Location).
            if (!string.IsNullOrWhiteSpace(types))
            {
                var typeSet = new HashSet<string>(
                    types.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                    StringComparer.OrdinalIgnoreCase);
                allEntities = allEntities.Where(e => typeSet.Contains(e.EntitySubType)).ToList();
            }

            // Apply work filter if specified (e.g. ?work=Q190192).
            if (!string.IsNullOrWhiteSpace(work))
            {
                var entityIdsInWork = new HashSet<Guid>();
                foreach (var entity in allEntities)
                {
                    var workLinks = await entityRepo.GetWorkLinksAsync(entity.Id, ct);
                    if (workLinks.Any(wl => wl.WorkQid.Equals(work, StringComparison.OrdinalIgnoreCase)))
                        entityIdsInWork.Add(entity.Id);
                }
                allEntities = allEntities.Where(e => entityIdsInWork.Contains(e.Id)).ToList();
            }

            var entityQids = allEntities.Select(e => e.WikidataQid).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Apply ego-network filter (e.g. ?center=Q937618&depth=1).
            if (!string.IsNullOrWhiteSpace(center) && entityQids.Contains(center))
            {
                var maxDepth = depth ?? 1;
                var egoNetwork = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { center };
                var frontier = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { center };

                for (var d = 0; d < maxDepth; d++)
                {
                    var nextFrontier = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var nodeQid in frontier)
                    {
                        var nodeEdges = await relRepo.GetByEntityAsync(nodeQid, ct);
                        foreach (var edge in nodeEdges)
                        {
                            if (entityQids.Contains(edge.SubjectQid) && egoNetwork.Add(edge.SubjectQid))
                                nextFrontier.Add(edge.SubjectQid);
                            if (entityQids.Contains(edge.ObjectQid) && egoNetwork.Add(edge.ObjectQid))
                                nextFrontier.Add(edge.ObjectQid);
                        }
                    }
                    frontier = nextFrontier;
                    if (frontier.Count == 0) break;
                }

                allEntities = allEntities.Where(e => egoNetwork.Contains(e.WikidataQid)).ToList();
                entityQids = allEntities.Select(e => e.WikidataQid).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            // Load relationships between filtered entities.
            var relationships = (await relRepo.GetByUniverseAsync(entityQids, ct)).ToList();

            // Apply timeline year filter — exclude edges that started after the given year.
            if (timeline_year.HasValue)
            {
                relationships = relationships.Where(r =>
                {
                    if (string.IsNullOrWhiteSpace(r.StartTime)) return true;
                    // Handle Wikidata "+YYYY..." prefix by skipping the leading '+'.
                    var span = r.StartTime.AsSpan();
                    if (span.Length > 0 && span[0] == '+') span = span[1..];
                    if (int.TryParse(span[..Math.Min(4, span.Length)], out var startYear))
                        return startYear <= timeline_year.Value;
                    return true;
                }).ToList();
            }

            // Build nodes with work links.
            var nodes = new List<object>(allEntities.Count);
            foreach (var entity in allEntities)
            {
                var workLinks = await entityRepo.GetWorkLinksAsync(entity.Id, ct);

                // Resolve era-specific actor image for character nodes.
                string? image = null;
                if (timeline_year.HasValue
                    && string.Equals(entity.EntitySubType, "Character", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(entity.WikidataQid))
                {
                    var resolution = await eraActorResolver.ResolveActorForEraAsync(
                        entity.WikidataQid, timeline_year.Value, ct);
                    if (resolution is not null)
                        image = resolution.HeadshotUrl;
                }

                nodes.Add(new
                {
                    id         = entity.WikidataQid,
                    label      = entity.Label,
                    type       = entity.EntitySubType,
                    description = entity.Description,
                    image,
                    works      = workLinks.Select(wl => new { qid = wl.WorkQid, label = wl.WorkLabel }),
                });
            }

            // Build edges.
            var edges = relationships.Select(r => new
            {
                source       = r.SubjectQid,
                target       = r.ObjectQid,
                type         = r.RelationshipTypeValue,
                label        = FormatEdgeLabel(r.RelationshipTypeValue),
                confidence   = r.Confidence,
                context_work = r.ContextWorkQid,
                start_time   = r.StartTime,
                end_time     = r.EndTime,
            });

            return Results.Ok(new
            {
                universe = new { qid = root.Qid, label = root.Label },
                nodes,
                edges,
            });
        });

        // POST /universe/entity/{qid}/deep-enrich — on-demand deep enrichment.
        // Fetches 2+ hop entities for a character/entity that hasn't been deep-enriched.
        group.MapPost("/universe/entity/{qid}/deep-enrich", async (
            string qid,
            int? depth,
            IFictionalEntityRepository entityRepo,
            IMetadataHarvestingService harvesting,
            IEntityRelationshipRepository relRepo,
            CancellationToken ct) =>
        {
            // 1. Check if entity exists.
            var entity = await entityRepo.FindByQidAsync(qid, ct);
            if (entity is null)
                return Results.NotFound($"Entity '{qid}' not found.");

            var maxDepth = Math.Min(depth ?? 2, 3); // Cap at 3 to prevent runaway traversal.

            // 2. Get current relationships to find targets for deeper enrichment.
            var existingEdges = await relRepo.GetByEntityAsync(qid, ct);
            var neighborQids = existingEdges
                .SelectMany(e => new[] { e.SubjectQid, e.ObjectQid })
                .Where(q => !string.Equals(q, qid, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 3. Find neighbors that haven't been enriched yet.
            var unenrichedCount = 0;
            foreach (var neighborQid in neighborQids)
            {
                ct.ThrowIfCancellationRequested();

                var neighbor = await entityRepo.FindByQidAsync(neighborQid, ct);
                if (neighbor is null || neighbor.EnrichedAt is not null)
                    continue;

                var entityType = neighbor.EntitySubType switch
                {
                    "Character"    => MediaEngine.Domain.Enums.EntityType.Character,
                    "Location"     => MediaEngine.Domain.Enums.EntityType.Location,
                    "Organization" => MediaEngine.Domain.Enums.EntityType.Organization,
                    "Event"        => MediaEngine.Domain.Enums.EntityType.Event,
                    _              => MediaEngine.Domain.Enums.EntityType.Character,
                };

                await harvesting.EnqueueAsync(new MediaEngine.Domain.Models.HarvestRequest
                {
                    EntityId   = neighbor.Id,
                    EntityType = entityType,
                    MediaType  = MediaEngine.Domain.Enums.MediaType.Unknown,
                    Hints      = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["wikidata_qid"]     = neighborQid,
                        ["label"]            = neighbor.Label,
                        ["entity_sub_type"]  = neighbor.EntitySubType,
                        ["universe_qid"]     = neighbor.FictionalUniverseQid ?? string.Empty,
                        ["enrichment_depth"] = "1",
                    },
                }, ct);

                unenrichedCount++;
            }

            // 4. If the entity itself hasn't been enriched, enqueue it too.
            if (entity.EnrichedAt is null)
            {
                var selfEntityType = entity.EntitySubType switch
                {
                    "Character"    => MediaEngine.Domain.Enums.EntityType.Character,
                    "Location"     => MediaEngine.Domain.Enums.EntityType.Location,
                    "Organization" => MediaEngine.Domain.Enums.EntityType.Organization,
                    "Event"        => MediaEngine.Domain.Enums.EntityType.Event,
                    _              => MediaEngine.Domain.Enums.EntityType.Character,
                };

                await harvesting.EnqueueAsync(new MediaEngine.Domain.Models.HarvestRequest
                {
                    EntityId   = entity.Id,
                    EntityType = selfEntityType,
                    MediaType  = MediaEngine.Domain.Enums.MediaType.Unknown,
                    Hints      = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["wikidata_qid"]     = qid,
                        ["label"]            = entity.Label,
                        ["entity_sub_type"]  = entity.EntitySubType,
                        ["universe_qid"]     = entity.FictionalUniverseQid ?? string.Empty,
                        ["enrichment_depth"] = "0",
                    },
                }, ct);
                unenrichedCount++;
            }

            return Results.Ok(new
            {
                entity_qid          = qid,
                neighbors_found     = neighborQids.Count,
                enrichment_enqueued = unenrichedCount,
                message             = unenrichedCount > 0
                    ? $"Enqueued {unenrichedCount} entities for deep enrichment."
                    : "All neighboring entities are already enriched.",
            });
        });

        // GET /universe/{qid}/paths?from=Q1&to=Q2&maxHops=4 — find paths between entities.
        group.MapGet("/universe/{qid}/paths", async (
            string qid,
            string from,
            string to,
            int? maxHops,
            IUniverseGraphQueryService graphQuery,
            CancellationToken ct) =>
        {
            var paths = await graphQuery.FindPathsAsync(qid, from, to, maxHops ?? 4, ct);
            return Results.Ok(new { universe_qid = qid, from_qid = from, to_qid = to, paths });
        });

        // GET /universe/{qid}/family-tree?character=Q1&generations=3 — character family tree.
        group.MapGet("/universe/{qid}/family-tree", async (
            string qid,
            string character,
            int? generations,
            IUniverseGraphQueryService graphQuery,
            CancellationToken ct) =>
        {
            var tree = await graphQuery.GetFamilyTreeAsync(qid, character, generations ?? 3, ct);
            return Results.Ok(new
            {
                universe_qid  = qid,
                character_qid = character,
                generations   = tree.ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => kvp.Value),
            });
        });

        // GET /universe/{qid}/cross-media — entities appearing in 2+ works.
        group.MapGet("/universe/{qid}/cross-media", async (
            string qid,
            IUniverseGraphQueryService graphQuery,
            CancellationToken ct) =>
        {
            var entities = await graphQuery.FindCrossMediaEntitiesAsync(qid, ct);
            return Results.Ok(new { universe_qid = qid, cross_media_entities = entities });
        });

        return app;
    }

    /// <summary>
    /// Converts a relationship type constant (e.g. "father") to a human-readable
    /// edge label (e.g. "parent of"). Used in the graph JSON for Cytoscape.js display.
    /// </summary>
    private static string FormatEdgeLabel(string relType) => relType switch
    {
        "father"              => "father of",
        "mother"              => "mother of",
        "spouse"              => "spouse of",
        "sibling"             => "sibling of",
        "child"               => "child of",
        "opponent"            => "opponent of",
        "student_of"          => "student of",
        "member_of"           => "member of",
        "residence"           => "resides in",
        "located_in"          => "located in",
        "part_of"             => "part of",
        "head_of"             => "head of",
        "parent_organization" => "parent org of",
        "has_parts"           => "has parts",
        "creator"             => "created by",
        "performer"           => "performed by",
        "same_as"             => "same as",
        "significant_person"  => "significant to",
        "affiliation"         => "affiliated with",
        _                     => relType.Replace('_', ' '),
    };
}
