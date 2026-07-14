using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;

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
            IFictionalEntityRepository entityRepo,
            IEntityRelationshipRepository relRepo,
            CancellationToken ct) =>
        {
            var roots = await rootRepo.ListAllAsync(ct);
            var results = new List<object>();
            foreach (var root in roots)
            {
                var entities = await entityRepo.GetByUniverseAsync(root.Qid, ct);
                var entityQids = entities.Select(e => e.WikidataQid).ToHashSet(StringComparer.OrdinalIgnoreCase);
                IReadOnlyList<MediaEngine.Domain.Entities.EntityRelationship> relationships =
                    entityQids.Count == 0
                        ? []
                        : await relRepo.GetByUniverseAsync(entityQids, ct);

                results.Add(new
                {
                    qid   = root.Qid,
                    label = root.Label,
                    level = root.Level,
                    parent_qid = root.ParentQid,
                    entity_count = entities.Count,
                    character_count = entities.Count(e => e.EntitySubType == "Character"),
                    location_count = entities.Count(e => e.EntitySubType == "Location"),
                    organization_count = entities.Count(e => e.EntitySubType == "Organization"),
                    event_count = entities.Count(e => e.EntitySubType == "Event"),
                    relationship_count = relationships.Count,
                    has_graph = entities.Count > 0 && relationships.Count > 0,
                    enrichment_status = entities.Count == 0
                        ? "Enrichment pending"
                        : relationships.Count == 0
                            ? "Partial"
                            : "Live",
                });
            }

            return Results.Ok(results);
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

        // GET /universe/{qid}/health — universe health score based on entity enrichment.
        group.MapGet("/universe/{qid}/health", async (
            string qid,
            INarrativeRootRepository rootRepo,
            IFictionalEntityRepository entityRepo,
            IEntityRelationshipRepository relRepo,
            IEntityAssetRepository assetRepo,
            CancellationToken ct) =>
        {
            var root = await rootRepo.FindByQidAsync(qid, ct);
            if (root is null)
                return Results.NotFound($"Narrative root '{qid}' not found.");

            var entities      = await entityRepo.GetByUniverseAsync(qid, ct);
            var entityQids    = entities.Select(e => e.WikidataQid).ToHashSet();
            var relationships = await relRepo.GetByUniverseAsync(entityQids, ct);

            var total      = entities.Count;
            var enriched   = entities.Count(e => e.EnrichedAt is not null);
            var withImages = entities.Count(e => !string.IsNullOrWhiteSpace(e.ImageUrl));
            var relCount   = relationships.Count;

            // Formula: base 20 + enrichment 40% + images 20% + relationship density 20%
            double health = 20.0;
            if (total > 0)
            {
                health += (enriched   / (double)total) * 40.0;
                health += (withImages / (double)total) * 20.0;
                var relDensity = Math.Min(relCount / (double)Math.Max(total, 1), 2.0) / 2.0;
                health += relDensity * 20.0;
            }

            return Results.Ok(new
            {
                qid                  = root.Qid,
                label                = root.Label,
                entities_total       = total,
                entities_enriched    = enriched,
                entities_with_images = withImages,
                relationships_total  = relCount,
                health_percent       = Math.Round(health, 1),
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
            bool? include_supplemental_lore,
            INarrativeRootRepository rootRepo,
            IFictionalEntityRepository entityRepo,
            IEntityRelationshipRepository relRepo,
            IPluginLoreRepository pluginLoreRepo,
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

            var workLinks = await entityRepo.GetWorkLinksAsync(
                allEntities.Select(entity => entity.Id), ct);
            var workLinksByEntity = workLinks.ToLookup(link => link.FictionalEntityId);

            // Apply work filter if specified (e.g. ?work=Q190192).
            if (!string.IsNullOrWhiteSpace(work))
            {
                allEntities = allEntities
                    .Where(entity => workLinksByEntity[entity.Id]
                        .Any(link => link.WorkQid.Equals(work, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            var entityQids = allEntities.Select(e => e.WikidataQid).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var relationships = (await relRepo.GetByUniverseAsync(entityQids, ct)).ToList();

            // Apply ego-network filter (e.g. ?center=Q937618&depth=1).
            if (!string.IsNullOrWhiteSpace(center) && entityQids.Contains(center))
            {
                var maxDepth = depth ?? 1;
                var egoNetwork = BuildEgoNetwork(center, maxDepth, entityQids, relationships);

                allEntities = allEntities.Where(e => egoNetwork.Contains(e.WikidataQid)).ToList();
                entityQids = allEntities.Select(e => e.WikidataQid).ToHashSet(StringComparer.OrdinalIgnoreCase);
                relationships = relationships
                    .Where(edge => entityQids.Contains(edge.SubjectQid)
                        && entityQids.Contains(edge.ObjectQid))
                    .ToList();
            }

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
            IReadOnlyDictionary<string, ActorResolution> actorResolutions =
                new Dictionary<string, ActorResolution>(StringComparer.OrdinalIgnoreCase);
            if (timeline_year.HasValue)
            {
                actorResolutions = await eraActorResolver.ResolveActorsForEraAsync(
                    allEntities
                        .Where(entity => string.Equals(
                            entity.EntitySubType, "Character", StringComparison.OrdinalIgnoreCase))
                        .Select(entity => entity.WikidataQid),
                    timeline_year.Value,
                    ct);
            }

            var nodes = new List<object>(allEntities.Count);
            foreach (var entity in allEntities)
            {
                // Resolve era-specific actor image for character nodes.
                string? image = null;
                if (timeline_year.HasValue
                    && string.Equals(entity.EntitySubType, "Character", StringComparison.OrdinalIgnoreCase)
                    && actorResolutions.TryGetValue(entity.WikidataQid, out var resolution)
                    && resolution.ActorPersonId.HasValue)
                {
                    image = ApiImageUrls.BuildPersonHeadshotUrl(
                        resolution.ActorPersonId.Value,
                        resolution.LocalHeadshotPath,
                        resolution.HeadshotUrl);
                }

                nodes.Add(new
                {
                    id         = entity.WikidataQid,
                    label      = entity.Label,
                    type       = entity.EntitySubType,
                    description = entity.Description,
                    image,
                    works      = workLinksByEntity[entity.Id]
                        .Select(link => new { qid = link.WorkQid, label = link.WorkLabel }),
                    supplemental = false,
                    provenance = "wikidata",
                    source_plugin = (string?)null,
                    source_url = (string?)null,
                });
            }

            // Build edges.
            var edges = relationships.Select(r => (object)new
            {
                source       = r.SubjectQid,
                target       = r.ObjectQid,
                type         = r.RelationshipTypeValue,
                label        = FormatEdgeLabel(r.RelationshipTypeValue),
                confidence   = r.Confidence,
                context_work = r.ContextWorkQid,
                start_time   = r.StartTime,
                end_time     = r.EndTime,
                supplemental = false,
                provenance = "wikidata",
                source_plugin = (string?)null,
                source_url = (string?)null,
            }).ToList();

            if (include_supplemental_lore == true
                && string.IsNullOrWhiteSpace(work)
                && string.IsNullOrWhiteSpace(center))
            {
                await AppendSupplementalLoreAsync(
                    qid,
                    types,
                    pluginLoreRepo,
                    entityQids,
                    nodes,
                    edges,
                    ct).ConfigureAwait(false);
            }

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

        // GET /universe/{qid}/cast — characters with their real-world performers.
        group.MapGet("/universe/{qid}/cast", async (
            string qid,
            IFictionalEntityRepository entityRepo,
            IPersonRepository personRepo,
            CancellationToken ct) =>
        {
            // Load all Character-type entities in this universe.
            var allEntities = await entityRepo.GetByUniverseAsync(qid, ct);
            var characters  = allEntities.Where(e =>
                string.Equals(e.EntitySubType, "Character", StringComparison.OrdinalIgnoreCase)).ToList();

            if (characters.Count == 0)
                return Results.Ok(new { universe_qid = qid, characters = Array.Empty<object>() });

            var allPerformerRows = await personRepo.GetCharacterPerformersAsync(
                characters.Select(character => character.Id),
                ct);

            // Group performers by entity UUID for O(1) lookup.
            var performersByEntity = allPerformerRows
                .GroupBy(performer => performer.FictionalEntityId)
                .ToDictionary(grouping => grouping.Key, grouping => grouping.ToList());

            var castList = characters.Select(character =>
            {
                performersByEntity.TryGetValue(character.Id, out var performers);
                return (object)new
                {
                    qid         = character.WikidataQid,
                    label       = character.Label,
                    image       = character.ImageUrl,
                    description = character.Description,
                    performers  = (performers ?? []).Select(p => new
                    {
                        person_id    = p.PersonId,
                        name         = p.PerformerName,
                        headshot_url = ApiImageUrls.BuildPersonHeadshotUrl(
                            p.PersonId,
                            p.LocalHeadshotPath,
                            p.HeadshotUrl),
                        work_qid     = p.WorkQid,
                        year         = (int?)null,
                    }),
                };
            }).ToList();

            return Results.Ok(new { universe_qid = qid, characters = castList });
        });

        // GET /universe/{qid}/adaptations — adaptation chain (based_on/derivative_work).
        group.MapGet("/universe/{qid}/adaptations", async (
            string qid,
            IFictionalEntityRepository entityRepo,
            IEntityRelationshipRepository relRepo,
            INarrativeRootRepository rootRepo,
            CancellationToken ct) =>
        {
            var root = await rootRepo.FindByQidAsync(qid, ct);
            if (root is null)
                return Results.NotFound($"Narrative root '{qid}' not found.");

            // Load all entities and adaptation-type relationships.
            var allEntities    = await entityRepo.GetByUniverseAsync(qid, ct);
            var entityQids     = allEntities.Select(e => e.WikidataQid).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allRels        = await relRepo.GetByUniverseAsync(entityQids, ct);
            var adaptationTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "based_on", "derivative_work", "inspired_by" };

            var adaptationRels = allRels
                .Where(r => adaptationTypes.Contains(r.RelationshipTypeValue))
                .ToList();

            // Build a lookup by entity QID → work metadata from entities table.
            // Works (not fictional entities) are tracked separately; fall back to entity data.
            var entityByQid = allEntities.ToDictionary(
                e => e.WikidataQid, e => e, StringComparer.OrdinalIgnoreCase);

            // Identify root works (sources that appear only as subjects, never as objects of based_on).
            var childQids = adaptationRels
                .Select(r => r.SubjectQid)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var rootWorkQids = adaptationRels
                .Select(r => r.ObjectQid)
                .Where(q => !childQids.Contains(q))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // If no adaptation relationships exist, return all works as flat roots.
            if (!adaptationRels.Any())
            {
                var flatWorks = allEntities.Select(e => new
                {
                    qid                    = e.WikidataQid,
                    label                  = e.Label,
                    year                   = (int?)null,
                    media_type             = e.EntitySubType,
                    cover_image            = e.ImageUrl,
                    relationship_to_parent = (string?)null,
                    children               = Array.Empty<object>(),
                }).ToList();
                return Results.Ok(new { universe_qid = qid, works = flatWorks });
            }

            // Build tree recursively.
            static IEnumerable<object> BuildChildren(
                string parentQid,
                IReadOnlyList<MediaEngine.Domain.Entities.EntityRelationship> rels,
                IReadOnlyDictionary<string, MediaEngine.Domain.Entities.FictionalEntity> byQid,
                int depth)
            {
                if (depth > 6) yield break; // Guard against cycles.
                foreach (var rel in rels.Where(r =>
                    string.Equals(r.ObjectQid, parentQid, StringComparison.OrdinalIgnoreCase)))
                {
                    byQid.TryGetValue(rel.SubjectQid, out var child);
                    yield return new
                    {
                        qid                    = rel.SubjectQid,
                        label                  = child?.Label ?? rel.SubjectQid,
                        year                   = (int?)null,
                        media_type             = child?.EntitySubType ?? "Unknown",
                        cover_image            = child?.ImageUrl,
                        relationship_to_parent = FormatEdgeLabel(rel.RelationshipTypeValue),
                        children               = BuildChildren(rel.SubjectQid, rels, byQid, depth + 1).ToList(),
                    };
                }
            }

            var tree = rootWorkQids.Select(rootQid =>
            {
                entityByQid.TryGetValue(rootQid, out var entity);
                return (object)new
                {
                    qid                    = rootQid,
                    label                  = entity?.Label ?? rootQid,
                    year                   = (int?)null,
                    media_type             = entity?.EntitySubType ?? "Unknown",
                    cover_image            = entity?.ImageUrl,
                    relationship_to_parent = (string?)null,
                    children               = BuildChildren(rootQid, adaptationRels, entityByQid, 1).ToList(),
                };
            }).ToList();

            return Results.Ok(new { universe_qid = qid, works = tree });
        });

        return app;
    }

    private static async Task AppendSupplementalLoreAsync(
        string universeQid,
        string? types,
        IPluginLoreRepository pluginLoreRepo,
        IReadOnlySet<string> existingQids,
        List<object> nodes,
        List<object> edges,
        CancellationToken ct)
    {
        var typeSet = string.IsNullOrWhiteSpace(types)
            ? null
            : new HashSet<string>(
                types.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);

        var supplementalEntities = await pluginLoreRepo.GetEntitiesAsync(universeQid, approvedOnly: true, ct).ConfigureAwait(false);
        if (typeSet is not null)
            supplementalEntities = supplementalEntities.Where(entity => typeSet.Contains(entity.EntityType)).ToList();

        var nodeIdsByExternalKey = new Dictionary<(Guid SourceId, string ExternalKey), string>();
        var nodeIds = existingQids.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in supplementalEntities)
        {
            var nodeId = !string.IsNullOrWhiteSpace(entity.WikidataQid)
                ? entity.WikidataQid
                : BuildSupplementalNodeId(entity.SourceId, entity.ExternalKey);

            nodeIdsByExternalKey[(entity.SourceId, entity.ExternalKey)] = nodeId;

            if (!nodeIds.Add(nodeId))
                continue;

            nodes.Add(new
            {
                id = nodeId,
                label = entity.Label,
                type = entity.EntityType,
                description = entity.Description,
                image = (string?)null,
                works = Array.Empty<object>(),
                supplemental = true,
                provenance = "plugin",
                source_plugin = entity.PluginId,
                source_url = entity.SourceUrl,
            });
        }

        var supplementalRelationships = await pluginLoreRepo.GetRelationshipsAsync(universeQid, approvedOnly: true, ct).ConfigureAwait(false);
        foreach (var relationship in supplementalRelationships)
        {
            var sourceId = !string.IsNullOrWhiteSpace(relationship.SubjectQid)
                ? relationship.SubjectQid
                : nodeIdsByExternalKey.GetValueOrDefault((relationship.SourceId, relationship.SubjectExternalKey));
            var targetId = !string.IsNullOrWhiteSpace(relationship.ObjectQid)
                ? relationship.ObjectQid
                : nodeIdsByExternalKey.GetValueOrDefault((relationship.SourceId, relationship.ObjectExternalKey));

            if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetId))
                continue;
            if (!nodeIds.Contains(sourceId) || !nodeIds.Contains(targetId))
                continue;

            edges.Add(new
            {
                source = sourceId,
                target = targetId,
                type = relationship.RelationshipType,
                label = FormatEdgeLabel(relationship.RelationshipType),
                confidence = relationship.Confidence,
                context_work = (string?)null,
                start_time = (string?)null,
                end_time = (string?)null,
                supplemental = true,
                provenance = "plugin",
                source_plugin = relationship.PluginId,
                source_url = relationship.SourceUrl,
            });
        }
    }

    internal static HashSet<string> BuildEgoNetwork(
        string center,
        int maxDepth,
        IReadOnlySet<string> entityQids,
        IReadOnlyList<MediaEngine.Domain.Entities.EntityRelationship> relationships)
    {
        var egoNetwork = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { center };
        var frontier = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { center };

        for (var currentDepth = 0; currentDepth < maxDepth; currentDepth++)
        {
            var nextFrontier = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var edge in relationships)
            {
                if (!frontier.Contains(edge.SubjectQid) && !frontier.Contains(edge.ObjectQid))
                    continue;

                if (entityQids.Contains(edge.SubjectQid) && egoNetwork.Add(edge.SubjectQid))
                    nextFrontier.Add(edge.SubjectQid);
                if (entityQids.Contains(edge.ObjectQid) && egoNetwork.Add(edge.ObjectQid))
                    nextFrontier.Add(edge.ObjectQid);
            }

            frontier = nextFrontier;
            if (frontier.Count == 0)
                break;
        }

        return egoNetwork;
    }

    private static string BuildSupplementalNodeId(Guid sourceId, string externalKey) =>
        $"plugin:{sourceId:N}:{Uri.EscapeDataString(externalKey)}";

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
        "based_on"            => "based on",
        "derivative_work"     => "derivative of",
        "inspired_by"         => "inspired by",
        _                     => relType.Replace('_', ' '),
    };
}
