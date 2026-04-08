using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;
using Tuvima.Wikidata;
using TwPersonRole = Tuvima.Wikidata.PersonRole;
using TwPersonSearchRequest = Tuvima.Wikidata.PersonSearchRequest;
using TwPersonSearchResult = Tuvima.Wikidata.PersonSearchResult;

// PersonSearchResult exists in both MediaEngine.Domain.Models and Tuvima.Wikidata
// (the v2.1+ Persons sub-service). The Domain DTO is the public contract this
// service exposes; the library type is only used internally to call the wrapper.
using PersonSearchResult = MediaEngine.Domain.Models.PersonSearchResult;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Standalone person reconciliation service that resolves unlinked person names
/// to Wikidata QIDs by delegating to the Tuvima.Wikidata v2.1+
/// <see cref="Services.PersonsService"/> sub-service. Phase 4 of the adapter
/// slimdown remediation collapses the previous ~440-line hand-rolled
/// implementation into this thin wrapper.
///
/// <para>
/// Three-tier confidence model (unchanged from the legacy version):
///   - Tier 1 (0.90): Structured Wikidata properties (P50, P57, P161, P175)
///   - Tier 2 (0.80): This service — standalone name search with occupation match
///   - Tier 3 (0.75): AI description extraction fallback
/// </para>
///
/// <para>
/// Auto-accept threshold: score &gt;= 0.80. Below that, the person is skipped
/// and retried at the next 30-day refresh cycle. The library's
/// <see cref="TwPersonSearchRequest.AcceptThreshold"/> defaults to 0.80, so we
/// pass the same value through.
/// </para>
///
/// <para>
/// What the library handles natively (no longer in this file):
/// role → P106 occupation mapping (replaces the OccupationsByRole dictionary),
/// musical group inclusion for Performer / Artist roles (replaces the manual
/// Q5 → Q215380 → Q5741069 fallback chain), notable-work P800 boost for the
/// title hint, and the v2.3 fix for the P106-on-musical-groups penalty that
/// previously forced consumers to lower the threshold to ~0.5 for Daft Punk /
/// Radiohead. Daft Punk now passes at the documented default 0.80.
/// </para>
/// </summary>
public sealed class PersonReconciliationService : IPersonReconciliationService
{
    private const double AutoAcceptThreshold = 0.80;

    private readonly WikidataReconciler? _reconciler;
    private readonly IConfigurationLoader _configLoader;
    private readonly ILogger<PersonReconciliationService> _logger;

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

        var language = _configLoader.LoadCore().Language?.Metadata ?? "en";

        TwPersonSearchResult libResult;
        try
        {
            libResult = await _reconciler.Persons.SearchAsync(
                new TwPersonSearchRequest
                {
                    Name            = name,
                    Role            = MapRole(expectedRole),
                    TitleHint       = workTitle,
                    Language        = language,
                    AcceptThreshold = AutoAcceptThreshold,
                    // IncludeMusicalGroups left null — the library defaults
                    // Performer / Artist to true and every other role to false,
                    // which matches the legacy MusicRoles set.
                }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Person reconciliation search failed for '{Name}' ({Role})", name, expectedRole);
            return null;
        }

        if (!libResult.Found || string.IsNullOrWhiteSpace(libResult.Qid))
        {
            _logger.LogDebug(
                "Person reconciliation auto-skipped: '{Name}' ({Role}) — best score {Score:F2} below threshold {Threshold:F2}",
                name, expectedRole, libResult.Score, AutoAcceptThreshold);
            return null;
        }

        var canonicalName = libResult.CanonicalName ?? name;
        _logger.LogInformation(
            "Person reconciliation auto-accepted: '{Name}' ({Role}) → {QID} '{WikiName}' (score={Score:F2})",
            name, expectedRole, libResult.Qid, canonicalName, libResult.Score);

        return new PersonSearchResult(libResult.Qid!, canonicalName, libResult.Score);
    }

    public async Task<IReadOnlyDictionary<string, PersonSearchResult?>> SearchPersonsBatchAsync(
        IReadOnlyList<(string Name, string Role, string? WorkTitle)> requests,
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, PersonSearchResult?>(StringComparer.OrdinalIgnoreCase);
        if (_reconciler is null || requests.Count == 0)
            return results;

        // Deduplicate by name (case-insensitive), keeping the first occurrence's
        // Role and WorkTitle. Mirrors the legacy implementation's contract.
        var seen = new Dictionary<string, (string Name, string Role, string? WorkTitle)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var req in requests)
        {
            if (!string.IsNullOrWhiteSpace(req.Name) && !seen.ContainsKey(req.Name))
                seen[req.Name] = req;
        }

        if (seen.Count == 0) return results;

        // PersonsService does not yet expose a batch entry point — issue the
        // unique requests sequentially. The library's internal ConcurrencyLimiter
        // (default MaxConcurrency = 5) bounds the parallelism if a future
        // version adds Task.WhenAll fan-out here. For now, the deduplication
        // alone is the primary win: 30 tracks by the same artist still cost
        // one round-trip instead of 30.
        foreach (var (personName, role, workTitle) in seen.Values)
        {
            ct.ThrowIfCancellationRequested();
            var result = await SearchPersonAsync(personName, role, workTitle, ct)
                .ConfigureAwait(false);
            results[personName.ToLowerInvariant()] = result;
        }

        return results;
    }

    /// <summary>
    /// Translates the consumer's string-typed role into the library's
    /// <see cref="TwPersonRole"/> enum. Roles outside the known set fall back
    /// to <see cref="TwPersonRole.Unknown"/>, which causes the library to
    /// skip the occupation filter entirely.
    /// </summary>
    private static TwPersonRole MapRole(string expectedRole) => expectedRole switch
    {
        // Match-case insensitive comparison via ToLowerInvariant first.
        _ when string.Equals(expectedRole, "Author",     StringComparison.OrdinalIgnoreCase) => TwPersonRole.Author,
        _ when string.Equals(expectedRole, "Narrator",   StringComparison.OrdinalIgnoreCase) => TwPersonRole.Narrator,
        _ when string.Equals(expectedRole, "Director",   StringComparison.OrdinalIgnoreCase) => TwPersonRole.Director,
        _ when string.Equals(expectedRole, "Actor",      StringComparison.OrdinalIgnoreCase) => TwPersonRole.Actor,
        _ when string.Equals(expectedRole, "VoiceActor", StringComparison.OrdinalIgnoreCase) => TwPersonRole.VoiceActor,
        _ when string.Equals(expectedRole, "Composer",   StringComparison.OrdinalIgnoreCase) => TwPersonRole.Composer,
        _ when string.Equals(expectedRole, "Performer",  StringComparison.OrdinalIgnoreCase) => TwPersonRole.Performer,
        _ when string.Equals(expectedRole, "Artist",     StringComparison.OrdinalIgnoreCase) => TwPersonRole.Artist,
        _ when string.Equals(expectedRole, "Screenwriter", StringComparison.OrdinalIgnoreCase) => TwPersonRole.Screenwriter,
        _ => TwPersonRole.Unknown,
    };
}
