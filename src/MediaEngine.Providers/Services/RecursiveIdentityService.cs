using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Providers.Services;

/// <summary>
/// QID-first person enrichment service.
///
/// Triggered by the hydration pipeline after Stage 1 (Reconciliation) resolves
/// person QIDs from Wikidata Data Extension properties (P50 author, P57 director,
/// P161 cast, P2093 narrator).
///
/// Only creates Person records for references that carry a confirmed Wikidata QID.
/// References without a QID (e.g. from raw file metadata before Wikidata match)
/// are silently skipped — no Person record is created until the media item has
/// a confirmed Wikidata identity.
///
/// For each QID-backed person reference:
///  1. Looks up or creates a <see cref="Person"/> record (QID-first lookup).
///  2. Links the person to the ingested media asset.
///  3. If the person has not yet been Wikidata-enriched, returns a
///     <see cref="HarvestRequest"/> with <see cref="EntityType.Person"/>
///     so the caller can decide whether to process it synchronously or
///     enqueue it for background processing.
///  4. Creates the <c>.people/{QID}/</c> folder under the library root.
///
/// This service is intentionally lightweight: all heavy I/O runs later,
/// either synchronously (during review resolution) or asynchronously
/// (via the harvest queue during normal ingestion).
///
/// Spec: Phase 9 – Recursive Person Enrichment (QID-first).
/// </summary>
public sealed class RecursiveIdentityService : IRecursiveIdentityService
{
    private readonly IPersonRepository _personRepo;
    private readonly ILogger<RecursiveIdentityService> _logger;

    // Prevents concurrent threads from creating duplicate Person rows for the
    // same name+role identity.  The window is tiny (FindByName → CreateAsync),
    // but it fires when multiple audiobooks by the same author arrive together.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _personLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public RecursiveIdentityService(
        IPersonRepository personRepo,
        ILogger<RecursiveIdentityService> logger)
    {
        ArgumentNullException.ThrowIfNull(personRepo);
        ArgumentNullException.ThrowIfNull(logger);
        _personRepo = personRepo;
        _logger     = logger;
    }

    // ── IRecursiveIdentityService ─────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<HarvestRequest>> EnrichAsync(
        Guid mediaAssetId,
        IReadOnlyList<PersonReference> persons,
        CancellationToken ct = default)
    {
        if (persons.Count == 0)
            return Array.Empty<HarvestRequest>();

        var pendingRequests = new List<HarvestRequest>();

        foreach (var reference in persons)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(reference.Name) &&
                string.IsNullOrWhiteSpace(reference.WikidataQid))
            {
                continue;
            }

            // QID-first: only create Person records for references with a confirmed
            // Wikidata QID.  Name-only references (from file metadata before Wikidata
            // match) are skipped — the canonical value still shows the author name on
            // the media card, but no Person entity is created until the QID is known.
            if (string.IsNullOrWhiteSpace(reference.WikidataQid))
            {
                _logger.LogDebug(
                    "Skipping person '{Name}' ({Role}) for asset {AssetId} — no QID",
                    reference.Name, reference.Role, mediaAssetId);
                continue;
            }

            try
            {
                var request = await ProcessPersonAsync(mediaAssetId, reference, ct).ConfigureAwait(false);
                if (request is not null)
                    pendingRequests.Add(request);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A failure for one person must not prevent processing the rest.
                _logger.LogWarning(ex,
                    "Failed to process person '{Name}' ({Role}) for asset {AssetId}",
                    reference.Name, reference.Role, mediaAssetId);
            }
        }

        return pendingRequests;
    }

    private async Task<HarvestRequest?> ProcessPersonAsync(
        Guid mediaAssetId,
        PersonReference reference,
        CancellationToken ct)
    {
        // Normalize "Last, First" → "First Last" for consistent storage and search.
        var normalizedName = string.IsNullOrWhiteSpace(reference.Name)
            ? reference.WikidataQid!
            : NormalizePersonName(reference.Name);

        // QID is guaranteed non-null by EnrichAsync's guard above.
        // Serialize on QID to prevent concurrent threads from both missing
        // FindByQid → both calling CreateAsync → duplicate row.
        var personKey = $"QID:{reference.WikidataQid}";
        var personLock = _personLocks.GetOrAdd(personKey, _ => new SemaphoreSlim(1, 1));
        await personLock.WaitAsync(ct).ConfigureAwait(false);
        Person? person;
        try
        {
            // Primary lookup: find by QID (guaranteed present).
            person = await _personRepo.FindByQidAsync(reference.WikidataQid!, ct)
                         .ConfigureAwait(false);

            if (person is not null)
            {
                // Add role to existing person if not already present.
                await _personRepo.AddRoleAsync(person.Id, reference.Role, ct).ConfigureAwait(false);
            }
            else
            {
                person = await _personRepo.CreateAsync(new Person
                {
                    Name         = normalizedName,
                    Roles        = [reference.Role],
                    WikidataQid  = reference.WikidataQid,
                }, ct).ConfigureAwait(false);

                _logger.LogDebug(
                    "Created person record for '{Name}' ({Role}, {Qid}), id={Id}",
                    person.Name, reference.Role, reference.WikidataQid, person.Id);
            }
        }
        finally
        {
            personLock.Release();
        }

        if (person is null) return null;

        // 2. Link person to the media asset (INSERT OR IGNORE — idempotent).
        await _personRepo.LinkToMediaAssetAsync(mediaAssetId, person.Id, reference.Role, ct)
            .ConfigureAwait(false);

        // 2a. Ensure the .people/ folder exists for this person.
        EnsurePersonFolder(person);

        // 3. If not yet enriched, return a harvest request for the caller to handle.
        //    QID is always present — the harvest service uses it directly for
        //    Data Extension property fetching (no name-based search needed).
        if (person.EnrichedAt is null)
        {
            var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = normalizedName,
                ["role"] = reference.Role,
                ["wikidata_qid"] = reference.WikidataQid!,
            };

            var harvestRequest = new HarvestRequest
            {
                EntityId   = person.Id,
                EntityType = EntityType.Person,
                MediaType  = MediaType.Unknown,
                Hints      = hints,
            };

            _logger.LogDebug(
                "Person '{Name}' ({Id}) needs enrichment — returning harvest request",
                person.Name, person.Id);

            return harvestRequest;
        }

        return null;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates the <c>.people/{Name} ({QID})/</c> directory under the configured
    /// library root. Directory creation is deferred to the enrichment service
    /// (MetadataHarvestingService.PersistPersonStorageAsync), which creates the
    /// person image directory only when a headshot image is actually saved.
    /// This prevents empty directories from accumulating for unenriched persons.
    ///
    /// This method is intentionally a no-op: it exists as a hook for callers
    /// that previously triggered folder creation at entity-creation time.
    /// </summary>
    private void EnsurePersonFolder(Domain.Entities.Person person)
    {
        // No-op: person image directory is created lazily when a headshot is downloaded,
        // not at entity-creation time. See MetadataHarvestingService.PersistPersonStorageAsync.
    }

    /// <summary>
    /// Normalizes author names from "Last, First" to "First Last" format.
    /// If the name contains exactly one comma followed by a space, it's assumed
    /// to be in "Last, First" bibliographic format and is reversed.
    /// Names with multiple commas, no comma, or no space after the comma are returned as-is.
    /// </summary>
    internal static string NormalizePersonName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        var trimmed = name.Trim();

        // Only normalize if there's exactly one comma.
        var commaIndex = trimmed.IndexOf(',');
        if (commaIndex < 0 || commaIndex != trimmed.LastIndexOf(','))
            return trimmed;

        var last  = trimmed[..commaIndex].Trim();
        var first = trimmed[(commaIndex + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last))
            return trimmed;

        return $"{first} {last}";
    }
}
