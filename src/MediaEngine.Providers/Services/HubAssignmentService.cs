using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Assigns works to ContentGroup hubs based on Wikidata QID relationships.
/// After Stage 2 resolves a QID, this service reads the work's canonical values
/// (series_qid, franchise_qid, fictional_universe_qid) and creates or finds
/// a Hub for the parent entity (album, TV show, book series, etc.).
///
/// Uses <see cref="IHubRepository.FindByQidAsync"/> to avoid duplicates and
/// <see cref="IHubRepository.AssignWorkToHubAsync"/> to set the FK.
/// Idempotent — skips works that already have a hub_id.
/// </summary>
public sealed class HubAssignmentService
{
    private readonly IHubRepository _hubRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly ILogger<HubAssignmentService> _logger;

    public HubAssignmentService(
        IHubRepository hubRepo,
        ICanonicalValueRepository canonicalRepo,
        ILogger<HubAssignmentService> logger)
    {
        _hubRepo = hubRepo;
        _canonicalRepo = canonicalRepo;
        _logger = logger;
    }

    /// <summary>
    /// Finds or creates a ContentGroup hub for the work's parent entity
    /// (album, series, show) and assigns the work to it via hub_id FK.
    /// </summary>
    public async Task AssignAsync(Guid entityId, CancellationToken ct = default)
    {
        // Resolve the Work ID from the entity (MediaAsset) ID
        var workId = await _hubRepo.GetWorkIdByMediaAssetAsync(entityId, ct);
        if (workId is null)
        {
            _logger.LogDebug("HubAssignment: no work found for entity {EntityId}", entityId);
            return;
        }

        // Skip if work already has a hub_id
        var existingHubId = await _hubRepo.GetHubIdByWorkIdAsync(workId.Value, ct);
        if (existingHubId is not null)
        {
            _logger.LogDebug("HubAssignment: work {WorkId} already assigned to hub {HubId}",
                workId, existingHubId);
            return;
        }

        // Load canonical values — keyed by the MediaAsset entity ID (not the Work ID),
        // because the pipeline stores canonicals under job.EntityId which is the asset.
        var canonicals = await _canonicalRepo.GetByEntityAsync(entityId, ct);
        var lookup = canonicals.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);

        // Try to find a parent QID from Wikidata relationship properties.
        // Priority: series_qid (P179) > franchise_qid (P8345) > fictional_universe_qid (P1434)
        // We use the most specific level for hub assignment — an album or TV show,
        // not the broader franchise/universe.
        var (parentQid, parentLabel) = ResolveParentQid(lookup);

        if (string.IsNullOrWhiteSpace(parentQid))
        {
            _logger.LogDebug("HubAssignment: no parent QID for work {WorkId} — standalone", workId);
            return;
        }

        // Find or create a ContentGroup hub for this parent QID
        var hub = await _hubRepo.FindByQidAsync(parentQid, ct);

        if (hub is null)
        {
            // Sanitize the label — fall back to QID if label is just a QID or empty
            if (string.IsNullOrWhiteSpace(parentLabel) ||
                (parentLabel.Length > 1 && parentLabel[0] is 'Q' && char.IsDigit(parentLabel[1])))
            {
                parentLabel = parentQid;
            }

            hub = new Hub
            {
                Id = Guid.NewGuid(),
                DisplayName = parentLabel,
                WikidataQid = parentQid,
                HubType = "ContentGroup",
                Resolution = "materialized",
                UniverseStatus = "Unknown",
                CreatedAt = DateTimeOffset.UtcNow,
            };

            await _hubRepo.UpsertAsync(hub, ct);

            _logger.LogInformation(
                "HubAssignment: created ContentGroup hub '{Name}' ({Qid}) for work {WorkId}",
                hub.DisplayName, parentQid, workId);
        }

        // Assign the work to the hub
        await _hubRepo.AssignWorkToHubAsync(workId.Value, hub.Id, ct);

        _logger.LogInformation(
            "HubAssignment: assigned work {WorkId} to hub '{HubName}' ({Qid})",
            workId, hub.DisplayName, parentQid);
    }

    /// <summary>
    /// Extracts the most specific parent QID from canonical values.
    /// Priority: series (P179) → franchise (P8345) → fictional_universe (P1434).
    /// Returns the QID and human-readable label.
    /// </summary>
    private static (string? Qid, string? Label) ResolveParentQid(
        Dictionary<string, string> lookup)
    {
        // Try series first (most specific: album, TV show, book series)
        if (TryGetQid(lookup, "series", out var qid, out var label))
            return (qid, label);

        // Then franchise
        if (TryGetQid(lookup, "franchise", out qid, out label))
            return (qid, label);

        // Then fictional universe (broadest)
        if (TryGetQid(lookup, "fictional_universe", out qid, out label))
            return (qid, label);

        return (null, null);
    }

    /// <summary>
    /// Extracts a clean QID and label from canonical values for a given claim key.
    /// Handles URI prefixes, :: suffixes, and ||| legacy separators.
    /// </summary>
    private static bool TryGetQid(
        Dictionary<string, string> lookup,
        string claimKey,
        out string qid,
        out string label)
    {
        qid = string.Empty;
        label = string.Empty;

        if (!lookup.TryGetValue($"{claimKey}_qid", out var rawQid) ||
            string.IsNullOrWhiteSpace(rawQid))
            return false;

        // Strip entity URI prefix
        qid = rawQid.Contains('/') ? rawQid.Split('/')[^1] : rawQid;

        // Strip ||| legacy separator
        if (qid.Contains("|||"))
            qid = qid.Split("|||")[0].Trim();

        // Strip ::Label suffix
        if (qid.Contains("::"))
            qid = qid.Split("::", 2)[0];

        if (string.IsNullOrWhiteSpace(qid))
            return false;

        // Get the label
        if (lookup.TryGetValue(claimKey, out var rawLabel) && !string.IsNullOrWhiteSpace(rawLabel))
        {
            var cleaned = rawLabel.Contains("|||") ? rawLabel.Split("|||")[0].Trim() : rawLabel;
            label = cleaned.Contains("::") ? cleaned.Split("::", 2)[^1].Trim() : cleaned;
        }
        else
        {
            label = qid;
        }

        return true;
    }
}
