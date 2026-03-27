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
/// Scans a Library Root directory for media files and uses embedded metadata
/// plus batch reconciliation to rebuild the database — the "Great Inhale v2".
///
/// <para>
/// Files already known to the database (matched by content hash) have their
/// paths updated if they have moved. New files are noted for a follow-up
/// ingestion pass. Person sidecars (person.xml) are read from <c>.people/</c>
/// for recovery. Universe sidecars have been removed — universe data is stored
/// exclusively in the database.
/// </para>
/// </summary>
public sealed class LibraryScanner : ILibraryScanner
{
    private readonly IAssetHasher                    _hasher;
    private readonly IHubRepository                  _hubRepo;
    private readonly IMediaAssetRepository           _assetRepo;
    private readonly ICanonicalValueRepository       _canonicalRepo;
    private readonly IMetadataClaimRepository        _claimRepo;
    private readonly IPersonRepository               _personRepo;
    private readonly INarrativeRootRepository        _rootRepo;
    private readonly IFictionalEntityRepository      _fictionalEntityRepo;
    private readonly IEntityRelationshipRepository   _relRepo;
    private readonly IQidLabelRepository             _qidLabelRepo;
    private readonly ICanonicalValueArrayRepository  _arrayRepo;
    private readonly ILogger<LibraryScanner>         _logger;

    // Stable GUID representing the library-scanner as a "provider" when re-inserting
    // canonical values. Distinct from the local-processor GUID so the claim source
    // is distinguishable in the claims table.
    private static readonly Guid ScannerProviderGuid =
        new("c9d8e7f6-a5b4-4321-fedc-0102030405c9");

    // Media file extensions that the scanner should enumerate.
    private static readonly HashSet<string> MediaExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".epub", ".pdf",
            ".m4b", ".mp3", ".m4a", ".flac", ".ogg", ".wav", ".opus", ".wma",
            ".mp4", ".mkv", ".avi", ".webm",
            ".cbz", ".cbr",
        };

    // Sub-directories inside LibraryRoot that should never be scanned for media files.
    private static readonly HashSet<string> SkipDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".staging", ".people", ".universe", ".tuvima-shadow", ".orphans",
        };

    public LibraryScanner(
        IAssetHasher                    hasher,
        IHubRepository                  hubRepo,
        IMediaAssetRepository           assetRepo,
        ICanonicalValueRepository       canonicalRepo,
        IMetadataClaimRepository        claimRepo,
        IPersonRepository               personRepo,
        INarrativeRootRepository        rootRepo,
        IFictionalEntityRepository      fictionalEntityRepo,
        IEntityRelationshipRepository   relRepo,
        IQidLabelRepository             qidLabelRepo,
        ICanonicalValueArrayRepository  arrayRepo,
        ILogger<LibraryScanner>         logger)
    {
        _hasher               = hasher;
        _hubRepo              = hubRepo;
        _assetRepo            = assetRepo;
        _canonicalRepo        = canonicalRepo;
        _claimRepo            = claimRepo;
        _personRepo           = personRepo;
        _rootRepo             = rootRepo;
        _fictionalEntityRepo  = fictionalEntityRepo;
        _relRepo              = relRepo;
        _qidLabelRepo         = qidLabelRepo;
        _arrayRepo            = arrayRepo;
        _logger               = logger;
    }

    /// <inheritdoc/>
    public async Task<LibraryScanResult> ScanAsync(
        string libraryRoot,
        CancellationToken ct = default)
    {
        var sw            = Stopwatch.StartNew();
        int filesScanned  = 0;
        int pathsUpdated  = 0;
        int errors        = 0;

        _logger.LogInformation(
            "Great Inhale v2 started. Library root: {LibraryRoot}", libraryRoot);

        foreach (var filePath in EnumerateMediaFiles(libraryRoot))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Compute a SHA-256 hash to identify the file by its content.
                HashResult hash;
                try
                {
                    hash = await _hasher.ComputeAsync(filePath, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Great Inhale: could not hash {Path} — skipping", filePath);
                    errors++;
                    continue;
                }

                // If the asset is already in the database, update its path if it moved.
                var existing = await _assetRepo.FindByHashAsync(hash.Hex, ct)
                    .ConfigureAwait(false);

                if (existing is not null)
                {
                    if (!string.Equals(existing.FilePathRoot, filePath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        await _assetRepo.UpdateFilePathAsync(existing.Id, filePath, ct)
                            .ConfigureAwait(false);
                        pathsUpdated++;
                        _logger.LogDebug(
                            "Great Inhale: path updated for asset {Id}: {Old} → {New}",
                            existing.Id, existing.FilePathRoot, filePath);
                    }
                }
                else
                {
                    // New file not yet in the database.
                    // A full ingestion pass is needed to process it properly
                    // (processor → claims → scoring → hydration).
                    _logger.LogDebug(
                        "Great Inhale: new file not yet in DB — " +
                        "run an ingestion pass to process it: {Path}", filePath);
                }

                filesScanned++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Great Inhale: error processing {Path}", filePath);
                errors++;
            }
        }

        // Recover person data from the .people/ sub-directory.
        int personsRecovered = 0;
        try
        {
            personsRecovered = await ScanPeopleAsync(libraryRoot, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Great Inhale: people scan failed — continuing");
        }

        sw.Stop();
        _logger.LogInformation(
            "Great Inhale v2 complete — files: {Files}, paths updated: {Paths}, " +
            "persons: {Persons}, errors: {Errors}, elapsed: {Elapsed}ms",
            filesScanned, pathsUpdated, personsRecovered, errors,
            (long)sw.Elapsed.TotalMilliseconds);

        return new LibraryScanResult
        {
            HubsUpserted     = 0,
            EditionsUpserted = filesScanned,
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
            {
                // No person.xml — try to recover from folder name pattern alone.
                var folderName = Path.GetFileName(subDir);
                var folderMatch = System.Text.RegularExpressions.Regex.Match(
                    folderName, @"^(.+?)\s*\((Q\d+)\)$");
                if (folderMatch.Success)
                {
                    var nameFromFolder = folderMatch.Groups[1].Value.Trim();
                    var qidFromFolder  = folderMatch.Groups[2].Value;
                    if (!string.IsNullOrWhiteSpace(nameFromFolder) &&
                        !string.IsNullOrWhiteSpace(qidFromFolder))
                    {
                        var personByQid = await _personRepo
                            .FindByQidAsync(qidFromFolder, ct).ConfigureAwait(false);
                        if (personByQid is null)
                        {
                            _logger.LogDebug(
                                "Great Inhale: no person.xml in {Dir} but QID found in " +
                                "folder name — noting for future enrichment", subDir);
                        }
                    }
                }
                continue;
            }

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
                existing ??= await _personRepo.FindByNameAsync(name, ct).ConfigureAwait(false);
                existing ??= await _personRepo.FindByIdAsync(personId, ct).ConfigureAwait(false);

                if (existing is null)
                {
                    // Create a new person record.
                    var person = new Person
                    {
                        Id           = personId,
                        Name         = name,
                        Roles        = [role],
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
    // Universe scanning (no-op — universe data is stored in the database)
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<UniverseScanResult> ScanUniversesAsync(
        string libraryRoot,
        CancellationToken ct = default)
    {
        // Universe sidecar files (universe.xml) have been removed. Universe data
        // (fictional_entities, entity_relationships, narrative_roots) is stored
        // exclusively in the database and does not require filesystem recovery.
        _logger.LogDebug(
            "ScanUniversesAsync called — no-op (universe data is stored in the database)");
        return Task.FromResult(new UniverseScanResult());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Enumerates all media files under <paramref name="root"/>, skipping
    /// internal sub-directories (<c>.staging</c>, <c>.people</c>, etc.).
    /// </summary>
    private static IEnumerable<string> EnumerateMediaFiles(string root)
    {
        if (!Directory.Exists(root))
            yield break;

        // Check root-level files first.
        foreach (var file in Directory.EnumerateFiles(root))
        {
            if (MediaExtensions.Contains(Path.GetExtension(file)))
                yield return file;
        }

        // Then recurse into non-skip sub-directories.
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var dirName = Path.GetFileName(dir);
            if (SkipDirectories.Contains(dirName))
                continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (MediaExtensions.Contains(Path.GetExtension(file)))
                    yield return file;
            }
        }
    }
}
