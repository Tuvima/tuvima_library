using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;
using Microsoft.Extensions.Logging;
using Tuvima.WikidataReconciliation;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Batch-verifies pending person signals against Wikidata after an ingestion
/// batch completes.
///
/// <para>
/// Workflow:
/// <list type="number">
///   <item>Load unique (name, role) pairs from <c>pending_person_signals</c>.</item>
///   <item>
///     Reconcile each unique name against Wikidata with a Q5 (human) type
///     constraint using <see cref="WikidataReconciler.ReconcileAsync"/>.
///   </item>
///   <item>
///     Batch-fetch P31 (instance_of) + P106 (occupation) for top candidates via
///     <see cref="WikidataReconciler.GetPropertiesAsync"/>.
///   </item>
///   <item>
///     Validate: P31 must contain Q5 (human) and P106 must intersect the
///     occupation_classes configured for the extracted role in
///     <c>config/signal_extraction.json</c>.
///   </item>
///   <item>
///     For verified persons: call <see cref="IRecursiveIdentityService.EnrichAsync"/>
///     with a <see cref="PersonReference"/> carrying the confirmed QID so a Person
///     record is created and linked to every entity that held a pending signal.
///   </item>
///   <item>
///     Enqueue returned harvest requests for background Wikidata person enrichment
///     (biography, headshot, social links).
///   </item>
///   <item>Clean up all processed pending signals for this (name, role) pair.</item>
/// </list>
/// </para>
///
/// Spec: §3.13 Two-Stage Hydration — person signal batch verification.
/// </summary>
public sealed class PersonSignalVerificationService : IPersonSignalVerificationService
{
    // Wikidata property codes used for human + occupation validation.
    private const string P31InstanceOf  = "P31";
    private const string P106Occupation = "P106";

    // Q5 = human — the required P31 value for any person in Wikidata.
    private const string Q5Human = "Q5";

    private readonly IPendingPersonSignalRepository _pendingRepo;
    private readonly IRecursiveIdentityService _identity;
    private readonly IMetadataHarvestingService _harvesting;
    private readonly IConfigurationLoader _configLoader;
    private readonly WikidataReconciler? _reconciler;
    private readonly ILogger<PersonSignalVerificationService> _logger;

    public PersonSignalVerificationService(
        IPendingPersonSignalRepository pendingRepo,
        IRecursiveIdentityService identity,
        IMetadataHarvestingService harvesting,
        IConfigurationLoader configLoader,
        ILogger<PersonSignalVerificationService> logger,
        WikidataReconciler? reconciler = null)
    {
        ArgumentNullException.ThrowIfNull(pendingRepo);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(harvesting);
        ArgumentNullException.ThrowIfNull(configLoader);
        ArgumentNullException.ThrowIfNull(logger);

        _pendingRepo   = pendingRepo;
        _identity      = identity;
        _harvesting    = harvesting;
        _configLoader  = configLoader;
        _logger        = logger;
        _reconciler    = reconciler;
    }

    // ── IPersonSignalVerificationService ──────────────────────────────────────

    /// <inheritdoc />
    public async Task<int> VerifyPendingSignalsAsync(CancellationToken ct = default)
    {
        // Guard: nothing to do if WikidataReconciler is unavailable (e.g. test environments).
        if (_reconciler is null)
        {
            _logger.LogWarning(
                "{Service}: WikidataReconciler not available — person signal verification skipped",
                nameof(PersonSignalVerificationService));
            return 0;
        }

        var uniquePairs = await _pendingRepo.GetUniqueNameRolePairsAsync(ct).ConfigureAwait(false);
        if (uniquePairs.Count == 0)
            return 0;

        _logger.LogInformation(
            "{Service}: verifying {Count} unique person signal (name, role) pairs",
            nameof(PersonSignalVerificationService), uniquePairs.Count);

        var settings       = LoadSettings();
        var roleOccupations = BuildRoleOccupationMap(settings);
        var minScore       = settings?.GlobalSettings.MinWikidataScore ?? 70;

        int verified = 0;

        foreach (var (name, role) in uniquePairs)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var wasVerified = await VerifyOnePairAsync(
                    name, role, roleOccupations, minScore, ct).ConfigureAwait(false);

                if (wasVerified)
                    verified++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "{Service}: failed to verify person signal: '{Name}' ({Role})",
                    nameof(PersonSignalVerificationService), name, role);

                // Delete signals for this pair so they don't block future batches on a permanent error.
                await SafeDeleteByNameRoleAsync(name, role, ct).ConfigureAwait(false);
            }
        }

        _logger.LogInformation(
            "{Service}: verification complete — {Verified}/{Total} pairs verified",
            nameof(PersonSignalVerificationService), verified, uniquePairs.Count);

        return verified;
    }

    // ── Private: per-pair verification ────────────────────────────────────────

    /// <summary>
    /// Verifies a single (name, role) pair against Wikidata.
    /// Returns <c>true</c> if the person was confirmed and Person records were created.
    /// Always deletes the pending signals for this pair when done (pass or fail).
    /// </summary>
    private async Task<bool> VerifyOnePairAsync(
        string name,
        string role,
        Dictionary<string, List<string>> roleOccupations,
        int minScore,
        CancellationToken ct)
    {
        // 1. Load all pending signals for this (name, role) pair upfront so we can
        //    delete them regardless of whether verification succeeds.
        var matchingSignals = await _pendingRepo
            .GetByNameAndRoleAsync(name, role, ct).ConfigureAwait(false);

        if (matchingSignals.Count == 0)
            return false;

        try
        {
            // 2. Search Wikidata with Q5 (human) type constraint.
            //    ReconcileAsync returns results sorted by score descending.
            var candidates = await _reconciler!.ReconcileAsync(name, "Q5", ct)
                .ConfigureAwait(false);

            if (candidates.Count == 0)
            {
                _logger.LogDebug(
                    "{Service}: no Wikidata candidates for '{Name}' ({Role})",
                    nameof(PersonSignalVerificationService), name, role);
                return false;
            }

            // 3. Pick the top-scoring candidate that meets the minimum score threshold.
            var top = candidates[0];
            if (top.Score < minScore)
            {
                _logger.LogDebug(
                    "{Service}: top candidate for '{Name}' scored {Score} < threshold {Min} — skipping",
                    nameof(PersonSignalVerificationService), name, top.Score, minScore);
                return false;
            }

            var qid = top.Id;

            // 4. Fetch P31 (instance_of) + P106 (occupation) for validation.
            var language   = _configLoader.LoadCore().Language ?? "en";
            var properties = await _reconciler.GetPropertiesAsync(
                [qid], [P31InstanceOf, P106Occupation], language, ct).ConfigureAwait(false);

            if (!properties.TryGetValue(qid, out var entityProps))
            {
                _logger.LogDebug(
                    "{Service}: could not fetch properties for QID {Qid} ('{Name}')",
                    nameof(PersonSignalVerificationService), qid, name);
                return false;
            }

            // 4a. Validate P31 = Q5 (must be a human).
            if (!IsHuman(entityProps))
            {
                _logger.LogDebug(
                    "{Service}: QID {Qid} ('{Name}') failed P31=Q5 human check",
                    nameof(PersonSignalVerificationService), qid, name);
                return false;
            }

            // 4b. Validate P106 (occupation) against the role's configured occupation classes.
            //     If no occupation classes are configured for this role, skip the occupation check —
            //     the P31=Q5 human check is sufficient.
            if (roleOccupations.TryGetValue(role, out var expectedOccupations)
                && expectedOccupations.Count > 0
                && !HasMatchingOccupation(entityProps, expectedOccupations))
            {
                _logger.LogDebug(
                    "{Service}: QID {Qid} ('{Name}', role={Role}) failed P106 occupation check",
                    nameof(PersonSignalVerificationService), qid, name, role);
                return false;
            }

            // 5. Person is verified — create Person records and links for each entity
            //    that held a pending signal for this (name, role) pair.
            //    Group signals by entity so we call EnrichAsync once per entity.
            var signalsByEntity = matchingSignals
                .GroupBy(s => s.EntityId)
                .ToList();

            var personRef = new PersonReference(
                Role:              role,
                Name:              name,
                WikidataQid:       qid,
                IsCollectivePseudonym: false);

            foreach (var group in signalsByEntity)
            {
                ct.ThrowIfCancellationRequested();

                var entityId = group.Key;

                try
                {
                    // EnrichAsync creates the Person record and links it to the entity.
                    // It returns harvest requests for persons that still need enrichment.
                    var harvestRequests = await _identity
                        .EnrichAsync(entityId, [personRef], ct).ConfigureAwait(false);

                    // Enqueue harvest requests for background Wikidata person enrichment
                    // (biography, headshot, social links — deferred enrichment).
                    foreach (var harvestRequest in harvestRequests)
                    {
                        await _harvesting.EnqueueAsync(harvestRequest, ct).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "{Service}: failed to enrich entity {EntityId} for person '{Name}' (QID {Qid})",
                        nameof(PersonSignalVerificationService), entityId, name, qid);
                    // Continue with remaining entities — one failure must not block the rest.
                }
            }

            _logger.LogInformation(
                "{Service}: verified '{Name}' ({Role}) → QID {Qid} (score={Score}), {EntityCount} entities linked",
                nameof(PersonSignalVerificationService), name, role, qid, top.Score, signalsByEntity.Count);

            return true;
        }
        finally
        {
            // 6. Always clean up processed signals, whether verification succeeded or failed.
            await SafeDeleteByNameRoleAsync(name, role, ct).ConfigureAwait(false);
        }
    }

    // ── Private: Wikidata property checks ────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when the entity's P31 (instance_of) claims contain Q5 (human).
    /// Uses the library's <c>WikidataClaim.Value.EntityId</c> to extract QID values.
    /// </summary>
    private static bool IsHuman(IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>> props)
    {
        if (!props.TryGetValue(P31InstanceOf, out var p31Claims))
            return false;

        foreach (var claim in p31Claims)
        {
            var entityId = claim.Value?.EntityId;
            if (string.Equals(entityId, Q5Human, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when any P106 (occupation) claim value is contained in
    /// <paramref name="expectedOccupations"/> (Wikidata QIDs configured per role).
    /// </summary>
    private static bool HasMatchingOccupation(
        IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>> props,
        IReadOnlyList<string> expectedOccupations)
    {
        if (!props.TryGetValue(P106Occupation, out var p106Claims))
            return false;

        var occupationSet = new HashSet<string>(
            expectedOccupations, StringComparer.OrdinalIgnoreCase);

        foreach (var claim in p106Claims)
        {
            var entityId = claim.Value?.EntityId;
            if (!string.IsNullOrEmpty(entityId) && occupationSet.Contains(entityId))
                return true;
        }

        return false;
    }

    // ── Private: config helpers ───────────────────────────────────────────────

    /// <summary>
    /// Builds a mapping of role name → list of Wikidata occupation QIDs
    /// from all categories in the signal extraction config.
    /// </summary>
    private static Dictionary<string, List<string>> BuildRoleOccupationMap(
        SignalExtractionSettings? settings)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (settings is null)
            return map;

        foreach (var (_, category) in settings.Categories)
        {
            foreach (var rule in category.ExtractionRules)
            {
                if (string.IsNullOrWhiteSpace(rule.Role))
                    continue;

                if (!map.TryGetValue(rule.Role, out var list))
                {
                    list = [];
                    map[rule.Role] = list;
                }

                list.AddRange(rule.OccupationClasses);
            }
        }

        return map;
    }

    private SignalExtractionSettings? LoadSettings()
    {
        try
        {
            return _configLoader.LoadConfig<SignalExtractionSettings>("", "signal_extraction");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "{Service}: could not load signal_extraction.json — using defaults",
                nameof(PersonSignalVerificationService));
            return null;
        }
    }

    // ── Private: cleanup helper ───────────────────────────────────────────────

    /// <summary>
    /// Deletes all pending signals for a (name, role) pair.
    /// Swallows exceptions — cleanup failures must not propagate to the caller.
    /// </summary>
    private async Task SafeDeleteByNameRoleAsync(
        string name, string role, CancellationToken ct)
    {
        try
        {
            var signals = await _pendingRepo
                .GetByNameAndRoleAsync(name, role, ct).ConfigureAwait(false);

            if (signals.Count > 0)
            {
                var ids = signals.Select(s => s.Id).ToList();
                await _pendingRepo.DeleteByIdsAsync(ids, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "{Service}: failed to clean up signals for '{Name}' ({Role})",
                nameof(PersonSignalVerificationService), name, role);
        }
    }
}
