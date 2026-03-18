using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IPersonRepository"/>.
/// Uses Dapper for type-safe column-to-property mapping.
///
/// Persons are looked up by (name, role) at ingestion time and enriched
/// asynchronously by the Wikidata adapter.  Person-to-asset links live in
/// the <c>person_media_links</c> junction table.
///
/// Spec: Phase 9 – Recursive Person Enrichment.
/// </summary>
public sealed class PersonRepository : IPersonRepository
{
    private readonly IDatabaseConnection _db;

    // Reusable SELECT list with aliases matching Person property names.
    private const string SelectColumns = """
        id                 AS Id,
        name               AS Name,
        role               AS Role,
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
        string role,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        using var conn = _db.CreateConnection();
        // COLLATE NOCASE: SQLite case-insensitive comparison for ASCII names.
        var result = conn.QueryFirstOrDefault<Person>($"""
            SELECT {SelectColumns}
            FROM   persons
            WHERE  name = @name COLLATE NOCASE
              AND  role = @role COLLATE NOCASE
            LIMIT  1;
            """, new { name, role });

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<Person> CreateAsync(Person person, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(person);

        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT INTO persons
                (id, name, role, wikidata_qid, headshot_url, biography,
                 created_at, enriched_at, date_of_birth, date_of_death,
                 place_of_birth, place_of_death, nationality, is_pseudonym)
            VALUES
                (@id, @name, @role, @wikidataQid, @headshotUrl, @biography,
                 @createdAt, @enrichedAt, @dateOfBirth, @dateOfDeath,
                 @placeOfBirth, @placeOfDeath, @nationality, @isPseudonym);
            """,
            new
            {
                id           = person.Id.ToString(),
                person.Name,
                person.Role,
                wikidataQid  = person.WikidataQid,
                headshotUrl  = person.HeadshotUrl,
                biography    = person.Biography,
                createdAt    = person.CreatedAt.ToString("o"),
                enrichedAt   = person.EnrichedAt.HasValue ? person.EnrichedAt.Value.ToString("o") : null,
                dateOfBirth  = person.DateOfBirth,
                dateOfDeath  = person.DateOfDeath,
                placeOfBirth = person.PlaceOfBirth,
                placeOfDeath = person.PlaceOfDeath,
                nationality  = person.Nationality,
                isPseudonym  = person.IsPseudonym ? 1 : 0,
            });

        return Task.FromResult(person);
    }

    /// <inheritdoc/>
    public Task UpdateEnrichmentAsync(
        Guid personId,
        string? wikidataQid,
        string? headshotUrl,
        string? biography,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        conn.Execute("""
            UPDATE persons
            SET    wikidata_qid = @wikidataQid,
                   headshot_url = @headshotUrl,
                   biography    = @biography,
                   enriched_at  = @enrichedAt
            WHERE  id = @id;
            """,
            new
            {
                wikidataQid,
                headshotUrl,
                biography,
                enrichedAt = DateTimeOffset.UtcNow.ToString("o"),
                id         = personId.ToString(),
            });

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
        conn.Execute("""
            UPDATE persons
            SET    date_of_birth  = @dateOfBirth,
                   date_of_death  = @dateOfDeath,
                   place_of_birth = @placeOfBirth,
                   place_of_death = @placeOfDeath,
                   nationality    = @nationality,
                   is_pseudonym   = @isPseudonym
            WHERE  id = @id;
            """,
            new
            {
                dateOfBirth,
                dateOfDeath,
                placeOfBirth,
                placeOfDeath,
                nationality,
                isPseudonym = isPseudonym ? 1 : 0,
                id          = personId.ToString(),
            });

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
        conn.Execute("""
            UPDATE persons
            SET    occupation = COALESCE(@occupation, occupation),
                   instagram  = COALESCE(@instagram,  instagram),
                   twitter    = COALESCE(@twitter,    twitter),
                   tiktok     = COALESCE(@tiktok,     tiktok),
                   mastodon   = COALESCE(@mastodon,   mastodon),
                   website    = COALESCE(@website,    website)
            WHERE  id = @id;
            """,
            new
            {
                occupation,
                instagram,
                twitter,
                tiktok,
                mastodon,
                website,
                id = personId.ToString(),
            });

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
        conn.Execute("""
            INSERT OR IGNORE INTO person_media_links
                (media_asset_id, person_id, role)
            VALUES
                (@mediaAssetId, @personId, @role);
            """,
            new
            {
                mediaAssetId = mediaAssetId.ToString(),
                personId     = personId.ToString(),
                role,
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Person>> GetByMediaAssetAsync(
        Guid mediaAssetId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var results = conn.Query<Person>($"""
            SELECT p.id                  AS Id,
                   p.name                AS Name,
                   p.role                AS Role,
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
                   p.is_pseudonym        AS IsPseudonym
            FROM   persons p
            JOIN   person_media_links l ON l.person_id = p.id
            WHERE  l.media_asset_id = @mediaAssetId
            ORDER  BY p.name ASC;
            """, new { mediaAssetId = mediaAssetId.ToString() }).AsList();

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
        conn.Execute("""
            UPDATE persons
            SET    local_headshot_path = @path
            WHERE  id = @id;
            """,
            new
            {
                path,
                id = id.ToString(),
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<Person?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var result = conn.QueryFirstOrDefault<Person>($"""
            SELECT {SelectColumns}
            FROM   persons
            WHERE  id = @id
            LIMIT  1;
            """, new { id = id.ToString() });

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Person>> ListAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var results = conn.Query<Person>($"""
            SELECT {SelectColumns}
            FROM   persons
            ORDER  BY name ASC;
            """).AsList();

        return Task.FromResult<IReadOnlyList<Person>>(results);
    }

    /// <inheritdoc/>
    public Task<int> CountMediaLinksAsync(Guid personId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var count = conn.ExecuteScalar<int>("""
            SELECT COUNT(*) FROM person_media_links WHERE person_id = @id;
            """, new { id = personId.ToString() });

        return Task.FromResult(count);
    }

    /// <inheritdoc/>
    public Task<Person?> FindByQidAsync(string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(qid);

        using var conn = _db.CreateConnection();
        var result = conn.QueryFirstOrDefault<Person>($"""
            SELECT {SelectColumns}
            FROM   persons
            WHERE  wikidata_qid = @qid COLLATE NOCASE
            LIMIT  1;
            """, new { qid });

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(Guid personId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var id = personId.ToString();
        using var conn = _db.CreateConnection();

        // Delete links first (FK-safe even without ON DELETE CASCADE).
        conn.Execute(
            "DELETE FROM person_media_links WHERE person_id = @id;",
            new { id });

        conn.Execute(
            "DELETE FROM persons WHERE id = @id;",
            new { id });

        return Task.CompletedTask;
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
        conn.Execute("""
            INSERT OR IGNORE INTO person_aliases
                (pseudonym_person_id, real_person_id)
            VALUES
                (@pseudonymId, @realId);
            """,
            new
            {
                pseudonymId = pseudonymPersonId.ToString(),
                realId      = realPersonId.ToString(),
            });

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
        var results = conn.Query<Person>($"""
            SELECT p.id                  AS Id,
                   p.name                AS Name,
                   p.role                AS Role,
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
                   p.is_pseudonym        AS IsPseudonym
            FROM   persons p
            WHERE  p.id IN (
                SELECT real_person_id FROM person_aliases WHERE pseudonym_person_id = @id
                UNION
                SELECT pseudonym_person_id FROM person_aliases WHERE real_person_id = @id
            )
            ORDER  BY p.name ASC;
            """, new { id = personId.ToString() }).AsList();

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
        conn.Execute("""
            INSERT OR IGNORE INTO character_performer_links
                (person_id, fictional_entity_id, work_qid)
            VALUES
                (@personId, @entityId, @workQid);
            """,
            new
            {
                personId = personId.ToString(),
                entityId = fictionalEntityId.ToString(),
                workQid,
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<(Guid FictionalEntityId, string? WorkQid)>> GetCharacterLinksAsync(
        Guid personId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var rows = conn.Query<CharacterLinkRow>("""
            SELECT fictional_entity_id AS FictionalEntityId,
                   work_qid            AS WorkQid
            FROM   character_performer_links
            WHERE  person_id = @personId;
            """, new { personId = personId.ToString() }).AsList();

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
        conn.Execute(
            "UPDATE OR IGNORE person_media_links SET person_id = @to WHERE person_id = @from;",
            new { to, from }, transaction: tx);
        conn.Execute(
            "DELETE FROM person_media_links WHERE person_id = @from;",
            new { from }, transaction: tx);

        // 2. Reassign character-performer links
        conn.Execute(
            "UPDATE OR IGNORE character_performer_links SET person_id = @to WHERE person_id = @from;",
            new { to, from }, transaction: tx);
        conn.Execute(
            "DELETE FROM character_performer_links WHERE person_id = @from;",
            new { from }, transaction: tx);

        // 3. Reassign alias links (both directions)
        conn.Execute(
            "UPDATE OR IGNORE person_aliases SET pseudonym_person_id = @to WHERE pseudonym_person_id = @from;",
            new { to, from }, transaction: tx);
        conn.Execute(
            "UPDATE OR IGNORE person_aliases SET real_person_id = @to WHERE real_person_id = @from;",
            new { to, from }, transaction: tx);
        conn.Execute(
            "DELETE FROM person_aliases WHERE pseudonym_person_id = @from OR real_person_id = @from;",
            new { from }, transaction: tx);

        tx.Commit();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> IsPseudonymOrAliasAsync(Guid personId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var id = personId.ToString();
        using var conn = _db.CreateConnection();

        // Check persons.is_pseudonym flag first — cheapest query.
        var isPseudo = conn.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM persons WHERE id = @id AND is_pseudonym = 1;",
            new { id }) > 0;

        if (isPseudo)
            return Task.FromResult(true);

        // Check person_aliases in either direction — pen name or real author.
        var isAlias = conn.ExecuteScalar<int>("""
            SELECT COUNT(1) FROM person_aliases
            WHERE pseudonym_person_id = @id OR real_person_id = @id;
            """, new { id }) > 0;

        return Task.FromResult(isAlias);
    }
}
