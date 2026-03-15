using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Compares an edition's canonical values against its master work (Wikidata P629).
///
/// <para>
/// When a library entity is an edition (instance_of = Q3331189), it may carry the
/// <c>edition_or_translation_of</c> canonical value pointing to a master work QID.
/// This service loads both sets of canonical values and compares core fields
/// (title, author, year) to detect discrepancies.
/// </para>
/// </summary>
public sealed class CanonDiscrepancyService : ICanonDiscrepancyService
{
    private static readonly HashSet<string> ComparisonFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "title", "author", "year", "genre", "series", "series_position",
    };

    private readonly ICanonicalValueRepository _canonRepo;
    private readonly ILogger<CanonDiscrepancyService> _logger;

    public CanonDiscrepancyService(
        ICanonicalValueRepository canonRepo,
        ILogger<CanonDiscrepancyService> logger)
    {
        ArgumentNullException.ThrowIfNull(canonRepo);
        ArgumentNullException.ThrowIfNull(logger);
        _canonRepo = canonRepo;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CanonDiscrepancy>> DetectAsync(
        Guid entityId, CancellationToken ct = default)
    {
        // Load canonical values for the edition entity.
        var editionValues = await _canonRepo.GetByEntityAsync(entityId, ct)
            .ConfigureAwait(false);

        var editionDict = editionValues
            .ToDictionary(cv => cv.Key, cv => cv.Value, StringComparer.OrdinalIgnoreCase);

        // Check if this is an edition — must have edition_or_translation_of.
        if (!editionDict.TryGetValue("edition_or_translation_of", out var masterWorkQid) ||
            string.IsNullOrWhiteSpace(masterWorkQid))
        {
            return [];
        }

        // Strip QID from URI if needed (e.g. "http://www.wikidata.org/entity/Q12345" → "Q12345").
        if (masterWorkQid.Contains('/'))
            masterWorkQid = masterWorkQid.Split('/')[^1];

        // Find entity IDs in the library that carry the matching wikidata_qid canonical value.
        var masterEntityIds = await _canonRepo.FindByValueAsync("wikidata_qid", masterWorkQid, ct)
            .ConfigureAwait(false);

        if (masterEntityIds.Count == 0)
        {
            _logger.LogDebug(
                "No entity found for master work {MasterQid} — skipping discrepancy check",
                masterWorkQid);
            return [];
        }

        // Use the first matching entity (there should only be one per QID).
        var masterValues = await _canonRepo.GetByEntityAsync(masterEntityIds[0], ct)
            .ConfigureAwait(false);

        if (masterValues.Count == 0)
        {
            _logger.LogDebug(
                "No canonical values found for master work {MasterQid} — skipping discrepancy check",
                masterWorkQid);
            return [];
        }

        var masterDict = masterValues
            .ToDictionary(cv => cv.Key, cv => cv.Value, StringComparer.OrdinalIgnoreCase);

        // Compare fields.
        var discrepancies = new List<CanonDiscrepancy>();
        foreach (var field in ComparisonFields)
        {
            if (!editionDict.TryGetValue(field, out var editionValue) ||
                !masterDict.TryGetValue(field, out var masterValue))
                continue;

            if (!string.Equals(editionValue, masterValue, StringComparison.OrdinalIgnoreCase))
            {
                discrepancies.Add(new CanonDiscrepancy(
                    field, masterValue, editionValue, masterWorkQid));
            }
        }

        if (discrepancies.Count > 0)
        {
            _logger.LogInformation(
                "Found {Count} canon discrepancies between entity {EntityId} and master work {MasterQid}",
                discrepancies.Count, entityId, masterWorkQid);
        }

        return discrepancies;
    }
}
