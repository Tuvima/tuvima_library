using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;

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
    private readonly ILogger<RecursiveIdentityService> _logger;

    public RecursiveIdentityService(
        IPersonRepository personRepo,
        IMetadataHarvestingService harvesting,
        ILogger<RecursiveIdentityService> logger)
    {
        ArgumentNullException.ThrowIfNull(personRepo);
        ArgumentNullException.ThrowIfNull(harvesting);
        ArgumentNullException.ThrowIfNull(logger);
        _personRepo = personRepo;
        _harvesting = harvesting;
        _logger     = logger;
    }

    // ── IRecursiveIdentityService ─────────────────────────────────────────────

    /// <inheritdoc/>
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

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task ProcessPersonAsync(
        Guid mediaAssetId,
        PersonReference reference,
        CancellationToken ct)
    {
        // Normalize "Last, First" → "First Last" for consistent storage and search.
        var normalizedName = NormalizePersonName(reference.Name);

        // 1. Find or create the person record.
        //    Try the normalized name first; fall back to the original name for
        //    backward compatibility with records created before normalization.
        var person = await _personRepo.FindByNameAsync(normalizedName, reference.Role, ct)
                     .ConfigureAwait(false);

        if (person is null && normalizedName != reference.Name)
        {
            person = await _personRepo.FindByNameAsync(reference.Name, reference.Role, ct)
                     .ConfigureAwait(false);
        }

        if (person is null)
        {
            person = await _personRepo.CreateAsync(new Person
            {
                Name = normalizedName,
                Role = reference.Role,
            }, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Created person record for '{Name}' ({Role}), id={Id}",
                person.Name, person.Role, person.Id);
        }

        // 2. Link person to the media asset (INSERT OR IGNORE — idempotent).
        await _personRepo.LinkToMediaAssetAsync(mediaAssetId, person.Id, reference.Role, ct)
            .ConfigureAwait(false);

        // 3. If not yet enriched, enqueue a Wikidata harvest request.
        if (person.EnrichedAt is null)
        {
            await _harvesting.EnqueueAsync(new HarvestRequest
            {
                EntityId   = person.Id,
                EntityType = EntityType.Person,
                MediaType  = MediaType.Unknown,
                Hints      = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["name"] = normalizedName,
                    ["role"] = reference.Role,
                },
            }, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Enqueued Wikidata enrichment for person '{Name}' ({Id})",
                person.Name, person.Id);
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
