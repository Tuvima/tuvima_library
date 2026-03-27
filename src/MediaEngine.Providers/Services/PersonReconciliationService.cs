using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;
using Tuvima.WikidataReconciliation;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Standalone person reconciliation service that resolves unlinked person names
/// to Wikidata QIDs by searching for matching human entities and scoring them
/// against expected role (occupation) and optional notable work title.
///
/// Three-tier confidence model:
///   - Tier 1 (0.90): Structured Wikidata properties (P50, P57, P161, P175)
///   - Tier 2 (0.80): This service — standalone name search with occupation match
///   - Tier 3 (0.75): AI description extraction fallback
///
/// Auto-accept threshold: score >= 0.80. Below that, the person is skipped
/// and retried at the next 30-day refresh cycle.
/// </summary>
public sealed class PersonReconciliationService : IPersonReconciliationService
{
    private const double AutoAcceptThreshold = 0.80;
    private const double OccupationBoost = 0.20;
    private const double NotableWorkBoost = 0.10;

    // Q5 = human — used to filter non-person entities from search results.
    private const string HumanClassQid = "Q5";

    private readonly WikidataReconciler? _reconciler;
    private readonly IConfigurationLoader _configLoader;
    private readonly ILogger<PersonReconciliationService> _logger;

    /// <summary>
    /// Maps PersonReference roles to expected Wikidata P106 (occupation) labels.
    /// Multiple labels per role to handle variant Wikidata labeling.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> OccupationsByRole =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Author"]       = new(StringComparer.OrdinalIgnoreCase) { "writer", "author", "novelist", "poet", "playwright", "essayist", "journalist" },
            ["Narrator"]     = new(StringComparer.OrdinalIgnoreCase) { "narrator", "voice actor", "actor", "actress", "audiobook narrator" },
            ["Director"]     = new(StringComparer.OrdinalIgnoreCase) { "film director", "television director", "director", "film producer" },
            ["Screenwriter"] = new(StringComparer.OrdinalIgnoreCase) { "screenwriter", "playwright", "television writer", "writer" },
            ["Composer"]     = new(StringComparer.OrdinalIgnoreCase) { "composer", "film score composer", "musician", "songwriter" },
            ["Cast Member"]  = new(StringComparer.OrdinalIgnoreCase) { "actor", "actress", "film actor", "television actor", "voice actor" },
            ["Illustrator"]  = new(StringComparer.OrdinalIgnoreCase) { "illustrator", "comics artist", "mangaka", "graphic artist", "artist" },
        };

    public PersonReconciliationService(
        IConfigurationLoader configLoader,
        ILogger<PersonReconciliationService> logger,
        WikidataReconciler? reconciler = null)
    {
        ArgumentNullException.ThrowIfNull(configLoader);
        ArgumentNullException.ThrowIfNull(logger);
        _configLoader = configLoader;
        _logger = logger;
        _reconciler = reconciler;
    }

    public async Task<PersonSearchResult?> SearchPersonAsync(
        string name,
        string expectedRole,
        string? workTitle = null,
        CancellationToken ct = default)
    {
        if (_reconciler is null || string.IsNullOrWhiteSpace(name))
            return null;

        var language = _configLoader.LoadCore().Language.Metadata ?? "en";

        // Step 1: Search Wikidata for person candidates.
        var request = new ReconciliationRequest
        {
            Query = name,
            Limit = 10,
            Language = language,
        };

        IReadOnlyList<ReconciliationResult> candidates;
        try
        {
            candidates = await _reconciler.ReconcileAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Person reconciliation search failed for '{Name}' ({Role})", name, expectedRole);
            return null;
        }

        if (candidates.Count == 0)
        {
            _logger.LogDebug("No Wikidata candidates found for person '{Name}' ({Role})", name, expectedRole);
            return null;
        }

        // Step 2: Fetch P31 (instance_of), P106 (occupation), P800 (notable_work) for all candidates.
        var candidateQids = candidates.Select(c => c.Id).Distinct().ToList();
        IReadOnlyList<string> propertiesToFetch = ["P31", "P106", "P800"];

        Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>>> properties;
        try
        {
            properties = await _reconciler.GetPropertiesAsync(candidateQids, propertiesToFetch, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Person property fetch failed for '{Name}' candidates", name);
            return null;
        }

        // Step 3: Score each candidate.
        PersonSearchResult? bestMatch = null;

        foreach (var candidate in candidates)
        {
            if (!properties.TryGetValue(candidate.Id, out var props))
                continue;

            // Filter: must be Q5 (human).
            if (!IsHuman(props))
                continue;

            // Base score from reconciliation (0-100 → 0.0-1.0), weighted at 0.50.
            double baseScore = (candidate.Score / 100.0) * 0.50;

            // Occupation match boost (+0.20).
            double occupationScore = HasMatchingOccupation(props, expectedRole) ? OccupationBoost : 0.0;

            // Notable work match boost (+0.10).
            double notableWorkScore = 0.0;
            if (!string.IsNullOrWhiteSpace(workTitle))
                notableWorkScore = HasMatchingNotableWork(props, workTitle) ? NotableWorkBoost : 0.0;

            double totalScore = baseScore + occupationScore + notableWorkScore;

            _logger.LogDebug(
                "Person candidate {QID} '{CandidateName}' for '{SearchName}' ({Role}): " +
                "base={Base:F2}, occupation={Occ:F2}, notable={Notable:F2}, total={Total:F2}",
                candidate.Id, candidate.Name, name, expectedRole,
                baseScore, occupationScore, notableWorkScore, totalScore);

            if (totalScore >= AutoAcceptThreshold && (bestMatch is null || totalScore > bestMatch.Score))
                bestMatch = new PersonSearchResult(candidate.Id, candidate.Name, totalScore);
        }

        if (bestMatch is not null)
        {
            _logger.LogInformation(
                "Person reconciliation auto-accepted: '{Name}' ({Role}) → {QID} '{WikiName}' (score={Score:F2})",
                name, expectedRole, bestMatch.WikidataQid, bestMatch.Name, bestMatch.Score);
        }
        else
        {
            _logger.LogDebug(
                "Person reconciliation auto-skipped: '{Name}' ({Role}) — no candidate met threshold {Threshold:F2}",
                name, expectedRole, AutoAcceptThreshold);
        }

        return bestMatch;
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    /// <summary>Returns true if the entity's P31 (instance_of) includes Q5 (human).</summary>
    private static bool IsHuman(IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>> props)
    {
        if (!props.TryGetValue("P31", out var p31Claims))
            return false;

        return p31Claims.Any(c =>
            string.Equals(c.Value?.EntityId, HumanClassQid, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns true if P106 (occupation) contains a label matching the expected role.</summary>
    private static bool HasMatchingOccupation(
        IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>> props,
        string expectedRole)
    {
        if (!props.TryGetValue("P106", out var p106Claims))
            return false;

        if (!OccupationsByRole.TryGetValue(expectedRole, out var expectedOccupations))
            return false;

        return p106Claims.Any(c =>
        {
            var label = c.Value?.RawValue;
            return !string.IsNullOrWhiteSpace(label) && expectedOccupations.Contains(label);
        });
    }

    /// <summary>Returns true if P800 (notable_work) contains a label fuzzy-matching the work title.</summary>
    private static bool HasMatchingNotableWork(
        IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>> props,
        string workTitle)
    {
        if (!props.TryGetValue("P800", out var p800Claims))
            return false;

        var normalizedTitle = workTitle.Trim().ToLowerInvariant();

        return p800Claims.Any(c =>
        {
            var label = c.Value?.RawValue;
            if (string.IsNullOrWhiteSpace(label))
                return false;

            var normalizedLabel = label.Trim().ToLowerInvariant();
            // Exact match or containment (handles "Dune" matching "Dune Part One").
            return normalizedLabel == normalizedTitle
                || normalizedLabel.Contains(normalizedTitle)
                || normalizedTitle.Contains(normalizedLabel);
        });
    }
}
