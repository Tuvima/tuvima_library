using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IWikidataCandidateRepository"/>.
/// Persists every Wikidata entity evaluated during Stage 2 bridge resolution.
/// </summary>
public sealed class WikidataCandidateRepository : IWikidataCandidateRepository
{
    private readonly IDatabaseConnection _db;

    public WikidataCandidateRepository(IDatabaseConnection db) => _db = db;

    public Task InsertBatchAsync(IReadOnlyList<WikidataBridgeCandidate> candidates, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        foreach (var c in candidates)
        {
            conn.Execute("""
                INSERT INTO wikidata_bridge_candidates
                    (id, job_id, qid, label, description, matched_by, bridge_id_type,
                     is_exact_match, score_total, score_breakdown_json, outcome, created_at)
                VALUES
                    (@Id, @JobId, @Qid, @Label, @Description, @MatchedBy, @BridgeIdType,
                     @IsExactMatch, @ScoreTotal, @ScoreBreakdownJson, @Outcome, @CreatedAt);
                """,
                new
                {
                    Id           = c.Id.ToString(),
                    JobId        = c.JobId.ToString(),
                    c.Qid,
                    c.Label,
                    c.Description,
                    c.MatchedBy,
                    c.BridgeIdType,
                    IsExactMatch = c.IsExactMatch ? 1 : 0,
                    c.ScoreTotal,
                    c.ScoreBreakdownJson,
                    c.Outcome,
                    CreatedAt    = c.CreatedAt.ToString("O"),
                }, tx);
        }

        tx.Commit();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WikidataBridgeCandidate>> GetByJobAsync(Guid jobId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = conn.Query<WikidataCandidateRow>(SelectSql + " WHERE job_id = @jobId ORDER BY score_total DESC;",
            new { jobId = jobId.ToString() });
        IReadOnlyList<WikidataBridgeCandidate> result = rows.Select(MapRow).ToList();
        return Task.FromResult(result);
    }

    public Task<WikidataBridgeCandidate?> GetSelectedAsync(Guid jobId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<WikidataCandidateRow>(
            SelectSql + " WHERE job_id = @jobId AND outcome = 'AutoAccepted' ORDER BY score_total DESC LIMIT 1;",
            new { jobId = jobId.ToString() });
        return Task.FromResult(row is null ? null : MapRow(row));
    }

    public Task<WikidataBridgeCandidate?> GetByIdAsync(Guid candidateId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<WikidataCandidateRow>(
            SelectSql + " WHERE id = @candidateId LIMIT 1;",
            new { candidateId = candidateId.ToString() });
        return Task.FromResult(row is null ? null : MapRow(row));
    }

    // ── Shared SELECT prefix ─────────────────────────────────────────────────

    private const string SelectSql = """
        SELECT id                    AS Id,
               job_id                AS JobId,
               qid                   AS Qid,
               label                 AS Label,
               description           AS Description,
               matched_by            AS MatchedBy,
               bridge_id_type        AS BridgeIdType,
               is_exact_match        AS IsExactMatch,
               score_total           AS ScoreTotal,
               score_breakdown_json  AS ScoreBreakdownJson,
               outcome               AS Outcome,
               created_at            AS CreatedAt
        FROM   wikidata_bridge_candidates
        """;

    // ── Private intermediate row type and mapper ─────────────────────────────

    private sealed class WikidataCandidateRow
    {
        public string  Id                 { get; set; } = "";
        public string  JobId              { get; set; } = "";
        public string  Qid                { get; set; } = "";
        public string  Label              { get; set; } = "";
        public string? Description        { get; set; }
        public string  MatchedBy          { get; set; } = "";
        public string? BridgeIdType       { get; set; }
        public long    IsExactMatch       { get; set; }
        public double  ScoreTotal         { get; set; }
        public string? ScoreBreakdownJson { get; set; }
        public string  Outcome            { get; set; } = "";
        public string  CreatedAt          { get; set; } = "";
    }

    private static WikidataBridgeCandidate MapRow(WikidataCandidateRow r) => new()
    {
        Id                 = Guid.Parse(r.Id),
        JobId              = Guid.Parse(r.JobId),
        Qid                = r.Qid,
        Label              = r.Label,
        Description        = r.Description,
        MatchedBy          = r.MatchedBy,
        BridgeIdType       = r.BridgeIdType,
        IsExactMatch       = r.IsExactMatch != 0,
        ScoreTotal         = r.ScoreTotal,
        ScoreBreakdownJson = r.ScoreBreakdownJson,
        Outcome            = r.Outcome,
        CreatedAt          = DateTimeOffset.Parse(r.CreatedAt),
    };
}
