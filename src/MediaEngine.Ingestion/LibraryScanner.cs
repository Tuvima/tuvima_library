using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Ingestion;

/// <summary>
/// Recursively scans a Library Root directory for <c>library.xml</c> sidecar files
/// and uses them to hydrate (or restore) the database — the "Great Inhale".
///
/// <para>
/// <b>XML always wins:</b> when the sidecar and the database disagree on a canonical
/// value, the sidecar value is applied. This makes the filesystem the authoritative
/// source of truth.
/// </para>
///
/// <para>
/// <b>Scope:</b>
/// <list type="bullet">
///   <item>Hub-level XML (<c>&lt;library-hub&gt;</c>) — creates or updates Hub records.</item>
///   <item>Edition-level XML (<c>&lt;library-edition&gt;</c>) — upserts canonical values for
///     any MediaAsset already in the database (matched by content hash). Re-inserts
///     user-locked claims that have been lost from <c>metadata_claims</c>.</item>
/// </list>
/// Full MediaAsset creation from scratch (after a complete DB wipe) requires a future
/// Work/Edition repository layer and a separate ingestion pass.
/// </para>
///
/// <para>
/// No file hashing or metadata extraction is performed — the scan reads XML only,
/// making it orders of magnitude faster than a full ingestion pass.
/// </para>
/// </summary>
public sealed class LibraryScanner : ILibraryScanner
{
    private readonly ISidecarWriter             _sidecar;
    private readonly IHubRepository             _hubRepo;
    private readonly IMediaAssetRepository      _assetRepo;
    private readonly ICanonicalValueRepository  _canonicalRepo;
    private readonly IMetadataClaimRepository   _claimRepo;
    private readonly IPersonRepository          _personRepo;
    private readonly ILogger<LibraryScanner>    _logger;

    // Stable GUID representing the library-scanner as a "provider" when re-inserting
    // canonical values from the sidecar. Distinct from the local-processor GUID
    // so the claim source is distinguishable in the claims table.
    private static readonly Guid LibraryScannerProviderId =
        new("c9d8e7f6-a5b4-4321-fedc-0102030405c9");

    public LibraryScanner(
        ISidecarWriter            sidecar,
        IHubRepository            hubRepo,
        IMediaAssetRepository     assetRepo,
        ICanonicalValueRepository canonicalRepo,
        IMetadataClaimRepository  claimRepo,
        IPersonRepository         personRepo,
        ILogger<LibraryScanner>   logger)
    {
        _sidecar       = sidecar;
        _hubRepo       = hubRepo;
        _assetRepo     = assetRepo;
        _canonicalRepo = canonicalRepo;
        _claimRepo     = claimRepo;
        _personRepo    = personRepo;
        _logger        = logger;
    }

    /// <inheritdoc/>
    public async Task<LibraryScanResult> ScanAsync(
        string libraryRoot,
        CancellationToken ct = default)
    {
        var sw               = Stopwatch.StartNew();
        int hubsUpserted     = 0;
        int editionsUpserted = 0;
        int errors           = 0;

        _logger.LogInformation(
            "Great Inhale started. Library root: {LibraryRoot}", libraryRoot);

        var xmlFiles = Directory.EnumerateFiles(
                libraryRoot, "library.xml", SearchOption.AllDirectories);

        foreach (var xmlPath in xmlFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Peek at the root element name to determine sidecar type.
                var rootName = PeekRootName(xmlPath);

                if (rootName is "library-hub")
                {
                    if (await HydrateHubAsync(xmlPath, ct).ConfigureAwait(false))
                        hubsUpserted++;
                }
                else if (rootName is "library-edition")
                {
                    if (await HydrateEditionAsync(xmlPath, ct).ConfigureAwait(false))
                        editionsUpserted++;
                }
                else
                {
                    _logger.LogDebug(
                        "Skipping unknown sidecar root '{Root}' at {Path}",
                        rootName, xmlPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Great Inhale: error processing {XmlPath}", xmlPath);
                errors++;
            }
        }

        sw.Stop();
        _logger.LogInformation(
            "Great Inhale complete — hubs: {Hubs}, editions: {Editions}, errors: {Errors}, elapsed: {Elapsed}ms",
            hubsUpserted, editionsUpserted, errors, (long)sw.Elapsed.TotalMilliseconds);

        return new LibraryScanResult
        {
            HubsUpserted     = hubsUpserted,
            EditionsUpserted = editionsUpserted,
            Errors           = errors,
            Elapsed          = sw.Elapsed,
        };
    }

    // -------------------------------------------------------------------------
    // People scanning (Great Inhale person recovery)
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<int> ScanPeopleAsync(
        string libraryRoot,
        CancellationToken ct = default)
    {
        var peopleRoot = Path.Combine(libraryRoot, ".people");
        if (!Directory.Exists(peopleRoot))
        {
            _logger.LogDebug("No .people/ directory found; skipping people scan");
            return 0;
        }

        int upserted = 0;
        _logger.LogInformation("Great Inhale: scanning .people/ for person recovery");

        foreach (var subDir in Directory.GetDirectories(peopleRoot))
        {
            ct.ThrowIfCancellationRequested();

            var personXml = Path.Combine(subDir, "person.xml");
            if (!File.Exists(personXml))
                continue;

            try
            {
                var doc = XDocument.Load(personXml);
                if (doc.Root?.Name.LocalName != "library-person")
                    continue;

                var identity = doc.Root.Element("identity");
                if (identity is null)
                    continue;

                var idText     = identity.Element("id")?.Value;
                var name       = identity.Element("name")?.Value;
                var qid        = identity.Element("wikidata-qid")?.Value;
                var role       = identity.Element("role")?.Value ?? "Author";
                var occupation = identity.Element("occupation")?.Value;

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var personId = Guid.TryParse(idText, out var parsed) ? parsed : Guid.NewGuid();

                // Resolve by QID first (most stable), then by name+role, then by ID.
                Person? existing = null;
                if (!string.IsNullOrWhiteSpace(qid))
                    existing = await _personRepo.FindByQidAsync(qid, ct).ConfigureAwait(false);
                existing ??= await _personRepo.FindByNameAsync(name, role, ct).ConfigureAwait(false);
                existing ??= await _personRepo.FindByIdAsync(personId, ct).ConfigureAwait(false);

                if (existing is null)
                {
                    // Create a new person record.
                    var person = new Person
                    {
                        Id          = personId,
                        Name        = name,
                        Role        = role,
                        WikidataQid = string.IsNullOrWhiteSpace(qid) ? null : qid,
                        Occupation  = string.IsNullOrWhiteSpace(occupation) ? null : occupation,
                    };

                    // Read optional social/biography fields.
                    person.Biography = doc.Root.Element("biography")?.Value;
                    var social = doc.Root.Element("social");
                    if (social is not null)
                    {
                        person.Instagram = NullIfEmpty(social.Element("instagram")?.Value);
                        person.Twitter   = NullIfEmpty(social.Element("twitter")?.Value);
                        person.TikTok    = NullIfEmpty(social.Element("tiktok")?.Value);
                        person.Mastodon  = NullIfEmpty(social.Element("mastodon")?.Value);
                        person.Website   = NullIfEmpty(social.Element("website")?.Value);
                    }

                    // Check if headshot exists on disk.
                    var headshotPath = Path.Combine(subDir, "headshot.jpg");
                    if (File.Exists(headshotPath))
                        person.LocalHeadshotPath = headshotPath;

                    await _personRepo.CreateAsync(person, ct).ConfigureAwait(false);
                    existing = person;
                }

                // Rebuild person_media_links by matching known-names against
                // canonical author/narrator values across all assets.
                var knownNames = doc.Root.Element("known-names")?
                    .Elements("name")
                    .Select(e => e.Value.Trim())
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                    ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Always include the primary name.
                knownNames.Add(name);

                // Search for media assets with matching author/narrator canonical values.
                foreach (var knownName in knownNames)
                {
                    var matchingAssets = await _canonicalRepo.FindByValueAsync(
                        "author", knownName, ct).ConfigureAwait(false);
                    foreach (var assetId in matchingAssets)
                    {
                        await _personRepo.LinkToMediaAssetAsync(
                            assetId, existing.Id, role, ct).ConfigureAwait(false);
                    }

                    // Also try narrator matches.
                    var narratorAssets = await _canonicalRepo.FindByValueAsync(
                        "narrator", knownName, ct).ConfigureAwait(false);
                    foreach (var assetId in narratorAssets)
                    {
                        await _personRepo.LinkToMediaAssetAsync(
                            assetId, existing.Id, role, ct).ConfigureAwait(false);
                    }
                }

                upserted++;
                _logger.LogDebug(
                    "Person recovered: '{Name}' (QID={Qid})", name, qid ?? "none");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Great Inhale: error processing person.xml at {Path}", personXml);
            }
        }

        _logger.LogInformation("Great Inhale: recovered {Count} person record(s)", upserted);
        return upserted;
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    // -------------------------------------------------------------------------
    // Hub hydration
    // -------------------------------------------------------------------------

    private async Task<bool> HydrateHubAsync(string xmlPath, CancellationToken ct)
    {
        var data = await _sidecar.ReadHubSidecarAsync(xmlPath, ct).ConfigureAwait(false);
        if (data is null || string.IsNullOrWhiteSpace(data.DisplayName))
        {
            _logger.LogDebug("Skipping hub sidecar with no display name: {Path}", xmlPath);
            return false;
        }

        // Find existing hub by display name to avoid duplicates.
        var existing = await _hubRepo.FindByDisplayNameAsync(data.DisplayName, ct)
                                      .ConfigureAwait(false);

        var hub = existing ?? new Hub
        {
            Id        = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // XML always wins for the display name.
        hub.DisplayName = data.DisplayName;

        await _hubRepo.UpsertAsync(hub, ct).ConfigureAwait(false);

        _logger.LogDebug(
            "Hub '{Name}' upserted (id={Id})", hub.DisplayName, hub.Id);

        return true;
    }

    // -------------------------------------------------------------------------
    // Edition hydration
    // -------------------------------------------------------------------------

    private async Task<bool> HydrateEditionAsync(string xmlPath, CancellationToken ct)
    {
        var data = await _sidecar.ReadEditionSidecarAsync(xmlPath, ct).ConfigureAwait(false);
        if (data is null || string.IsNullOrWhiteSpace(data.ContentHash))
        {
            _logger.LogDebug(
                "Skipping edition sidecar with no content hash: {Path}", xmlPath);
            return false;
        }

        // Look up the MediaAsset by its hash — the permanent identity key.
        var asset = await _assetRepo.FindByHashAsync(data.ContentHash, ct)
                                     .ConfigureAwait(false);

        if (asset is null)
        {
            // Asset is not in the DB. A full ingestion pass is needed to restore it.
            // Great Inhale cannot create the Hub → Work → Edition → MediaAsset hierarchy
            // without Work/Edition repositories (pre-existing Phase 7 gap).
            _logger.LogDebug(
                "Edition sidecar references unknown hash {Hash} — asset not in DB; " +
                "run a full ingestion pass to restore it. ({Path})",
                data.ContentHash[..Math.Min(12, data.ContentHash.Length)], xmlPath);
            return false;
        }

        // Upsert canonical values from the sidecar. XML wins over DB.
        var canonicals = BuildCanonicalValues(asset.Id, data);
        if (canonicals.Count > 0)
            await _canonicalRepo.UpsertBatchAsync(canonicals, ct).ConfigureAwait(false);

        // Re-insert user-locked claims that may have been lost from metadata_claims.
        if (data.UserLocks.Count > 0)
            await ReinsertUserLocksAsync(asset.Id, data.UserLocks, ct).ConfigureAwait(false);

        _logger.LogDebug(
            "Edition hydrated — hash={Hash}, {CanonicalCount} canonicals, {LockCount} locks",
            data.ContentHash[..Math.Min(12, data.ContentHash.Length)],
            canonicals.Count, data.UserLocks.Count);

        return true;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads only the root element name of an XML file without loading the full
    /// document, making sidecar type detection extremely fast.
    /// </summary>
    private static string? PeekRootName(string xmlPath)
    {
        try
        {
            using var reader = System.Xml.XmlReader.Create(xmlPath,
                new System.Xml.XmlReaderSettings { IgnoreWhitespace = true });

            while (reader.Read())
            {
                if (reader.NodeType == System.Xml.XmlNodeType.Element)
                    return reader.LocalName;
            }
        }
        catch { /* unreadable file — caller increments errors */ }
        return null;
    }

    /// <summary>
    /// Builds canonical value records from the sidecar's identity fields.
    /// Only fields with non-empty values are included.
    /// </summary>
    private static IReadOnlyList<CanonicalValue> BuildCanonicalValues(
        Guid assetId, EditionSidecarData data)
    {
        var now    = DateTimeOffset.UtcNow;
        var result = new List<CanonicalValue>(6);

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                result.Add(new CanonicalValue
                {
                    EntityId     = assetId,
                    Key          = key,
                    Value        = value,
                    LastScoredAt = now,
                });
        }

        Add("title",      data.Title);
        Add("author",     data.Author);
        Add("media_type", data.MediaType);
        Add("isbn",       data.Isbn);
        Add("asin",       data.Asin);

        return result;
    }

    /// <summary>
    /// Inserts user-locked claims into <c>metadata_claims</c> if they are not
    /// already present with <c>is_user_locked = 1</c>.
    /// Checks existing claims to avoid duplicating locked rows.
    /// </summary>
    private async Task ReinsertUserLocksAsync(
        Guid assetId,
        IReadOnlyList<UserLockedClaim> locks,
        CancellationToken ct)
    {
        // Load existing locked claims so we don't re-insert duplicates.
        var existing = await _claimRepo.GetByEntityAsync(assetId, ct).ConfigureAwait(false);
        var lockedKeys = existing
            .Where(c => c.IsUserLocked)
            .Select(c => c.ClaimKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toInsert = locks
            .Where(ul => !lockedKeys.Contains(ul.Key)
                         && !string.IsNullOrWhiteSpace(ul.Key)
                         && !string.IsNullOrWhiteSpace(ul.Value))
            .Select(ul => new MetadataClaim
            {
                Id           = Guid.NewGuid(),
                EntityId     = assetId,
                ProviderId   = LibraryScannerProviderId,
                ClaimKey     = ul.Key,
                ClaimValue   = ul.Value,
                Confidence   = 1.0,
                ClaimedAt    = ul.LockedAt,
                IsUserLocked = true,
            })
            .ToList();

        if (toInsert.Count > 0)
            await _claimRepo.InsertBatchAsync(toInsert, ct).ConfigureAwait(false);
    }
}
