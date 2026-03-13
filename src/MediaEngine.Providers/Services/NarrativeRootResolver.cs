using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Resolves the narrative root (fictional universe, franchise, or series) for a work
/// based on its Wikidata canonical values after Stage 1 hydration.
///
/// <para>
/// Priority order (broadest first):
/// <list type="number">
/// <item>P1434 (<c>fictional_universe_qid</c>) — broadest universe</item>
/// <item>P8345 (<c>franchise_qid</c>) — franchise within a universe</item>
/// <item>P179 (<c>series_qid</c>) — series within a franchise</item>
/// <item>Hub DisplayName — standalone fallback (no QID from Wikidata)</item>
/// </list>
/// </para>
///
/// <para>
/// Cross-media convergence: both "Dune (novel)" and "Dune (2021 film)" share
/// P1434 = Q3041974. That shared QID becomes the <c>.universe/</c> folder and the
/// merge point for characters, locations, and organizations across all media.
/// </para>
/// </summary>
public sealed class NarrativeRootResolver : INarrativeRootResolver
{
    private readonly ICanonicalValueRepository _canonicalValues;
    private readonly INarrativeRootRepository _narrativeRoots;
    private readonly ISystemActivityRepository _activity;
    private readonly ILogger<NarrativeRootResolver> _logger;

    public NarrativeRootResolver(
        ICanonicalValueRepository canonicalValues,
        INarrativeRootRepository narrativeRoots,
        ISystemActivityRepository activity,
        ILogger<NarrativeRootResolver> logger)
    {
        _canonicalValues = canonicalValues;
        _narrativeRoots = narrativeRoots;
        _activity = activity;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<NarrativeRoot?> ResolveAsync(Guid entityId, CancellationToken ct = default)
    {
        var values = await _canonicalValues.GetByEntityAsync(entityId, ct);
        var lookup = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);

        // Priority 1: fictional_universe (P1434) — broadest
        if (TryExtractQidAndLabel(lookup, "fictional_universe", out var qid, out var label))
        {
            var root = await UpsertRootAsync(qid, label, NarrativeLevel.Universe, parentQid: null, ct);
            await StoreNarrativeRootOnWork(entityId, root, ct);

            // Also record franchise and series as child nodes if present
            await TryRecordChildNode(lookup, "franchise", NarrativeLevel.Franchise, qid, ct);
            await TryRecordChildNode(lookup, "series", NarrativeLevel.Series,
                GetQidValue(lookup, "franchise") ?? qid, ct);

            return root;
        }

        // Priority 2: franchise (P8345)
        if (TryExtractQidAndLabel(lookup, "franchise", out qid, out label))
        {
            var root = await UpsertRootAsync(qid, label, NarrativeLevel.Franchise, parentQid: null, ct);
            await StoreNarrativeRootOnWork(entityId, root, ct);

            // Record series as child if present
            await TryRecordChildNode(lookup, "series", NarrativeLevel.Series, qid, ct);

            return root;
        }

        // Priority 3: series (P179)
        if (TryExtractQidAndLabel(lookup, "series", out qid, out label))
        {
            var root = await UpsertRootAsync(qid, label, NarrativeLevel.Series, parentQid: null, ct);
            await StoreNarrativeRootOnWork(entityId, root, ct);
            return root;
        }

        // Priority 4: Standalone — no Wikidata universe link found
        _logger.LogDebug("No narrative root found for entity {EntityId} — standalone work", entityId);
        return null;
    }

    // ── Private Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Try to extract a QID and label from the canonical values for a given claim key.
    /// QID is stored as <c>{key}_qid</c> (e.g. <c>fictional_universe_qid</c>).
    /// Label is stored as <c>{key}</c> (e.g. <c>fictional_universe</c>).
    /// </summary>
    private static bool TryExtractQidAndLabel(
        Dictionary<string, string> lookup,
        string claimKey,
        out string qid,
        out string label)
    {
        qid = string.Empty;
        label = string.Empty;

        if (!lookup.TryGetValue($"{claimKey}_qid", out var rawQid) ||
            string.IsNullOrWhiteSpace(rawQid))
        {
            // Some properties emit entity URIs — try the raw value and strip the URI prefix.
            if (lookup.TryGetValue(claimKey, out var rawValue) &&
                !string.IsNullOrWhiteSpace(rawValue) &&
                rawValue.StartsWith("http://www.wikidata.org/entity/Q", StringComparison.Ordinal))
            {
                qid = rawValue.Split('/')[^1];
                label = rawValue; // Will be overwritten if a label exists
            }
            else
            {
                return false;
            }
        }
        else
        {
            // Strip entity URI prefix if present
            qid = rawQid.Contains('/') ? rawQid.Split('/')[^1] : rawQid;
        }

        // Multi-valued: take the first QID only (broadest universe)
        if (qid.Contains("|||"))
            qid = qid.Split("|||")[0].Trim();

        if (string.IsNullOrWhiteSpace(qid))
            return false;

        // Grab the human-readable label
        if (lookup.TryGetValue(claimKey, out var labelValue) && !string.IsNullOrWhiteSpace(labelValue))
        {
            label = labelValue.Contains("|||") ? labelValue.Split("|||")[0].Trim() : labelValue;
        }
        else
        {
            label = qid; // Fallback to QID as label
        }

        return true;
    }

    private static string? GetQidValue(Dictionary<string, string> lookup, string claimKey)
    {
        if (lookup.TryGetValue($"{claimKey}_qid", out var val) && !string.IsNullOrWhiteSpace(val))
        {
            var qid = val.Contains('/') ? val.Split('/')[^1] : val;
            return qid.Contains("|||") ? qid.Split("|||")[0].Trim() : qid;
        }

        return null;
    }

    private async Task<NarrativeRoot> UpsertRootAsync(
        string qid, string label, string level, string? parentQid, CancellationToken ct)
    {
        var root = new NarrativeRoot
        {
            Qid = qid,
            Label = label,
            Level = level,
            ParentQid = parentQid,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _narrativeRoots.UpsertAsync(root, ct);

        _logger.LogInformation(
            "Narrative root resolved: {Label} ({Qid}) at level {Level}",
            label, qid, level);

        return root;
    }

    private async Task TryRecordChildNode(
        Dictionary<string, string> lookup,
        string claimKey,
        string level,
        string parentQid,
        CancellationToken ct)
    {
        if (TryExtractQidAndLabel(lookup, claimKey, out var childQid, out var childLabel))
        {
            await UpsertRootAsync(childQid, childLabel, level, parentQid, ct);
        }
    }

    private async Task StoreNarrativeRootOnWork(
        Guid entityId, NarrativeRoot root, CancellationToken ct)
    {
        // Store narrative_root_qid and narrative_root_label as canonical values on the work
        var values = new[]
        {
            new Domain.Entities.CanonicalValue
            {
                EntityId = entityId,
                Key = "narrative_root_qid",
                Value = root.Qid,
                LastScoredAt = DateTimeOffset.UtcNow,
            },
            new Domain.Entities.CanonicalValue
            {
                EntityId = entityId,
                Key = "narrative_root_label",
                Value = root.Label,
                LastScoredAt = DateTimeOffset.UtcNow,
            },
        };

        await _canonicalValues.UpsertBatchAsync(values, ct);

        // Log activity
        await _activity.LogAsync(new Domain.Entities.SystemActivityEntry
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActionType = SystemActionType.NarrativeRootResolved,
            HubName = root.Label,
            EntityId = entityId,
            EntityType = "NarrativeRoot",
            Detail = $"Resolved narrative root: {root.Label} ({root.Qid}) at level {root.Level}",
        }, ct);
    }
}
