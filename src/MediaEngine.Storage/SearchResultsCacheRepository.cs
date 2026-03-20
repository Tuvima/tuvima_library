using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class SearchResultsCacheRepository : ISearchResultsCacheRepository
{
    private readonly IDatabaseConnection _db;

    public SearchResultsCacheRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public Task<string?> FindAsync(Guid entityId, int maxAgeDays = 30, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-maxAgeDays).ToString("o");
        var result = conn.QueryFirstOrDefault<string>(
            """
            SELECT results_json FROM search_results_cache
            WHERE entity_id = @entityId AND searched_at >= @cutoff
            """,
            new { entityId = entityId.ToString(), cutoff });
        return Task.FromResult(result);
    }

    public Task UpsertAsync(Guid entityId, string resultsJson, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        conn.Execute(
            """
            INSERT INTO search_results_cache (entity_id, results_json, searched_at)
            VALUES (@entityId, @resultsJson, @searchedAt)
            ON CONFLICT(entity_id) DO UPDATE SET
                results_json = excluded.results_json,
                searched_at  = excluded.searched_at
            """,
            new
            {
                entityId = entityId.ToString(),
                resultsJson,
                searchedAt = DateTimeOffset.UtcNow.ToString("o"),
            });
        return Task.CompletedTask;
    }

    public Task<int> PurgeExpiredAsync(int maxAgeDays = 30, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-maxAgeDays).ToString("o");
        var count = conn.Execute(
            "DELETE FROM search_results_cache WHERE searched_at < @cutoff",
            new { cutoff });
        return Task.FromResult(count);
    }
}
