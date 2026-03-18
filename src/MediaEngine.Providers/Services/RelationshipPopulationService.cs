using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Reads enriched claims from a fictional entity and creates graph edges in
/// the <see cref="IEntityRelationshipRepository"/>.
///
/// <para>
/// Called after a Character/Location/Organization is enriched by Wikidata SPARQL.
/// For each <c>_qid</c> claim (e.g. <c>father_qid</c>, <c>member_of_qid</c>):
/// <list type="number">
/// <item>Find-or-create the target <see cref="FictionalEntity"/> by QID.</item>
/// <item>Insert a graph edge (idempotent via UNIQUE constraint).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Depth limit: 1</b> — target entities are created but NOT recursively enriched
/// for their own relationships. This prevents unbounded graph traversal.
/// </para>
/// </summary>
public interface IRelationshipPopulationService
{
    /// <summary>
    /// Populate relationship edges from the canonical values of an enriched entity.
    /// </summary>
    /// <param name="entityQid">The Wikidata QID of the enriched entity.</param>
    /// <param name="canonicalValues">Canonical values keyed by claim key.</param>
    /// <param name="universeQid">The narrative root QID for this entity's universe.</param>
    /// <param name="universeLabel">Human-readable universe label.</param>
    /// <param name="contextWorkQid">Optional work QID providing context for performer links.</param>
    /// <param name="temporalQualifiers">Optional per-target-QID temporal qualifiers (start/end time).</param>
    /// <param name="currentDepth">Current hop depth (0 = first-level relationships from the source entity).</param>
    /// <param name="maxDepth">Maximum enrichment depth. Target entities at depth &lt; maxDepth are enqueued for enrichment.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PopulateAsync(
        string entityQid,
        IReadOnlyDictionary<string, string> canonicalValues,
        string universeQid,
        string? universeLabel,
        string? contextWorkQid = null,
        IReadOnlyDictionary<string, (string? StartTime, string? EndTime)>? temporalQualifiers = null,
        int currentDepth = 0,
        int maxDepth = 1,
        CancellationToken ct = default);
}

/// <summary>
/// Mapping between claim keys and their relationship type + expected target entity type.
/// </summary>
internal static class RelationshipClaimMap
{
    /// <summary>
    /// Maps canonical value claim keys (that end in <c>_qid</c>) to relationship types.
    /// Only claims listed here produce graph edges.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, (string RelType, string TargetEntityType)> Map =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            // Character → Character relationships
            ["father_qid"]             = (RelationshipType.Father, FictionalEntityType.Character),
            ["mother_qid"]             = (RelationshipType.Mother, FictionalEntityType.Character),
            ["spouse_qid"]             = (RelationshipType.Spouse, FictionalEntityType.Character),
            ["sibling_qid"]            = (RelationshipType.Sibling, FictionalEntityType.Character),
            ["child_qid"]              = (RelationshipType.Child, FictionalEntityType.Character),
            ["opponent_qid"]           = (RelationshipType.Opponent, FictionalEntityType.Character),
            ["student_of_qid"]         = (RelationshipType.StudentOf, FictionalEntityType.Character),

            // Character → Character (romantic)
            ["partner_qid"]            = (RelationshipType.Partner, FictionalEntityType.Character),

            // Character → Organization
            ["member_of_qid"]          = (RelationshipType.MemberOf, FictionalEntityType.Organization),

            // Character → Organization (allegiance)
            ["allegiance_qid"]         = (RelationshipType.Allegiance, FictionalEntityType.Organization),

            // Character → Location
            ["residence_qid"]          = (RelationshipType.Residence, FictionalEntityType.Location),

            // Character → Organization/Location (education)
            ["educated_at_qid"]        = (RelationshipType.EducatedAt, FictionalEntityType.Organization),

            // Character/Location/Org → Person (creator)
            ["creator_qid"]            = (RelationshipType.Creator, FictionalEntityType.Character),

            // Location → Location
            ["located_in_qid"]         = (RelationshipType.LocatedIn, FictionalEntityType.Location),
            ["part_of_qid"]            = (RelationshipType.PartOf, FictionalEntityType.Location),

            // Organization → Character (head)
            ["head_of_qid"]            = (RelationshipType.HeadOf, FictionalEntityType.Character),

            // Organization → Organization
            ["parent_organization_qid"] = (RelationshipType.ParentOrganization, FictionalEntityType.Organization),
            ["has_parts_qid"]           = (RelationshipType.HasParts, FictionalEntityType.Organization),

            // Character/Location/Org → Entity (position)
            ["position_held_qid"]      = (RelationshipType.PositionHeld, FictionalEntityType.Character),

            // Character/Org → Event (conflict)
            ["conflict_qid"]           = (RelationshipType.Conflict, FictionalEntityType.Event),

            // Character → Character (social web)
            ["significant_person_qid"] = (RelationshipType.SignificantPerson, FictionalEntityType.Character),

            // Character → Organization (affiliation)
            ["affiliation_qid"]        = (RelationshipType.Affiliation, FictionalEntityType.Organization),
        };
}

/// <inheritdoc cref="IRelationshipPopulationService"/>
public sealed class RelationshipPopulationService : IRelationshipPopulationService
{
    private readonly IEntityRelationshipRepository _relRepo;
    private readonly IFictionalEntityRepository _entityRepo;
    private readonly ILogger<RelationshipPopulationService> _logger;

    // Resolved lazily to avoid a circular DI dependency:
    // MetadataHarvestingService → IRelationshipPopulationService → IMetadataHarvestingService.
    private IMetadataHarvestingService? _harvesting;
    private readonly IServiceProvider _serviceProvider;

    public RelationshipPopulationService(
        IEntityRelationshipRepository relRepo,
        IFictionalEntityRepository entityRepo,
        IServiceProvider serviceProvider,
        ILogger<RelationshipPopulationService> logger)
    {
        ArgumentNullException.ThrowIfNull(relRepo);
        ArgumentNullException.ThrowIfNull(entityRepo);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _relRepo         = relRepo;
        _entityRepo      = entityRepo;
        _serviceProvider = serviceProvider;
        _logger          = logger;
    }

    private IMetadataHarvestingService Harvesting =>
        _harvesting ??= _serviceProvider.GetService(typeof(IMetadataHarvestingService)) as IMetadataHarvestingService
            ?? throw new InvalidOperationException("IMetadataHarvestingService is not registered.");

    /// <inheritdoc/>
    public async Task PopulateAsync(
        string entityQid,
        IReadOnlyDictionary<string, string> canonicalValues,
        string universeQid,
        string? universeLabel,
        string? contextWorkQid = null,
        IReadOnlyDictionary<string, (string? StartTime, string? EndTime)>? temporalQualifiers = null,
        int currentDepth = 0,
        int maxDepth = 1,
        CancellationToken ct = default)
    {
        var edgesCreated = 0;

        foreach (var (claimKey, (relType, targetEntityType)) in RelationshipClaimMap.Map)
        {
            ct.ThrowIfCancellationRequested();

            if (!canonicalValues.TryGetValue(claimKey, out var rawQidValue) ||
                string.IsNullOrWhiteSpace(rawQidValue))
                continue;

            // DEPRECATED: ||| safety net for legacy SPARQL GROUP_CONCAT data.
            // New Reconciliation API emits individual claims — this split is a no-op for new data.
            var qids = rawQidValue.Contains("|||")
                ? rawQidValue.Split("|||", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : [rawQidValue];

            foreach (var rawQid in qids)
            {
                // Strip entity URI prefix if present
                var targetQid = rawQid.Contains('/')
                    ? rawQid.Split('/')[^1]
                    : rawQid;

                if (string.IsNullOrWhiteSpace(targetQid) || !targetQid.StartsWith('Q'))
                    continue;

                try
                {
                    // Find-or-create the target entity. Enqueues enrichment if within depth limit.
                    await EnsureTargetEntityExists(targetQid, targetEntityType, universeQid, universeLabel, currentDepth, maxDepth, ct)
                        .ConfigureAwait(false);

                    // Resolve temporal qualifiers if available.
                    var startTime = (string?)null;
                    var endTime = (string?)null;
                    if (temporalQualifiers?.TryGetValue(targetQid, out var temporal) == true)
                    {
                        startTime = temporal.StartTime;
                        endTime = temporal.EndTime;
                    }

                    // Create graph edge (idempotent via UNIQUE constraint).
                    await _relRepo.CreateAsync(new EntityRelationship
                    {
                        SubjectQid            = entityQid,
                        RelationshipTypeValue = relType,
                        ObjectQid             = targetQid,
                        Confidence            = 0.9,
                        ContextWorkQid        = contextWorkQid,
                        DiscoveredAt          = DateTimeOffset.UtcNow,
                        StartTime             = startTime,
                        EndTime               = endTime,
                    }, ct).ConfigureAwait(false);

                    edgesCreated++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to create {RelType} edge from {Subject} to {Object}",
                        relType, entityQid, targetQid);
                }
            }
        }

        if (edgesCreated > 0)
        {
            _logger.LogInformation(
                "Created {Count} relationship edges for entity {Qid}",
                edgesCreated, entityQid);

            if (!string.IsNullOrWhiteSpace(universeQid))
            {
                try
                {
                    var graphQuery = _serviceProvider.GetService(typeof(IUniverseGraphQueryService))
                        as IUniverseGraphQueryService;
                    graphQuery?.InvalidateCache(universeQid);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to invalidate graph cache for universe {Qid}", universeQid);
                }
            }
        }
    }

    /// <summary>
    /// Ensures a fictional entity record exists for the given QID.
    /// If not found, creates a stub record. When <paramref name="currentDepth"/> is
    /// less than <paramref name="maxDepth"/>, the new entity is also enqueued for
    /// Wikidata enrichment so its own relationships are discovered (2-hop lineage).
    /// </summary>
    private async Task EnsureTargetEntityExists(
        string qid, string entitySubType, string universeQid, string? universeLabel,
        int currentDepth, int maxDepth,
        CancellationToken ct)
    {
        var existing = await _entityRepo.FindByQidAsync(qid, ct).ConfigureAwait(false);
        if (existing is not null)
            return;

        var entity = new FictionalEntity
        {
            Id                     = Guid.NewGuid(),
            WikidataQid            = qid,
            Label                  = qid, // Will be updated when/if enriched
            EntitySubType          = entitySubType,
            FictionalUniverseQid   = universeQid,
            FictionalUniverseLabel = universeLabel,
            CreatedAt              = DateTimeOffset.UtcNow,
        };
        await _entityRepo.CreateAsync(entity, ct).ConfigureAwait(false);

        _logger.LogDebug(
            "Created stub entity for relationship target {Qid} ({Type})",
            qid, entitySubType);

        // Depth-aware enrichment: enqueue if within configured depth limit.
        if (currentDepth < maxDepth)
        {
            var entityType = entitySubType switch
            {
                FictionalEntityType.Character    => EntityType.Character,
                FictionalEntityType.Location     => EntityType.Location,
                FictionalEntityType.Organization => EntityType.Organization,
                FictionalEntityType.Event        => EntityType.Event,
                _                                => EntityType.Character,
            };

            await Harvesting.EnqueueAsync(new HarvestRequest
            {
                EntityId   = entity.Id,
                EntityType = entityType,
                MediaType  = MediaType.Unknown,
                Hints      = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["wikidata_qid"]    = qid,
                    ["label"]           = qid,
                    ["entity_sub_type"] = entitySubType,
                    ["universe_qid"]    = universeQid,
                    ["enrichment_depth"] = (currentDepth + 1).ToString(),
                },
            }, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Enqueued depth-{Depth} enrichment for relationship target {Qid} ({Type})",
                currentDepth + 1, qid, entitySubType);
        }
    }
}
