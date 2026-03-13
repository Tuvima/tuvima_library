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
///   <item>Edition-level XML (<c>&lt;library-edition&gt;</c>) — upserts canonical values for
///     any MediaAsset already in the database (matched by content hash). Re-inserts
///     user-locked claims that have been lost from <c>metadata_claims</c>.
///     Hub records are reconstructed from edition sidecar data (title as display name).</item>
/// </list>
/// Hub-level sidecars (<c>&lt;library-hub&gt;</c>) are no longer written.  Legacy
/// hub sidecars encountered during scanning are silently ignored.
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
    private readonly ISidecarWriter                  _sidecar;
    private readonly IHubRepository                  _hubRepo;
    private readonly IMediaAssetRepository           _assetRepo;
    private readonly ICanonicalValueRepository       _canonicalRepo;
    private readonly IMetadataClaimRepository        _claimRepo;
    private readonly IPersonRepository               _personRepo;
    private readonly INarrativeRootRepository        _rootRepo;
    private readonly IFictionalEntityRepository      _fictionalEntityRepo;
    private readonly IEntityRelationshipRepository   _relRepo;
    private readonly IUniverseSidecarWriter          _universeSidecar;
    private readonly IQidLabelRepository             _qidLabelRepo;
    private readonly ICanonicalValueArrayRepository  _arrayRepo;
    private readonly ILogger<LibraryScanner>         _logger;

    // Stable GUID representing the library-scanner as a "provider" when re-inserting
    // canonical values from the sidecar. Distinct from the local-processor GUID
    // so the claim source is distinguishable in the claims table.
    private static readonly Guid LibraryScannerProviderId =
        new("c9d8e7f6-a5b4-4321-fedc-0102030405c9");

    public LibraryScanner(
        ISidecarWriter                  sidecar,
        IHubRepository                  hubRepo,
        IMediaAssetRepository           assetRepo,
        ICanonicalValueRepository       canonicalRepo,
        IMetadataClaimRepository        claimRepo,
        IPersonRepository               personRepo,
        INarrativeRootRepository        rootRepo,
        IFictionalEntityRepository      fictionalEntityRepo,
        IEntityRelationshipRepository   relRepo,
        IUniverseSidecarWriter          universeSidecar,
        IQidLabelRepository             qidLabelRepo,
        ICanonicalValueArrayRepository  arrayRepo,
        ILogger<LibraryScanner>         logger)
    {
        _sidecar              = sidecar;
        _hubRepo              = hubRepo;
        _assetRepo            = assetRepo;
        _canonicalRepo        = canonicalRepo;
        _claimRepo            = claimRepo;
        _personRepo           = personRepo;
        _rootRepo             = rootRepo;
        _fictionalEntityRepo  = fictionalEntityRepo;
        _relRepo              = relRepo;
        _universeSidecar      = universeSidecar;
        _qidLabelRepo         = qidLabelRepo;
        _arrayRepo            = arrayRepo;
        _logger               = logger;
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
                    // Hub-level sidecars are legacy — silently skip.
                    // Hubs are reconstructed from edition sidecars.
                    _logger.LogDebug(
                        "Skipping legacy hub sidecar at {Path}", xmlPath);
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

                var name       = identity.Element("name")?.Value;
                var qid        = identity.Element("wikidata-qid")?.Value;
                var role       = identity.Element("role")?.Value ?? "Author";
                var occupation = identity.Element("occupation")?.Value;
                var isPseudonymStr = identity.Element("is-pseudonym")?.Value;
                var isPseudonym = string.Equals(isPseudonymStr, "true", StringComparison.OrdinalIgnoreCase);

                // v1.0 has <id> element; v1.1 relies on QID or folder name.
                var idText = identity.Element("id")?.Value;

                // Parse QID from folder name pattern: "Name (Qxxxxx)"
                if (string.IsNullOrWhiteSpace(qid))
                {
                    var folderName = Path.GetFileName(subDir);
                    var match = System.Text.RegularExpressions.Regex.Match(
                        folderName, @"\((Q\d+)\)$");
                    if (match.Success)
                        qid = match.Groups[1].Value;
                }

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
                        Id           = personId,
                        Name         = name,
                        Role         = role,
                        WikidataQid  = NullIfEmpty(qid),
                        Occupation   = NullIfEmpty(occupation),
                        IsPseudonym  = isPseudonym,
                    };

                    // Read v1.1 biographical details.
                    var details = doc.Root.Element("details");
                    if (details is not null)
                    {
                        person.DateOfBirth = NullIfEmpty(details.Element("date-of-birth")?.Value);
                        person.DateOfDeath = NullIfEmpty(details.Element("date-of-death")?.Value);
                        person.PlaceOfBirth = NullIfEmpty(details.Element("place-of-birth")?.Value);
                        person.PlaceOfDeath = NullIfEmpty(details.Element("place-of-death")?.Value);
                        person.Nationality = NullIfEmpty(details.Element("nationality")?.Value);
                    }

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

                    // Mark as enriched if QID is present (came from a v1.1 sidecar).
                    if (!string.IsNullOrWhiteSpace(qid))
                        person.EnrichedAt = DateTimeOffset.UtcNow;

                    await _personRepo.CreateAsync(person, ct).ConfigureAwait(false);
                    existing = person;
                }

                // Cache person QID→label in qid_labels for offline resolution.
                if (!string.IsNullOrWhiteSpace(existing.WikidataQid))
                {
                    try
                    {
                        await _qidLabelRepo.UpsertAsync(
                            existing.WikidataQid, existing.Name, existing.Biography,
                            "Person", ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogDebug(ex,
                            "Failed to cache person QID label for {Qid}", existing.WikidataQid);
                    }
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

                // Rebuild pseudonym alias links from v1.1 sidecar.
                var realIdentities = doc.Root.Element("real-identities")?.Elements("person");
                if (realIdentities is not null)
                {
                    foreach (var ri in realIdentities)
                    {
                        var realQid = ri.Attribute("qid")?.Value;
                        if (string.IsNullOrWhiteSpace(realQid)) continue;
                        var realPerson = await _personRepo.FindByQidAsync(realQid, ct).ConfigureAwait(false);
                        if (realPerson is not null)
                        {
                            await _personRepo.LinkAliasAsync(existing.Id, realPerson.Id, ct)
                                .ConfigureAwait(false);
                        }
                    }
                }

                var pseudonyms = doc.Root.Element("pseudonyms")?.Elements("person");
                if (pseudonyms is not null)
                {
                    foreach (var ps in pseudonyms)
                    {
                        var penQid = ps.Attribute("qid")?.Value;
                        if (string.IsNullOrWhiteSpace(penQid)) continue;
                        var penPerson = await _personRepo.FindByQidAsync(penQid, ct).ConfigureAwait(false);
                        if (penPerson is not null)
                        {
                            await _personRepo.LinkAliasAsync(penPerson.Id, existing.Id, ct)
                                .ConfigureAwait(false);
                        }
                    }
                }

                // Rebuild character-performer links from v1.1 sidecar.
                var characters = doc.Root.Element("characters")?.Elements("character");
                if (characters is not null)
                {
                    foreach (var ch in characters)
                    {
                        var charQid = ch.Attribute("qid")?.Value;
                        var workQidAttr = ch.Attribute("work-qid")?.Value;
                        if (string.IsNullOrWhiteSpace(charQid)) continue;

                        var entity = await _fictionalEntityRepo.FindByQidAsync(charQid, ct)
                            .ConfigureAwait(false);
                        if (entity is not null)
                        {
                            await _personRepo.LinkToCharacterAsync(
                                existing.Id, entity.Id, workQidAttr, ct)
                                .ConfigureAwait(false);
                        }
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
    // Universe scanning (Great Inhale universe graph recovery)
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<UniverseScanResult> ScanUniversesAsync(
        string libraryRoot,
        CancellationToken ct = default)
    {
        var universeRoot = Path.Combine(libraryRoot, ".universe");
        if (!Directory.Exists(universeRoot))
        {
            _logger.LogDebug("No .universe/ directory found; skipping universe scan");
            return new UniverseScanResult();
        }

        int universesUpserted     = 0;
        int entitiesUpserted      = 0;
        int relationshipsUpserted = 0;
        int errors                = 0;

        _logger.LogInformation("Great Inhale: scanning .universe/ for graph recovery");

        foreach (var subDir in Directory.GetDirectories(universeRoot))
        {
            ct.ThrowIfCancellationRequested();

            var xmlPath = Path.Combine(subDir, "universe.xml");
            if (!File.Exists(xmlPath))
                continue;

            try
            {
                var snapshot = await _universeSidecar.ReadUniverseXmlAsync(xmlPath, ct)
                    .ConfigureAwait(false);

                if (snapshot is null)
                {
                    errors++;
                    continue;
                }

                // 1. Upsert the narrative root.
                await _rootRepo.UpsertAsync(snapshot.Root, ct).ConfigureAwait(false);
                universesUpserted++;

                // 2. Upsert all fictional entities.
                foreach (var entitySnapshot in snapshot.Entities)
                {
                    ct.ThrowIfCancellationRequested();

                    var entity = entitySnapshot.Entity;

                    // Find-or-create by QID.
                    var existing = await _fictionalEntityRepo.FindByQidAsync(entity.WikidataQid, ct)
                        .ConfigureAwait(false);

                    if (existing is null)
                    {
                        await _fictionalEntityRepo.CreateAsync(entity, ct).ConfigureAwait(false);
                        existing = entity;
                    }

                    // Link entity to each work.
                    foreach (var wl in entitySnapshot.WorkLinks)
                    {
                        await _fictionalEntityRepo.LinkToWorkAsync(
                            existing.Id, wl.WorkQid, wl.WorkLabel, "appears_in", ct)
                            .ConfigureAwait(false);
                    }

                    entitiesUpserted++;
                }

                // 3. Upsert all relationship edges.
                foreach (var rel in snapshot.Relationships)
                {
                    ct.ThrowIfCancellationRequested();

                    await _relRepo.CreateAsync(rel, ct).ConfigureAwait(false);
                    relationshipsUpserted++;
                }

                _logger.LogDebug(
                    "Universe recovered: '{Label}' ({Qid}) — {EntityCount} entities, {EdgeCount} edges",
                    snapshot.Root.Label, snapshot.Root.Qid,
                    snapshot.Entities.Count, snapshot.Relationships.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Great Inhale: error processing universe.xml at {Path}", xmlPath);
                errors++;
            }
        }

        _logger.LogInformation(
            "Great Inhale: recovered {Universes} universes, {Entities} entities, {Rels} relationships",
            universesUpserted, entitiesUpserted, relationshipsUpserted);

        return new UniverseScanResult
        {
            UniversesUpserted     = universesUpserted,
            EntitiesUpserted      = entitiesUpserted,
            RelationshipsUpserted = relationshipsUpserted,
            Errors                = errors,
        };
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

        // Populate qid_labels from v2.0 QID attributes.
        await CacheQidLabelsFromSidecarAsync(data, ct).ConfigureAwait(false);

        // Decompose multi-valued canonicals into proper array rows.
        await DecomposeMultiValuedAsync(asset.Id, data, ct).ConfigureAwait(false);

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

    /// <summary>
    /// Caches QID→label pairs from v2.0 sidecar QID attributes into the
    /// <c>qid_labels</c> table so future sessions can resolve QIDs offline.
    /// Best-effort — never breaks the pipeline.
    /// </summary>
    private async Task CacheQidLabelsFromSidecarAsync(
        EditionSidecarData data,
        CancellationToken ct)
    {
        try
        {
            var entries = new List<(string qid, string label, string? entityType)>();

            if (!string.IsNullOrWhiteSpace(data.WikidataQid) &&
                !string.IsNullOrWhiteSpace(data.Title))
                entries.Add((data.WikidataQid, data.Title, "Work"));

            if (!string.IsNullOrWhiteSpace(data.AuthorQid) &&
                !string.IsNullOrWhiteSpace(data.Author))
                entries.Add((data.AuthorQid, data.Author, "Person"));

            // Multi-valued canonicals may carry QID→label pairs.
            foreach (var mv in data.MultiValuedCanonicals)
            {
                for (int i = 0; i < mv.Value.Qids.Length && i < mv.Value.Values.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(mv.Value.Qids[i]) &&
                        !string.IsNullOrWhiteSpace(mv.Value.Values[i]))
                        entries.Add((mv.Value.Qids[i], mv.Value.Values[i], null));
                }
            }

            foreach (var (qid, label, entityType) in entries)
            {
                await _qidLabelRepo.UpsertAsync(qid, label, null, entityType, ct)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to cache QID labels from edition sidecar");
        }
    }

    /// <summary>
    /// Decomposes multi-valued canonical entries from v2.0 sidecars into proper
    /// <c>canonical_value_arrays</c> rows.
    /// </summary>
    private async Task DecomposeMultiValuedAsync(
        Guid entityId,
        EditionSidecarData data,
        CancellationToken ct)
    {
        foreach (var mv in data.MultiValuedCanonicals)
        {
            if (mv.Value.Values.Length == 0)
                continue;

            try
            {
                var entries = new List<CanonicalArrayEntry>(mv.Value.Values.Length);
                for (int i = 0; i < mv.Value.Values.Length; i++)
                {
                    entries.Add(new CanonicalArrayEntry
                    {
                        Ordinal  = i,
                        Value    = mv.Value.Values[i],
                        ValueQid = i < mv.Value.Qids.Length ? mv.Value.Qids[i] : null,
                    });
                }

                await _arrayRepo.SetValuesAsync(entityId, mv.Key, entries, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex,
                    "Failed to decompose multi-valued field '{Key}' for entity {EntityId}",
                    mv.Key, entityId);
            }
        }
    }
}
