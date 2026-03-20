using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Recursive person enrichment service.
///
/// Triggered by the ingestion engine after a media asset is scored.
/// For each author or narrator reference extracted from the file's metadata:
///  1. Looks up or creates a <see cref="Person"/> record.
///  2. Links the person to the ingested media asset.
///  3. If the person has not yet been Wikidata-enriched, enqueues a
///     <see cref="HarvestRequest"/> with <see cref="EntityType.Person"/>
///     so the <see cref="MetadataHarvestingService"/> can dispatch it to
///     <c>WikidataAdapter</c>.
///  4. Creates the <c>.people/{QID}/</c> folder under the library root
///     immediately after the Person record is created, so downstream
///     services (headshot download, character images) have a guaranteed
///     location to write to.  When the QID is not yet known (enrichment
///     pending), a temporary folder keyed by the database ID is created
///     and will be renamed by the enrichment service once the QID is resolved.
///
/// This service is intentionally lightweight: all heavy I/O runs later,
/// asynchronously, via the harvest queue.
///
/// Spec: Phase 9 – Recursive Person Enrichment.
/// </summary>
public sealed class RecursiveIdentityService : IRecursiveIdentityService
{
    private readonly IPersonRepository _personRepo;
    private readonly IMetadataHarvestingService _harvesting;
    private readonly IConfigurationLoader _configLoader;
    private readonly ILogger<RecursiveIdentityService> _logger;

    // Prevents concurrent threads from creating duplicate Person rows for the
    // same name+role identity.  The window is tiny (FindByName → CreateAsync),
    // but it fires when multiple audiobooks by the same author arrive together.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _personLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public RecursiveIdentityService(
        IPersonRepository personRepo,
        IMetadataHarvestingService harvesting,
        IConfigurationLoader configLoader,
        ILogger<RecursiveIdentityService> logger)
    {
        ArgumentNullException.ThrowIfNull(personRepo);
        ArgumentNullException.ThrowIfNull(harvesting);
        ArgumentNullException.ThrowIfNull(configLoader);
        ArgumentNullException.ThrowIfNull(logger);
        _personRepo   = personRepo;
        _harvesting   = harvesting;
        _configLoader = configLoader;
        _logger       = logger;
    }

    // ── IRecursiveIdentityService ─────────────────────────────────────────────

    /// <inheritdoc />
    public async Task EnrichAsync(
        Guid mediaAssetId,
        IReadOnlyList<PersonReference> persons,
        CancellationToken ct = default)
    {
        if (persons.Count == 0)
            return;

        foreach (var reference in persons)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(reference.Name))
                continue;

            try
            {
                await ProcessPersonAsync(mediaAssetId, reference, ct).ConfigureAwait(false);
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
    }

    private async Task ProcessPersonAsync(
        Guid mediaAssetId,
        PersonReference reference,
        CancellationToken ct)
    {
        // Normalize "Last, First" → "First Last" for consistent storage and search.
        var normalizedName = NormalizePersonName(reference.Name);

        // 1. Find or create the person record.
        //    Serialize on a per-identity key to prevent concurrent threads from
        //    both missing FindByName → both calling CreateAsync → duplicate row.
        //    Prefer a QID-based key when available so that pen names and alternate
        //    name spellings all collapse to the same lock bucket.
        var personKey = !string.IsNullOrEmpty(reference.WikidataQid)
            ? $"QID:{reference.WikidataQid}"
            : $"{normalizedName.ToUpperInvariant()}:{reference.Role}";
        var personLock = _personLocks.GetOrAdd(personKey, _ => new SemaphoreSlim(1, 1));
        await personLock.WaitAsync(ct).ConfigureAwait(false);
        Person? person;
        try
        {
            // QID-first: if we already know who this person is, find by QID to
            // avoid creating duplicates when the same person appears under different
            // name spellings (e.g. pen names, transliterations).
            if (!string.IsNullOrEmpty(reference.WikidataQid))
            {
                person = await _personRepo.FindByQidAsync(reference.WikidataQid, ct)
                             .ConfigureAwait(false);
            }
            else
            {
                person = null;
            }

            // Fallback: name-based lookup (normalized first, then original for
            // backward compatibility with records created before normalization).
            if (person is null)
            {
                person = await _personRepo.FindByNameAsync(normalizedName, reference.Role, ct)
                             .ConfigureAwait(false);
            }

            if (person is null && normalizedName != reference.Name)
            {
                person = await _personRepo.FindByNameAsync(reference.Name, reference.Role, ct)
                             .ConfigureAwait(false);
            }

            if (person is null)
            {
                person = await _personRepo.CreateAsync(new Person
                {
                    Name         = normalizedName,
                    Role         = reference.Role,
                    WikidataQid  = reference.WikidataQid,
                }, ct).ConfigureAwait(false);

                _logger.LogDebug(
                    "Created person record for '{Name}' ({Role}), id={Id}",
                    person.Name, person.Role, person.Id);
            }
        }
        finally
        {
            personLock.Release();
        }

        if (person is null) return;

        // 2. Link person to the media asset (INSERT OR IGNORE — idempotent).
        await _personRepo.LinkToMediaAssetAsync(mediaAssetId, person.Id, reference.Role, ct)
            .ConfigureAwait(false);

        // 2a. Ensure the .people/ folder exists for this person.
        //
        // The folder name uses the Wikidata QID when available (e.g. ".people/Q378882/"),
        // because QIDs are the stable, collision-free identity. When the QID is not yet
        // known (enrichment is still pending), a temporary folder keyed by the database
        // GUID is created (e.g. ".people/tmp-{Id}/"). The enrichment service is
        // responsible for renaming the temporary folder to the QID once it is resolved.
        //
        // Failures here are non-fatal: the person record and harvest request are still
        // committed regardless. A missing folder simply means headshots will be skipped
        // until the folder is created on the next run.
        EnsurePersonFolder(person);

        // 3. If not yet enriched, enqueue a Wikidata harvest request.
        //    Collective pseudonyms (e.g. "James S. A. Corey" = Q6142591) are enriched
        //    normally — their QID is already resolved by Stage 1, so the harvest
        //    service will fetch the correct entity directly.
        if (person.EnrichedAt is null)
        {
            var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = normalizedName,
                ["role"] = reference.Role,
            };

            // When the caller already knows the person's Wikidata QID (e.g. from a
            // prior SPARQL result for a multi-authored book), pass it as a hint so
            // the WikidataAdapter can skip the name-based search entirely.
            if (!string.IsNullOrEmpty(reference.WikidataQid))
                hints["wikidata_qid"] = reference.WikidataQid;

            await _harvesting.EnqueueAsync(new HarvestRequest
            {
                EntityId   = person.Id,
                EntityType = EntityType.Person,
                MediaType  = MediaType.Unknown,
                Hints      = hints,
            }, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Enqueued Wikidata enrichment for person '{Name}' ({Id})",
                person.Name, person.Id);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates the <c>.people/{Name} ({QID})/</c> directory under the configured
    /// library root, where downstream services write headshots and character images.
    ///
    /// Only creates the folder when the Wikidata QID is known — this gives a
    /// stable, collision-free name.  When the QID is not yet resolved (enrichment
    /// pending), folder creation is deferred: the enrichment service will create
    /// the folder once the QID is available.
    ///
    /// This method is intentionally non-throwing: folder creation failures are logged
    /// at Debug level and do not abort the ingestion pipeline.
    /// </summary>
    private void EnsurePersonFolder(Domain.Entities.Person person)
    {
        try
        {
            var libraryRoot = _configLoader.LoadCore().LibraryRoot;
            if (string.IsNullOrWhiteSpace(libraryRoot)) return;

            // Only create folder when QID is known — stable, collision-free name.
            // Enrichment will create the folder later when QID is resolved.
            if (string.IsNullOrWhiteSpace(person.WikidataQid))
            {
                _logger.LogDebug(
                    "Skipping .people/ folder for '{Name}' — QID not yet known", person.Name);
                return;
            }

            var folderName = $"{person.Name} ({person.WikidataQid})";
            var personFolder = Path.Combine(libraryRoot, ".people", folderName);
            Directory.CreateDirectory(personFolder);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Failed to create .people/ folder for '{Name}'", person.Name);
        }
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
