using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IRetailCandidateRepository"/>.
/// Persists every retail provider candidate from Stage 1 with full score breakdowns.
/// </summary>
public sealed class RetailCandidateRepository : IRetailCandidateRepository
{
    private readonly IDatabaseConnection _db;

    public RetailCandidateRepository(IDatabaseConnection db) => _db = db;

    public Task InsertBatchAsync(IReadOnlyList<RetailMatchCandidate> candidates, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        foreach (var c in candidates)
        {
            conn.Execute("""
                INSERT INTO retail_match_candidates
                    (id, job_id, provider_id, provider_name, provider_item_id, rank,
                     title, creator, year, score_total, score_breakdown_json,
                     bridge_ids_json, description, image_url, outcome, created_at)
                VALUES
                    (@Id, @JobId, @ProviderId, @ProviderName, @ProviderItemId, @Rank,
                     @Title, @Creator, @Year, @ScoreTotal, @ScoreBreakdownJson,
                     @BridgeIdsJson, @Description, @ImageUrl, @Outcome, @CreatedAt);
                """,
                new
                {
                    Id         = c.Id.ToString(),
                    JobId      = c.JobId.ToString(),
                    ProviderId = c.ProviderId.ToString(),
                    c.ProviderName,
                    c.ProviderItemId,
                    c.Rank,
                    c.Title,
                    c.Creator,
                    c.Year,
                    c.ScoreTotal,
                    c.ScoreBreakdownJson,
                    c.BridgeIdsJson,
                    c.Description,
                    c.ImageUrl,
                    c.Outcome,
                    CreatedAt = c.CreatedAt.ToString("O"),
                }, tx);
        }

        tx.Commit();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RetailMatchCandidate>> GetByJobAsync(Guid jobId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = conn.Query<RetailCandidateRow>(SelectSql + " WHERE job_id = @jobId ORDER BY score_total DESC;",
            new { jobId = jobId.ToString() });
        IReadOnlyList<RetailMatchCandidate> result = rows.Select(MapRow).ToList();
        return Task.FromResult(result);
    }

    public Task<RetailMatchCandidate?> GetSelectedAsync(Guid jobId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<RetailCandidateRow>(
            SelectSql + " WHERE job_id = @jobId AND outcome IN ('AutoAccepted', 'Ambiguous') ORDER BY score_total DESC LIMIT 1;",
            new { jobId = jobId.ToString() });
        return Task.FromResult(row is null ? null : MapRow(row));
    }

    public Task<RetailMatchCandidate?> GetByIdAsync(Guid candidateId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<RetailCandidateRow>(
            SelectSql + " WHERE id = @candidateId LIMIT 1;",
            new { candidateId = candidateId.ToString() });
        return Task.FromResult(row is null ? null : MapRow(row));
    }

    // ── Shared SELECT prefix ─────────────────────────────────────────────────

    private const string SelectSql = """
        SELECT id                    AS Id,
               job_id                AS JobId,
               provider_id           AS ProviderId,
               provider_name         AS ProviderName,
               provider_item_id      AS ProviderItemId,
               rank                  AS Rank,
               title                 AS Title,
               creator               AS Creator,
               year                  AS Year,
               score_total           AS ScoreTotal,
               score_breakdown_json  AS ScoreBreakdownJson,
               bridge_ids_json       AS BridgeIdsJson,
               description           AS Description,
               image_url             AS ImageUrl,
               outcome               AS Outcome,
               created_at            AS CreatedAt
        FROM   retail_match_candidates
        """;

    // ── Private intermediate row type and mapper ─────────────────────────────

    private sealed class RetailCandidateRow
    {
        public string  Id                 { get; set; } = "";
        public string  JobId              { get; set; } = "";
        public string  ProviderId         { get; set; } = "";
        public string  ProviderName       { get; set; } = "";
        public string? ProviderItemId     { get; set; }
        public int     Rank               { get; set; }
        public string  Title              { get; set; } = "";
        public string? Creator            { get; set; }
        public string? Year               { get; set; }
        public double  ScoreTotal         { get; set; }
        public string? ScoreBreakdownJson { get; set; }
        public string? BridgeIdsJson      { get; set; }
        public string? Description        { get; set; }
        public string? ImageUrl           { get; set; }
        public string  Outcome            { get; set; } = "";
        public string  CreatedAt          { get; set; } = "";
    }

    private static RetailMatchCandidate MapRow(RetailCandidateRow r) => new()
    {
        Id                 = Guid.Parse(r.Id),
        JobId              = Guid.Parse(r.JobId),
        ProviderId         = Guid.Parse(r.ProviderId),
        ProviderName       = r.ProviderName,
        ProviderItemId     = r.ProviderItemId,
        Rank               = r.Rank,
        Title              = r.Title,
        Creator            = r.Creator,
        Year               = r.Year,
        ScoreTotal         = r.ScoreTotal,
        ScoreBreakdownJson = r.ScoreBreakdownJson,
        BridgeIdsJson      = r.BridgeIdsJson,
        Description        = r.Description,
        ImageUrl           = r.ImageUrl,
        Outcome            = r.Outcome,
        CreatedAt          = DateTimeOffset.Parse(r.CreatedAt),
    };
}
