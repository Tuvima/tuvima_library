using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

public interface IOrphanImageReferenceReadService
{
    Task<OrphanImageReferenceSet> GetKnownReferencesAsync(CancellationToken ct);
}

public sealed class OrphanImageReferenceReadService(IDatabaseConnection db) : IOrphanImageReferenceReadService
{
    public async Task<OrphanImageReferenceSet> GetKnownReferencesAsync(CancellationToken ct)
    {
        using var conn = db.CreateConnection();

        var knownWorkQids = await ReadStringSetAsync(
            conn,
            "SELECT DISTINCT wikidata_qid FROM works WHERE wikidata_qid IS NOT NULL",
            ct);

        var knownWorkId12 = await ReadStringSetAsync(
            conn,
            """
            SELECT DISTINCT LOWER(SUBSTR(REPLACE(ma.id, '-', ''), 1, 12))
            FROM media_assets ma
            INNER JOIN editions e ON e.id = ma.edition_id
            INNER JOIN works w ON w.id = e.work_id
            WHERE w.wikidata_qid IS NULL
            """,
            ct);

        var knownPersonQids = await ReadStringSetAsync(
            conn,
            "SELECT DISTINCT wikidata_qid FROM persons WHERE wikidata_qid IS NOT NULL",
            ct);

        var knownUniverseQids = await ReadStringSetAsync(
            conn,
            """
            SELECT DISTINCT wikidata_qid FROM collections
            WHERE wikidata_qid IS NOT NULL AND parent_collection_id IS NOT NULL
            UNION
            SELECT DISTINCT wikidata_qid FROM collections
            WHERE wikidata_qid IS NOT NULL AND id IN (
                SELECT DISTINCT parent_collection_id FROM collections WHERE parent_collection_id IS NOT NULL
            )
            """,
            ct);

        return new OrphanImageReferenceSet(
            knownWorkQids,
            knownWorkId12,
            knownPersonQids,
            knownUniverseQids);
    }

    private static async Task<HashSet<string>> ReadStringSetAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        string sql,
        CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!reader.IsDBNull(0))
            {
                values.Add(reader.GetString(0));
            }
        }

        return values;
    }
}

public sealed record OrphanImageReferenceSet(
    HashSet<string> KnownWorkQids,
    HashSet<string> KnownWorkId12,
    HashSet<string> KnownPersonQids,
    HashSet<string> KnownUniverseQids);
