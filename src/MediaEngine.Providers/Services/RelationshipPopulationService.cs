using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

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
    /// <param name="ct">Cancellation token.</param>
    Task PopulateAsync(
        string entityQid,
        IReadOnlyDictionary<string, string> canonicalValues,
        string universeQid,
        string? universeLabel,
        string? contextWorkQid = null,
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

            // Character → Organization
            ["member_of_qid"]          = (RelationshipType.MemberOf, FictionalEntityType.Organization),

            // Character → Location
            ["residence_qid"]          = (RelationshipType.Residence, FictionalEntityType.Location),

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
        };
}

/// <inheritdoc cref="IRelationshipPopulationService"/>
public sealed class RelationshipPopulationService : IRelationshipPopulationService
{
    private readonly IEntityRelationshipRepository _relRepo;
    private readonly IFictionalEntityRepository _entityRepo;
    private readonly ILogger<RelationshipPopulationService> _logger;

    public RelationshipPopulationService(
        IEntityRelationshipRepository relRepo,
        IFictionalEntityRepository entityRepo,
        ILogger<RelationshipPopulationService> logger)
    {
        ArgumentNullException.ThrowIfNull(relRepo);
        ArgumentNullException.ThrowIfNull(entityRepo);
        ArgumentNullException.ThrowIfNull(logger);
        _relRepo    = relRepo;
        _entityRepo = entityRepo;
        _logger     = logger;
    }

    /// <inheritdoc/>
    public async Task PopulateAsync(
        string entityQid,
        IReadOnlyDictionary<string, string> canonicalValues,
        string universeQid,
        string? universeLabel,
        string? contextWorkQid = null,
        CancellationToken ct = default)
    {
        var edgesCreated = 0;

        foreach (var (claimKey, (relType, targetEntityType)) in RelationshipClaimMap.Map)
        {
            ct.ThrowIfCancellationRequested();

            if (!canonicalValues.TryGetValue(claimKey, out var rawQidValue) ||
                string.IsNullOrWhiteSpace(rawQidValue))
                continue;

            // Multi-valued: split on the ||| separator used by SPARQL GROUP_CONCAT
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
                    // Find-or-create the target entity (depth limit = 1: created but NOT enriched).
                    await EnsureTargetEntityExists(targetQid, targetEntityType, universeQid, universeLabel, ct)
                        .ConfigureAwait(false);

                    // Create graph edge (idempotent via UNIQUE constraint).
                    await _relRepo.CreateAsync(new EntityRelationship
                    {
                        SubjectQid            = entityQid,
                        RelationshipTypeValue = relType,
                        ObjectQid             = targetQid,
                        Confidence            = 0.9,
                        ContextWorkQid        = contextWorkQid,
                        DiscoveredAt          = DateTimeOffset.UtcNow,
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
        }
    }

    /// <summary>
    /// Ensures a fictional entity record exists for the given QID.
    /// If not found, creates a stub record. Does NOT enqueue enrichment
    /// (depth limit = 1).
    /// </summary>
    private async Task EnsureTargetEntityExists(
        string qid, string entitySubType, string universeQid, string? universeLabel,
        CancellationToken ct)
    {
        var existing = await _entityRepo.FindByQidAsync(qid, ct).ConfigureAwait(false);
        if (existing is not null)
            return;

        // Also try to get the label from canonical values — we'll use the QID as fallback.
        // The label claim key matches the relationship claim key minus the _qid suffix.
        await _entityRepo.CreateAsync(new FictionalEntity
        {
            Id                     = Guid.NewGuid(),
            WikidataQid            = qid,
            Label                  = qid, // Will be updated when/if enriched
            EntitySubType          = entitySubType,
            FictionalUniverseQid   = universeQid,
            FictionalUniverseLabel = universeLabel,
            CreatedAt              = DateTimeOffset.UtcNow,
        }, ct).ConfigureAwait(false);

        _logger.LogDebug(
            "Created stub entity for relationship target {Qid} ({Type})",
            qid, entitySubType);
    }
}
