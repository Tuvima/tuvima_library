using Microsoft.Data.Sqlite;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// ORM-less SQLite implementation of <see cref="IPersonRepository"/>.
///
/// Persons are looked up by (name, role) at ingestion time and enriched
/// asynchronously by the Wikidata adapter.  Person-to-asset links live in
/// the <c>person_media_links</c> junction table.
///
/// Thread safety: same serialised-connection model as <see cref="MediaAssetRepository"/>.
/// Spec: Phase 9 – Recursive Person Enrichment.
/// </summary>
public sealed class PersonRepository : IPersonRepository
{
    private readonly IDatabaseConnection _db;

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
        using var cmd = conn.CreateCommand();
        // COLLATE NOCASE: SQLite case-insensitive comparison for ASCII names.
        // For non-ASCII author names (e.g. Björn) this gives best-effort matching.
        cmd.CommandText = """
            SELECT id, name, role, wikidata_qid, headshot_url, biography,
                   created_at, enriched_at, occupation, instagram, twitter,
                   tiktok, mastodon, website, local_headshot_path,
                   date_of_birth, date_of_death, place_of_birth,
                   place_of_death, nationality, is_pseudonym,
                   date_of_birth, date_of_death, place_of_birth,
                   place_of_death, nationality, is_pseudonym
            FROM   persons
            WHERE  name = @name COLLATE NOCASE
              AND  role = @role COLLATE NOCASE
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@role", role);

        using var reader = cmd.ExecuteReader();
        var result = reader.Read() ? MapRow(reader) : null;
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<Person> CreateAsync(Person person, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(person);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO persons
                (id, name, role, wikidata_qid, headshot_url, biography,
                 created_at, enriched_at, date_of_birth, date_of_death,
                 place_of_birth, place_of_death, nationality, is_pseudonym)
            VALUES
                (@id, @name, @role, @wikidata_qid, @headshot_url, @biography,
                 @created_at, @enriched_at, @date_of_birth, @date_of_death,
                 @place_of_birth, @place_of_death, @nationality, @is_pseudonym);
            """;
        cmd.Parameters.AddWithValue("@id",             person.Id.ToString());
        cmd.Parameters.AddWithValue("@name",           person.Name);
        cmd.Parameters.AddWithValue("@role",           person.Role);
        cmd.Parameters.AddWithValue("@wikidata_qid",   person.WikidataQid   as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@headshot_url",   person.HeadshotUrl   as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@biography",      person.Biography     as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created_at",     person.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@enriched_at",
            person.EnrichedAt.HasValue
                ? person.EnrichedAt.Value.ToString("o")
                : DBNull.Value);
        cmd.Parameters.AddWithValue("@date_of_birth",  person.DateOfBirth   as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@date_of_death",  person.DateOfDeath   as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@place_of_birth", person.PlaceOfBirth  as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@place_of_death", person.PlaceOfDeath  as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nationality",    person.Nationality   as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@is_pseudonym",   person.IsPseudonym ? 1 : 0);

        cmd.ExecuteNonQuery();
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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE persons
            SET    wikidata_qid = @wikidata_qid,
                   headshot_url = @headshot_url,
                   biography    = @biography,
                   enriched_at  = @enriched_at
            WHERE  id = @id;
            """;
        cmd.Parameters.AddWithValue("@wikidata_qid", wikidataQid as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@headshot_url", headshotUrl as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@biography",    biography   as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@enriched_at",  DateTimeOffset.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@id",           personId.ToString());
        cmd.ExecuteNonQuery();

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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE persons
            SET    date_of_birth  = @date_of_birth,
                   date_of_death  = @date_of_death,
                   place_of_birth = @place_of_birth,
                   place_of_death = @place_of_death,
                   nationality    = @nationality,
                   is_pseudonym   = @is_pseudonym
            WHERE  id = @id;
            """;
        cmd.Parameters.AddWithValue("@date_of_birth",  dateOfBirth  as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@date_of_death",  dateOfDeath  as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@place_of_birth", placeOfBirth as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@place_of_death", placeOfDeath as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nationality",    nationality  as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@is_pseudonym",   isPseudonym ? 1 : 0);
        cmd.Parameters.AddWithValue("@id",             personId.ToString());
        cmd.ExecuteNonQuery();

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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE persons
            SET    occupation = COALESCE(@occupation, occupation),
                   instagram  = COALESCE(@instagram,  instagram),
                   twitter    = COALESCE(@twitter,    twitter),
                   tiktok     = COALESCE(@tiktok,     tiktok),
                   mastodon   = COALESCE(@mastodon,   mastodon),
                   website    = COALESCE(@website,    website)
            WHERE  id = @id;
            """;
        cmd.Parameters.AddWithValue("@occupation", occupation as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@instagram",  instagram  as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@twitter",    twitter    as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tiktok",     tiktok     as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mastodon",   mastodon   as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@website",    website    as object ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id",         personId.ToString());
        cmd.ExecuteNonQuery();

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
        using var cmd = conn.CreateCommand();
        // INSERT OR IGNORE: composite PK (media_asset_id, person_id, role) prevents
        // duplicate links; repeated calls for the same triplet are safe no-ops.
        cmd.CommandText = """
            INSERT OR IGNORE INTO person_media_links
                (media_asset_id, person_id, role)
            VALUES
                (@media_asset_id, @person_id, @role);
            """;
        cmd.Parameters.AddWithValue("@media_asset_id", mediaAssetId.ToString());
        cmd.Parameters.AddWithValue("@person_id",       personId.ToString());
        cmd.Parameters.AddWithValue("@role",            role);
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Person>> GetByMediaAssetAsync(
        Guid mediaAssetId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT p.id, p.name, p.role, p.wikidata_qid, p.headshot_url, p.biography,
                   p.created_at, p.enriched_at, p.occupation, p.instagram, p.twitter,
                   p.tiktok, p.mastodon, p.website, p.local_headshot_path,
                   p.date_of_birth, p.date_of_death, p.place_of_birth,
                   p.place_of_death, p.nationality, p.is_pseudonym
            FROM   persons p
            JOIN   person_media_links l ON l.person_id = p.id
            WHERE  l.media_asset_id = @media_asset_id
            ORDER  BY p.name ASC;
            """;
        cmd.Parameters.AddWithValue("@media_asset_id", mediaAssetId.ToString());

        var results = new List<Person>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            results.Add(MapRow(reader));
        }

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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE persons
            SET    local_headshot_path = @path
            WHERE  id = @id;
            """;
        cmd.Parameters.AddWithValue("@path", path);
        cmd.Parameters.AddWithValue("@id",   id.ToString());
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<Person?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, role, wikidata_qid, headshot_url, biography,
                   created_at, enriched_at, occupation, instagram, twitter,
                   tiktok, mastodon, website, local_headshot_path,
                   date_of_birth, date_of_death, place_of_birth,
                   place_of_death, nationality, is_pseudonym
            FROM   persons
            WHERE  id = @id
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = cmd.ExecuteReader();
        var result = reader.Read() ? MapRow(reader) : null;
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Person>> ListAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, role, wikidata_qid, headshot_url, biography,
                   created_at, enriched_at, occupation, instagram, twitter,
                   tiktok, mastodon, website, local_headshot_path,
                   date_of_birth, date_of_death, place_of_birth,
                   place_of_death, nationality, is_pseudonym
            FROM   persons
            ORDER  BY name ASC;
            """;

        var results = new List<Person>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            results.Add(MapRow(reader));
        }

        return Task.FromResult<IReadOnlyList<Person>>(results);
    }

    /// <inheritdoc/>
    public Task<int> CountMediaLinksAsync(Guid personId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM person_media_links WHERE person_id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", personId.ToString());

        var count = Convert.ToInt32(cmd.ExecuteScalar());
        return Task.FromResult(count);
    }

    /// <inheritdoc/>
    public Task<Person?> FindByQidAsync(string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(qid);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, role, wikidata_qid, headshot_url, biography,
                   created_at, enriched_at, occupation, instagram, twitter,
                   tiktok, mastodon, website, local_headshot_path,
                   date_of_birth, date_of_death, place_of_birth,
                   place_of_death, nationality, is_pseudonym
            FROM   persons
            WHERE  wikidata_qid = @qid COLLATE NOCASE
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@qid", qid);

        using var reader = cmd.ExecuteReader();
        var result = reader.Read() ? MapRow(reader) : null;
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(Guid personId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();

        // Delete links first (FK-safe even without ON DELETE CASCADE).
        using var linksCmd = conn.CreateCommand();
        linksCmd.CommandText = "DELETE FROM person_media_links WHERE person_id = @id;";
        linksCmd.Parameters.AddWithValue("@id", personId.ToString());
        linksCmd.ExecuteNonQuery();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM persons WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", personId.ToString());
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Private row mapper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps the current reader row to a <see cref="Person"/>.
    /// Column ordinals match the SELECT list used in every query in this class:
    ///   0=id, 1=name, 2=role, 3=wikidata_qid, 4=headshot_url, 5=biography,
    ///   6=created_at, 7=enriched_at, 8=occupation, 9=instagram, 10=twitter,
    ///   11=tiktok, 12=mastodon, 13=website, 14=local_headshot_path,
    ///   15=date_of_birth, 16=date_of_death, 17=place_of_birth,
    ///   18=place_of_death, 19=nationality, 20=is_pseudonym
    /// </summary>
    private static Person MapRow(SqliteDataReader r) => new()
    {
        Id                = Guid.Parse(r.GetString(0)),
        Name              = r.GetString(1),
        Role              = r.GetString(2),
        WikidataQid       = r.IsDBNull(3)  ? null : r.GetString(3),
        HeadshotUrl       = r.IsDBNull(4)  ? null : r.GetString(4),
        Biography         = r.IsDBNull(5)  ? null : r.GetString(5),
        CreatedAt         = DateTimeOffset.Parse(r.GetString(6)),
        EnrichedAt        = r.IsDBNull(7)  ? null : DateTimeOffset.Parse(r.GetString(7)),
        Occupation        = r.IsDBNull(8)  ? null : r.GetString(8),
        Instagram         = r.IsDBNull(9)  ? null : r.GetString(9),
        Twitter           = r.IsDBNull(10) ? null : r.GetString(10),
        TikTok            = r.IsDBNull(11) ? null : r.GetString(11),
        Mastodon          = r.IsDBNull(12) ? null : r.GetString(12),
        Website           = r.IsDBNull(13) ? null : r.GetString(13),
        LocalHeadshotPath = r.IsDBNull(14) ? null : r.GetString(14),
        DateOfBirth       = r.IsDBNull(15) ? null : r.GetString(15),
        DateOfDeath       = r.IsDBNull(16) ? null : r.GetString(16),
        PlaceOfBirth      = r.IsDBNull(17) ? null : r.GetString(17),
        PlaceOfDeath      = r.IsDBNull(18) ? null : r.GetString(18),
        Nationality       = r.IsDBNull(19) ? null : r.GetString(19),
        IsPseudonym       = !r.IsDBNull(20) && r.GetInt32(20) != 0,
    };

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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO person_aliases
                (pseudonym_person_id, real_person_id)
            VALUES
                (@pseudonym_id, @real_id);
            """;
        cmd.Parameters.AddWithValue("@pseudonym_id", pseudonymPersonId.ToString());
        cmd.Parameters.AddWithValue("@real_id",      realPersonId.ToString());
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Person>> FindAliasesAsync(
        Guid personId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        // Returns both directions: real people behind a pseudonym,
        // and pseudonyms used by a real person.
        cmd.CommandText = """
            SELECT p.id, p.name, p.role, p.wikidata_qid, p.headshot_url, p.biography,
                   p.created_at, p.enriched_at, p.occupation, p.instagram, p.twitter,
                   p.tiktok, p.mastodon, p.website, p.local_headshot_path,
                   p.date_of_birth, p.date_of_death, p.place_of_birth,
                   p.place_of_death, p.nationality, p.is_pseudonym
            FROM   persons p
            WHERE  p.id IN (
                SELECT real_person_id FROM person_aliases WHERE pseudonym_person_id = @id
                UNION
                SELECT pseudonym_person_id FROM person_aliases WHERE real_person_id = @id
            )
            ORDER  BY p.name ASC;
            """;
        cmd.Parameters.AddWithValue("@id", personId.ToString());

        var results = new List<Person>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            results.Add(MapRow(reader));
        }

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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO character_performer_links
                (person_id, fictional_entity_id, work_qid)
            VALUES
                (@person_id, @entity_id, @work_qid);
            """;
        cmd.Parameters.AddWithValue("@person_id", personId.ToString());
        cmd.Parameters.AddWithValue("@entity_id", fictionalEntityId.ToString());
        cmd.Parameters.AddWithValue("@work_qid",  workQid as object ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<(Guid FictionalEntityId, string? WorkQid)>> GetCharacterLinksAsync(
        Guid personId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT fictional_entity_id, work_qid
            FROM   character_performer_links
            WHERE  person_id = @person_id;
            """;
        cmd.Parameters.AddWithValue("@person_id", personId.ToString());

        var results = new List<(Guid, string?)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            results.Add((
                Guid.Parse(reader.GetString(0)),
                reader.IsDBNull(1) ? null : reader.GetString(1)
            ));
        }

        return Task.FromResult<IReadOnlyList<(Guid FictionalEntityId, string? WorkQid)>>(results);
    }
}
