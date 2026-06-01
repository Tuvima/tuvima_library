using Dapper;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

public interface ILibraryOverviewReadService
{
    Task<LibraryOverviewReadModel> GetOverviewAggregatesAsync(CancellationToken ct);
}

public sealed class LibraryOverviewReadService(IDatabaseConnection db) : ILibraryOverviewReadService
{
    public async Task<LibraryOverviewReadModel> GetOverviewAggregatesAsync(CancellationToken ct)
    {
        using var conn = db.CreateConnection();

        var now = DateTimeOffset.UtcNow;
        var since24h = now.AddHours(-24).ToString("O");
        var since7d = now.AddDays(-7).ToString("O");
        var since30d = now.AddDays(-30).ToString("O");

        const string RecentlyAddedSql = """
            SELECT COUNT(*)
            FROM (
                SELECT e.work_id,
                       MIN(mc.claimed_at) AS first_claimed_at
                FROM editions e
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                INNER JOIN metadata_claims mc ON mc.entity_id = ma.id
                GROUP BY e.work_id
            ) added
            WHERE julianday(added.first_claimed_at) >= julianday(@since);
            """;

        var added24h = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(RecentlyAddedSql, new { since = since24h }, cancellationToken: ct));
        var added7d = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(RecentlyAddedSql, new { since = since7d }, cancellationToken: ct));
        var added30d = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(RecentlyAddedSql, new { since = since30d }, cancellationToken: ct));

        var pipelineRows = await conn.QueryAsync<(string State, int Count)>(new CommandDefinition(
            """
            SELECT state AS State, COUNT(*) AS Count
            FROM identity_jobs
            GROUP BY state
            """,
            cancellationToken: ct));

        var pipelineStates = pipelineRows.ToDictionary(row => row.State, row => row.Count);
        pipelineStates.TryGetValue("Ready", out var readyCount);
        pipelineStates.TryGetValue("ReadyWithoutUniverse", out var readyWithoutUniverseCount);
        pipelineStates.TryGetValue("Failed", out var failedCount);
        var successfulCount = readyCount + readyWithoutUniverseCount;
        var pipelineTotal = successfulCount + failedCount;
        var successRate = pipelineTotal > 0 ? (double)successfulCount / pipelineTotal : 1.0;

        return new LibraryOverviewReadModel(
            added24h,
            added7d,
            added30d,
            pipelineStates,
            Math.Round(successRate, 4));
    }
}

public sealed record LibraryOverviewReadModel(
    int Added24h,
    int Added7d,
    int Added30d,
    IReadOnlyDictionary<string, int> PipelineStates,
    double PipelineSuccessRate);
