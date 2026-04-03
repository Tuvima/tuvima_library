using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Workers;

/// <summary>
/// Resolves narrative root, extracts character/location QIDs from canonicals,
/// and dispatches to <see cref="IRecursiveFictionalEntityService"/> for enrichment.
///
/// Extracted from <c>HydrationPipelineService.RunFictionalEntityEnrichmentAsync</c>.
/// </summary>
public sealed class FictionalEntityWorker
{
    private readonly INarrativeRootResolver _narrativeRootResolver;
    private readonly IRecursiveFictionalEntityService _fictionalEntityService;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly ILogger<FictionalEntityWorker> _logger;

    public FictionalEntityWorker(
        INarrativeRootResolver narrativeRootResolver,
        IRecursiveFictionalEntityService fictionalEntityService,
        ICanonicalValueRepository canonicalRepo,
        ILogger<FictionalEntityWorker> logger)
    {
        _narrativeRootResolver = narrativeRootResolver;
        _fictionalEntityService = fictionalEntityService;
        _canonicalRepo = canonicalRepo;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the narrative root for the entity, extracts fictional entity
    /// references (characters, locations) from canonicals, and enriches them.
    /// </summary>
    public async Task EnrichAsync(Guid entityId, string workQid, CancellationToken ct)
    {
        var canonicals = await _canonicalRepo.GetByEntityAsync(entityId, ct);

        // 1. Resolve narrative root
        var narrativeRoot = await _narrativeRootResolver.ResolveAsync(entityId, ct);

        if (narrativeRoot is null)
        {
            _logger.LogDebug(
                "No narrative root resolved for entity {Id} (QID={Qid})",
                entityId, workQid);
            return;
        }

        // 2. Extract fictional entity references from canonicals
        var entityRefs = ExtractFictionalEntityReferences(canonicals);
        if (entityRefs.Count == 0)
        {
            _logger.LogDebug("No fictional entity references found for entity {Id}", entityId);
            return;
        }

        // 3. Enrich
        var workLabel = canonicals
            .FirstOrDefault(c => string.Equals(c.Key, MetadataFieldConstants.Title,
                StringComparison.OrdinalIgnoreCase))?.Value;

        _logger.LogInformation(
            "Fictional entity enrichment: {Count} entities for work '{Title}' (QID={Qid}) " +
            "in universe '{Universe}'",
            entityRefs.Count, workLabel ?? "(unknown)", workQid,
            narrativeRoot.Label ?? "(unknown)");

        await _fictionalEntityService.EnrichAsync(
            workQid, workLabel,
            narrativeRoot.Qid, narrativeRoot.Label,
            entityRefs, ct);
    }

    /// <summary>
    /// Extracts character and location QIDs from canonical values.
    /// </summary>
    private static IReadOnlyList<FictionalEntityReference> ExtractFictionalEntityReferences(
        IReadOnlyList<CanonicalValue> canonicals)
    {
        var refs = new List<FictionalEntityReference>();

        var entityKeys = new Dictionary<string, (string LabelKey, string EntitySubType)>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["characters_qid"] = ("characters", "Character"),
            ["narrative_location_qid"] = ("narrative_location", "Location"),
        };

        foreach (var (qidKey, (labelKey, entitySubType)) in entityKeys)
        {
            var qidValue = canonicals
                .FirstOrDefault(c => string.Equals(c.Key, qidKey, StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (string.IsNullOrWhiteSpace(qidValue)) continue;

            var qidParts = qidValue.Split(["|||", "; "], StringSplitOptions.RemoveEmptyEntries);
            var labelParts = canonicals
                .FirstOrDefault(c => string.Equals(c.Key, labelKey, StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?.Split(["|||", "; "], StringSplitOptions.RemoveEmptyEntries) ?? [];

            for (var i = 0; i < qidParts.Length; i++)
            {
                var qidPart = qidParts[i].Trim();
                var segments = qidPart.Split("::", 2);
                var qid = segments[0];

                if (!qid.StartsWith('Q')) continue;

                var label = segments.Length > 1 && !string.IsNullOrWhiteSpace(segments[1])
                    ? segments[1].Trim()
                    : (i < labelParts.Length ? labelParts[i].Trim() : null) ?? qid;

                refs.Add(new FictionalEntityReference(qid, label, entitySubType));
            }
        }

        return refs;
    }
}
