using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IPersonRepository"/>.
/// Uses Dapper for type-safe column-to-property mapping.
///
/// Persons are looked up by QID or name at ingestion time and enriched
/// asynchronously by the Wikidata adapter.  Roles are stored in the
/// <c>person_roles</c> junction table.  Person-to-asset links live in
/// the <c>person_media_links</c> junction table.
///
/// Spec: Phase 9 - Recursive Person Enrichment.
/// </summary>
public sealed class PersonRepository : IPersonRepository
{
    private readonly IDatabaseConnection _db;

    // Reusable SELECT list with aliases matching Person property names.
    // Note: 'role' column has been removed; roles come from person_roles.
    private const string SelectColumns = """
        id                 AS Id,
        name               AS Name,
        wikidata_qid       AS WikidataQid,
        headshot_url       AS HeadshotUrl,
        biography          AS Biography,
        created_at         AS CreatedAt,
        enriched_at        AS EnrichedAt,
        occupation         AS Occupation,
        instagram          AS Instagram,
        twitter            AS Twitter,
        tiktok             AS TikTok,
        mastodon           AS Mastodon,
        website            AS Website,
        local_headshot_path AS LocalHeadshotPath,
        date_of_birth      AS DateOfBirth,
        date_of_death      AS DateOfDeath,
        place_of_birth     AS PlaceOfBirth,
        place_of_death     AS PlaceOfDeath,
        nationality        AS Nationality,
        is_pseudonym       AS IsPseudonym
        """;

    /// <summary>Helper record for reading character-performer link rows.</summary>
    private sealed record CharacterLinkRow(string FictionalEntityId, string? WorkQid);

    /// <summary>Helper record for the ListAllAsync GROUP_CONCAT query.</summary>
    private sealed record PersonWithRolesCsv
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string? WikidataQid { get; init; }
        public string? HeadshotUrl { get; init; }
        public string? Biography { get; init; }
        public string? CreatedAt { get; init; }
        public string? EnrichedAt { get; init; }
        public string? Occupation { get; init; }
        public string? Instagram { get; init; }
        public string? Twitter { get; init; }
        public string? TikTok { get; init; }
        public string? Mastodon { get; init; }
        public string? Website { get; init; }
        public string? LocalHeadshotPath { get; init; }
        public string? DateOfBirth { get; init; }
        public string? DateOfDeath { get; init; }
        public string? PlaceOfBirth { get; init; }
        public string? PlaceOfDeath { get; init; }
        public string? Nationality { get; init; }
        public int IsPseudonym { get; init; }
        public string? RolesCsv { get; init; }
    }

    /// <summary>Helper record for the presence batch query.</summary>
    private sealed record PresenceRow
    {
        public string PersonId { get; init; } = "";
        public string MediaType { get; init; } = "";
        public int Count { get; init; }
    }

    public PersonRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    // -------------------------------------------------------------------------
    // IPersonRepository
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<Person?> FindByNameAsync(
        string name,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var conn = _db.CreateConnection();
        // COLLATE NOCASE: SQLite case-insensitive comparison for ASCII names.
        var p = new DynamicParameters();
        p.Add("name", name);
        var result = conn.QueryFirstOrDefault<Person>($"""
            SELECT {SelectColumns}
            FROM   persons
            WHERE  name = @name COLLATE NOCASE
            LIMIT  1;
            """, p);

        if (result is not null)
            PopulateRoles(conn, result);

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<Person> CreateAsync(Person person, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(person);

        using var conn = _db.CreateConnection();
        var p = new DynamicParameters();
        p.Add("id",          person.Id.ToString());
        p.Add("name",        person.Name);
        p.Add("wikidataQid", person.WikidataQid);
        p.Add("headshotUrl", person.HeadshotUrl);
        p.Add("biography",   person.Biography);
        p.Add("createdAt",   person.CreatedAt.ToString("o"));
        p.Add("enrichedAt",  person.EnrichedAt.HasValue ? person.EnrichedAt.Value.ToString("o") : null);
        p.Add("dateOfBirth",  person.DateOfBirth);
        p.Add("dateOfDeath",  person.DateOfDeath);
        p.Add("placeOfBirth", person.PlaceOfBirth);
        p.Add("placeOfDeath", person.PlaceOfDeath);
        p.Add("nationality",  person.Nationality);
        p.Add("isPseudonym",  person.IsPseudonym ? 1 : 0);
        conn.Execute("""
            INSERT INTO persons
                (id, name, wikidata_qid, headshot_url, biography,
                 created_at, enriched_at, date_of_birth, date_of_death,
                 place_of_birth, place_of_death, nationality, is_pseudonym)
            VALUES
                (@id, @name, @wikidataQid, @headshotUrl, @biography,
                 @createdAt, @enrichedAt, @dateOfBirth, @dateOfDeath,
                 @placeOfBirth, @placeOfDeath, @nationality, @isPseudonym);
            """, p);

        // Insert each role into person_roles junction table.
        foreach (var role in person.Roles)
        {
            if (string.IsNullOrWhiteSpace(role)) continue;
            var rp = new DynamicParameters();
            rp.Add("personId", person.Id.ToString());
            rp.Add("role", role);
            conn.Execute("""
                INSERT OR IGNORE INTO person_roles (person_id, role)
                VALUES (@personId, @role);
                """, rp);
        }

        return Task.FromResult(person);
    }

    /// <inheritdoc/>
    public Task UpdateEnrichmentAsync(
        Guid personId,
        string? wikidataQid,
        string? headshotUrl,
        string? biography,
        string? name,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var p = new DynamicParameters();
        p.Add("name",        name);
        p.Add("wikidataQid", wikidataQid);
        p.Add("headshotUrl", headshotUrl);
        p.Add("biography",   biography);
        p.Add("enrichedAt",  DateTimeOffset.UtcNow.ToString("o"));
        p.Add("id",          personId.ToString());
        conn.Execute("""
            UPDATE persons
            SET    name         = COALESCE(@name, name),
                   wikidata_qid = @wikidataQid,
                   headshot_url = @headshotUrl,
                   biography    = @biography,
                   enriched_at  = @enrichedAt
            WHERE  id = @id;
            """, p);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpdateBiographicalFieldsAsync(
        Guid personId,
        string? dateOfBirth,
        string? dateOfDeath,
        string? placeOfBirth,
        string? placeOfDeath,
        string? nationality,
        bool isPseudonym,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var p = new DynamicParameters();
        p.Add("dateOfBirth",  dateOfBirth);
        p.Add("dateOfDeath",  dateOfDeath);
        p.Add("placeOfBirth", placeOfBirth);
        p.Add("placeOfDeath", placeOfDeath);
        p.Add("nationality",  nationality);
        p.Add("isPseudonym",  isPseudonym ? 1 : 0);
        p.Add("id",           personId.ToString());
        conn.Execute("""
            UPDATE persons
            SET    date_of_birth  = @dateOfBirth,
                   date_of_death  = @dateOfDeath,
                   place_of_birth = @placeOfBirth,
                   place_of_death = @placeOfDeath,
                   nationality    = @nationality,
                   is_pseudonym   = @isPseudonym
            WHERE  id = @id;
            """, p);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpdateSocialFieldsAsync(
        Guid personId,
        string? occupation,
        string? instagram,
        string? twitter,
        string? tiktok,
        string? mastodon,
        string? website,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var p = new DynamicParameters();
        p.Add("occupation", occupation);
        p.Add("instagram",  instagram);
        p.Add("twitter",    twitter);
        p.Add("tiktok",     tiktok);
        p.Add("mastodon",   mastodon);
        p.Add("website",    website);
        p.Add("id",         personId.ToString());
        conn.Execute("""
            UPDATE persons
            SET    occupation = COALESCE(@occupation, occupation),
                   instagram  = COALESCE(@instagram,  instagram),
                   twitter    = COALESCE(@twitter,    twitter),
                   tiktok     = COALESCE(@tiktok,     tiktok),
                   mastodon   = COALESCE(@mastodon,   mastodon),
                   website    = COALESCE(@website,    website)
            WHERE  id = @id;
            """, p);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task LinkToMediaAssetAsync(
        Guid mediaAssetId,
        Guid personId,
        string role,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        using var conn = _db.CreateConnection();
        // INSERT OR IGNORE: composite PK (media_asset_id, person_id, role) prevents
        // duplicate links; repeated calls for the same triplet are safe no-ops.
        var p = new DynamicParameters();
        p.Add("mediaAssetId", mediaAssetId.ToString());
        p.Add("personId",     personId.ToString());
        p.Add("role",         role);
        conn.Execute("""
            INSERT OR IGNORE INTO person_media_links
                (media_asset_id, person_id, role)
            VALUES
                (@mediaAssetId, @personId, @role);
            """, p);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Person>> GetByMediaAssetAsync(
        Guid mediaAssetId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var p = new DynamicParameters();
        p.Add("mediaAssetId", mediaAssetId.ToString());
        var rows = conn.Query<PersonWithRolesCsv>($"""
            SELECT p.id                  AS Id,
                   p.name                AS Name,
                   p.wikidata_qid        AS WikidataQid,
                   p.headshot_url        AS HeadshotUrl,
                   p.biography           AS Biography,
                   p.created_at          AS CreatedAt,
                   p.enriched_at         AS EnrichedAt,
                   p.occupation          AS Occupation,
                   p.instagram           AS Instagram,
                   p.twitter             AS Twitter,
                   p.tiktok              AS TikTok,
                   p.mastodon            AS Mastodon,
                   p.website             AS Website,
                   p.local_headshot_path AS LocalHeadshotPath,
                   p.date_of_birth       AS DateOfBirth,
                   p.date_of_death       AS DateOfDeath,
                   p.place_of_birth      AS PlaceOfBirth,
                   p.place_of_death      AS PlaceOfDeath,
                   p.nationality         AS Nationality,
                   p.is_pseudonym        AS IsPseudonym,
                   GROUP_CONCAT(pr.role, ',') AS RolesCsv
            FROM   persons p
            JOIN   person_media_links l ON l.person_id = p.id
            LEFT JOIN person_roles pr ON pr.person_id = p.id
            WHERE  l.media_asset_id = @mediaAssetId
            GROUP  BY p.id
            ORDER  BY p.name ASC;
            """, p).AsList();

        var results = rows.Select(MapFromCsvRow).ToList();
        return Task.FromResult<IReadOnlyList<Person>>(results);
    }

    /// <inheritdoc/>
    public Task UpdateLocalHeadshotPathAsync(
        Guid id,
        string path,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var conn = _db.CreateConnection();
        var p = new DynamicParameters();
        p.Add("path", path);
        p.Add("id",   id.ToString());
        conn.Execute("""
            UPDATE persons
            SET    local_headshot_path = @path
            WHERE  id = @id;
            """, p);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<Person?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var p = new DynamicParameters();
        p.Add("id", id.ToString());
        var result = conn.QueryFirstOrDefault<Person>($"""
            SELECT {SelectColumns}
            FROM   persons
            WHERE  id = @id
            LIMIT  1;
            """, p);

        if (result is not null)
            PopulateRoles(conn, result);

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Person>> ListAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var rows = conn.Query<PersonWithRolesCsv>("""
            SELECT p.id                  AS Id,
                   p.name                AS Name,
                   p.wikidata_qid        AS WikidataQid,
                   p.headshot_url        AS HeadshotUrl,
                   p.biography           AS Biography,
                   p.created_at          AS CreatedAt,
                   p.enriched_at         AS EnrichedAt,
                   p.occupation          AS Occupation,
                   p.instagram           AS Instagram,
                   p.twitter             AS Twitter,
                   p.tiktok              AS TikTok,
                   p.mastodon            AS Mastodon,
                   p.website             AS Website,
                   p.local_headshot_path AS LocalHeadshotPath,
                   p.date_of_birth       AS DateOfBirth,
                   p.date_of_death       AS DateOfDeath,
                   p.place_of_birth      AS PlaceOfBirth,
                   p.place_of_death      AS PlaceOfDeath,
                   p.nationality         AS Nationality,
                   p.is_pseudonym        AS IsPseudonym,
                   GROUP_CONCAT(pr.role, ',') AS RolesCsv
            FROM   persons p
            LEFT JOIN person_roles pr ON pr.person_id = p.id
            GROUP  BY p.id
            ORDER  BY p.name ASC;
            """).AsList();

        var results = rows.Select(MapFromCsvRow).ToList();
        return Task.FromResult<IReadOnlyList<Person>>(results);
    }

    /// <inheritdoc/>
    public Task<int> CountMediaLinksAsync(Guid personId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var p = new DynamicParameters();
        p.Add("id", personId.ToString());
        var count = conn.ExecuteScalar<int>("""
            SELECT COUNT(*) FROM person_media_links WHERE person_id = @id;
            """, p);

        return Task.FromResult(count);
    }

    /// <inheritdoc/>
    public Task<Person?> FindByQidAsync(string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(qid);

        using var conn = _db.CreateConnection();
        var p = new DynamicParameters();
        p.Add("qid", qid);
        var result = conn.QueryFirstOrDefault<Person>($"""
            SELECT {SelectColumns}
            FROM   persons
            WHERE  wikidata_qid = @qid COLLATE NOCASE
            LIMIT  1;
            """, p);

        if (result is not null)
            PopulateRoles(conn, result);

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(Guid personId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var id = personId.ToString();
        using var conn = _db.CreateConnection();

        // Delete links first (FK-safe even without ON DELETE CASCADE).
        var p = new DynamicParameters();
        p.Add("id", id);
        conn.Execute(
            "DELETE FROM person_roles WHERE person_id = @id;",
            p);
        conn.Execute(
            "DELETE FROM person_media_links WHERE person_id = @id;",
            p);

        conn.Execute(
            "DELETE FROM persons WHERE id = @id;",
            p);

        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Role management (person_roles junction table)
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task AddRoleAsync(Guid personId, string role, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        using var conn = _db.CreateConnection();
        var p = new DynamicParameters();
        p.Add("personId", personId.ToString());
        p.Add("role", role);
        conn.Execute("""
            INSERT OR IGNORE INTO person_roles (person_id, role)
            VALUES (@personId, @role);
            """, p);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> GetRolesAsync(Guid personId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var p = new DynamicParameters();
        p.Add("id", personId.ToString());
        var roles = conn.Query<string>("""
            SELECT role FROM person_roles WHERE person_id = @id ORDER BY role;
            """, p).AsList();

        return Task.FromResult<IReadOnlyList<string>>(roles);
    }

    /// <inheritdoc/>
    public Task<Dictionary<string, int>> GetRoleCountsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var rows = conn.Query<(string Role, int Count)>("""
            SELECT role AS Role, COUNT(DISTINCT person_id) AS Count
            FROM   person_roles
            GROUP  BY role;
            """).AsList();

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (role, count) in rows)
            result[role] = count;

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<Dictionary<Guid, Dictionary<string, int>>> GetPresenceBatchAsync(
        IEnumerable<Guid> personIds,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var idList = personIds.Select(id => id.ToString()).ToList();
        if (idList.Count == 0)
            return Task.FromResult(new Dictionary<Guid, Dictionary<string, int>>());

        using var conn = _db.CreateConnection();
        var p = new DynamicParameters();
        p.Add("PersonIds", idList);

        // Primary path: persons linked via person_media_links (populated during Wikidata Stage 2).
        var rows = conn.Query<PresenceRow>("""
            SELECT p.id AS PersonId, cv.value AS MediaType, COUNT(DISTINCT w.id) AS Count
            FROM persons p
            JOIN person_media_links pml ON pml.person_id = p.id
            JOIN media_assets ma ON ma.id = pml.media_asset_id
            JOIN editions e ON e.id = ma.edition_id
            JOIN works w ON w.id = e.work_id
            JOIN canonical_values cv ON cv.entity_id = ma.id AND cv.key = 'media_type'
            WHERE p.id IN @PersonIds
            GROUP BY p.id, cv.value;
            """, p).AsList();

        // For persons with no media links, fall back to matching by name in canonical_values
        // (covers single-valued author/narrator/director fields stored during ingestion).
        var linkedPersonIds = rows.Select(r => r.PersonId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unlinkedIds = idList.Where(id => !linkedPersonIds.Contains(id)).ToList();

        if (unlinkedIds.Count > 0)
        {
            var fallbackParams = new DynamicParameters();
            fallbackParams.Add("UnlinkedIds", unlinkedIds);
            var fallbackRows = conn.Query<PresenceRow>("""
                SELECT p.id AS PersonId, cvmt.value AS MediaType, COUNT(DISTINCT w.id) AS Count
                FROM persons p
                JOIN canonical_values cva ON cva.value = p.name
                    AND cva.key IN ('author', 'narrator', 'director', 'artist', 'composer', 'illustrator', 'performer')
                JOIN media_assets ma ON ma.id = cva.entity_id
                JOIN editions e ON e.id = ma.edition_id
                JOIN works w ON w.id = e.work_id
                JOIN canonical_values cvmt ON cvmt.entity_id = ma.id AND cvmt.key = 'media_type'
                WHERE p.id IN @UnlinkedIds
                GROUP BY p.id, cvmt.value
                UNION ALL
                SELECT p.id AS PersonId, cvmt.value AS MediaType, COUNT(DISTINCT w.id) AS Count
                FROM persons p
                JOIN canonical_value_arrays cvaa ON cvaa.value = p.name
                    AND cvaa.key IN ('author', 'narrator', 'director', 'artist', 'composer', 'illustrator', 'performer')
                JOIN media_assets ma ON ma.id = cvaa.entity_id
                JOIN editions e ON e.id = ma.edition_id
                JOIN works w ON w.id = e.work_id
                JOIN canonical_values cvmt ON cvmt.entity_id = ma.id AND cvmt.key = 'media_type'
                WHERE p.id IN @UnlinkedIds
                GROUP BY p.id, cvmt.value;
                """, fallbackParams).AsList();
            rows.AddRange(fallbackRows);
        }

        var result = new Dictionary<Guid, Dictionary<string, int>>();
        foreach (var row in rows)
        {
            var personId = Guid.Parse(row.PersonId);
            if (!result.TryGetValue(personId, out var mediaMap))
            {
                mediaMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                result[personId] = mediaMap;
            }
            // Use max rather than overwrite to handle UNION ALL duplicates from fallback
            mediaMap[row.MediaType] = Math.Max(mediaMap.GetValueOrDefault(row.MediaType), row.Count);
        }

        return Task.FromResult(result);
    }

    // -------------------------------------------------------------------------
    // Alias (pseudonym) management
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task LinkAliasAsync(
        Guid pseudonymPersonId,
        Guid realPersonId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var p = new DynamicParameters();
        p.Add("pseudonymId", pseudonymPersonId.ToString());
        p.Add("realId",      realPersonId.ToString());
        conn.Execute("""
            INSERT OR IGNORE INTO person_aliases
                (pseudonym_person_id, real_person_id)
            VALUES
                (@pseudonymId, @realId);
            """, p);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Person>> FindAliasesAsync(
        Guid personId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        // Returns both directions: real people behind a pseudonym,
        // and pseudonyms used by a real person.
        var p = new DynamicParameters();
        p.Add("id", personId.ToString());
        var rows = conn.Query<PersonWithRolesCsv>($"""
            SELECT p.id                  AS Id,
                   p.name                AS Name,
                   p.wikidata_qid        AS WikidataQid,
                   p.headshot_url        AS HeadshotUrl,
                   p.biography           AS Biography,
                   p.created_at          AS CreatedAt,
                   p.enriched_at         AS EnrichedAt,
                   p.occupation          AS Occupation,
                   p.instagram           AS Instagram,
                   p.twitter             AS Twitter,
                   p.tiktok              AS TikTok,
                   p.mastodon            AS Mastodon,
                   p.website             AS Website,
                   p.local_headshot_path AS LocalHeadshotPath,
                   p.date_of_birth       AS DateOfBirth,
                   p.date_of_death       AS DateOfDeath,
                   p.place_of_birth      AS PlaceOfBirth,
                   p.place_of_death      AS PlaceOfDeath,
                   p.nationality         AS Nationality,
                   p.is_pseudonym        AS IsPseudonym,
                   GROUP_CONCAT(pr.role, ',') AS RolesCsv
            FROM   persons p
            LEFT JOIN person_roles pr ON pr.person_id = p.id
            WHERE  p.id IN (
                SELECT real_person_id FROM person_aliases WHERE pseudonym_person_id = @id
                UNION
                SELECT pseudonym_person_id FROM person_aliases WHERE real_person_id = @id
            )
            GROUP  BY p.id
            ORDER  BY p.name ASC;
            """, p).AsList();

        var results = rows.Select(MapFromCsvRow).ToList();
        return Task.FromResult<IReadOnlyList<Person>>(results);
    }

    // -------------------------------------------------------------------------
    // Character-performer links
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task LinkToCharacterAsync(
        Guid personId,
        Guid fictionalEntityId,
        string? workQid,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var p = new DynamicParameters();
        p.Add("personId", personId.ToString());
        p.Add("entityId", fictionalEntityId.ToString());
        p.Add("workQid",  workQid);
        conn.Execute("""
            INSERT OR IGNORE INTO character_performer_links
                (person_id, fictional_entity_id, work_qid)
            VALUES
                (@personId, @entityId, @workQid);
            """, p);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<(Guid FictionalEntityId, string? WorkQid)>> GetCharacterLinksAsync(
        Guid personId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var p = new DynamicParameters();
        p.Add("personId", personId.ToString());
        var rows = conn.Query<CharacterLinkRow>("""
            SELECT fictional_entity_id AS FictionalEntityId,
                   work_qid            AS WorkQid
            FROM   character_performer_links
            WHERE  person_id = @personId;
            """, p).AsList();

        IReadOnlyList<(Guid, string?)> result = rows
            .Select(r => (Guid.Parse(r.FictionalEntityId), r.WorkQid))
            .ToList();

        return Task.FromResult(result);
    }

    // -------------------------------------------------------------------------
    // QID-based deduplication merge
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task ReassignAllLinksAsync(
        Guid fromPersonId,
        Guid toPersonId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var from = fromPersonId.ToString();
        var to   = toPersonId.ToString();

        using var conn = _db.CreateConnection();
        using var tx   = conn.BeginTransaction();

        // 1. Reassign media links (OR IGNORE handles PK conflicts)
        var pToFrom = new DynamicParameters();
        pToFrom.Add("to",   to);
        pToFrom.Add("from", from);

        var pFrom = new DynamicParameters();
        pFrom.Add("from", from);

        conn.Execute(
            "UPDATE OR IGNORE person_media_links SET person_id = @to WHERE person_id = @from;",
            pToFrom, transaction: tx);
        conn.Execute(
            "DELETE FROM person_media_links WHERE person_id = @from;",
            pFrom, transaction: tx);

        // 2. Reassign person_roles (merge roles from source into target)
        conn.Execute(
            "INSERT OR IGNORE INTO person_roles (person_id, role) SELECT @to, role FROM person_roles WHERE person_id = @from;",
            pToFrom, transaction: tx);
        conn.Execute(
            "DELETE FROM person_roles WHERE person_id = @from;",
            pFrom, transaction: tx);

        // 3. Reassign character-performer links
        conn.Execute(
            "UPDATE OR IGNORE character_performer_links SET person_id = @to WHERE person_id = @from;",
            pToFrom, transaction: tx);
        conn.Execute(
            "DELETE FROM character_performer_links WHERE person_id = @from;",
            pFrom, transaction: tx);

        // 4. Reassign alias links (both directions)
        conn.Execute(
            "UPDATE OR IGNORE person_aliases SET pseudonym_person_id = @to WHERE pseudonym_person_id = @from;",
            pToFrom, transaction: tx);
        conn.Execute(
            "UPDATE OR IGNORE person_aliases SET real_person_id = @to WHERE real_person_id = @from;",
            pToFrom, transaction: tx);
        conn.Execute(
            "DELETE FROM person_aliases WHERE pseudonym_person_id = @from OR real_person_id = @from;",
            pFrom, transaction: tx);

        tx.Commit();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> IsPseudonymOrAliasAsync(Guid personId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var id = personId.ToString();
        using var conn = _db.CreateConnection();

        // Check persons.is_pseudonym flag first - cheapest query.
        var pId = new DynamicParameters();
        pId.Add("id", id);

        var isPseudo = conn.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM persons WHERE id = @id AND is_pseudonym = 1;",
            pId) > 0;

        if (isPseudo)
            return Task.FromResult(true);

        // Check person_aliases in either direction - pen name or real author.
        var isAlias = conn.ExecuteScalar<int>("""
            SELECT COUNT(1) FROM person_aliases
            WHERE pseudonym_person_id = @id OR real_person_id = @id;
            """, pId) > 0;

        return Task.FromResult(isAlias);
    }

    /// <inheritdoc/>
    public async Task LinkGroupMemberAsync(Guid groupId, Guid memberId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            """
            INSERT OR IGNORE INTO person_group_members (group_id, member_id)
            VALUES (@GroupId, @MemberId)
            """,
            new { GroupId = groupId.ToString(), MemberId = memberId.ToString() }).ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Populates the <see cref="Person.Roles"/> list from the <c>person_roles</c> table
    /// for a single person that was loaded without a GROUP_CONCAT join.
    /// </summary>
    private static void PopulateRoles(Microsoft.Data.Sqlite.SqliteConnection conn, Person person)
    {
        var p = new DynamicParameters();
        p.Add("id", person.Id.ToString());
        person.Roles = conn.Query<string>("""
            SELECT role FROM person_roles WHERE person_id = @id ORDER BY role;
            """, p).AsList();
    }

    /// <summary>
    /// Maps a <see cref="PersonWithRolesCsv"/> row (from GROUP_CONCAT query) to a
    /// <see cref="Person"/> entity with the <see cref="Person.Roles"/> list populated.
    /// </summary>
    private static Person MapFromCsvRow(PersonWithRolesCsv row)
    {
        var roles = string.IsNullOrEmpty(row.RolesCsv)
            ? new List<string>()
            : row.RolesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .ToList();

        return new Person
        {
            Id               = Guid.Parse(row.Id),
            Name             = row.Name,
            Roles            = roles,
            WikidataQid      = row.WikidataQid,
            HeadshotUrl      = row.HeadshotUrl,
            Biography        = row.Biography,
            CreatedAt        = row.CreatedAt is not null ? DateTimeOffset.Parse(row.CreatedAt) : DateTimeOffset.UtcNow,
            EnrichedAt       = row.EnrichedAt is not null ? DateTimeOffset.Parse(row.EnrichedAt) : null,
            Occupation       = row.Occupation,
            Instagram        = row.Instagram,
            Twitter          = row.Twitter,
            TikTok           = row.TikTok,
            Mastodon         = row.Mastodon,
            Website          = row.Website,
            LocalHeadshotPath = row.LocalHeadshotPath,
            DateOfBirth      = row.DateOfBirth,
            DateOfDeath      = row.DateOfDeath,
            PlaceOfBirth     = row.PlaceOfBirth,
            PlaceOfDeath     = row.PlaceOfDeath,
            Nationality      = row.Nationality,
            IsPseudonym      = row.IsPseudonym != 0,
        };
    }
}
