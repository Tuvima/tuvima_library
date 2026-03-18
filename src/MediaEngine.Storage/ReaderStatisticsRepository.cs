using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IReaderStatisticsRepository"/>.
/// Uses INSERT OR REPLACE on the (user_id, asset_id) unique constraint.
/// </summary>
public sealed class ReaderStatisticsRepository : IReaderStatisticsRepository
{
    private readonly IDatabaseConnection _db;

    public ReaderStatisticsRepository(IDatabaseConnection db) => _db = db;

    public Task<ReaderStatistics?> GetAsync(string userId, Guid assetId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<StatsRow>("""
            SELECT id                       AS Id,
                   user_id                  AS UserId,
                   asset_id                 AS AssetId,
                   chapters_read            AS ChaptersRead,
                   total_reading_time_secs  AS TotalReadingTimeSecs,
                   words_read               AS WordsRead,
                   sessions_count           AS SessionsCount,
                   avg_words_per_minute     AS AvgWordsPerMinute,
                   last_session_at          AS LastSessionAt
            FROM   reader_statistics
            WHERE  user_id = @userId AND asset_id = @assetId
            LIMIT  1;
            """, new { userId, assetId = assetId.ToString() });

        return Task.FromResult(row is null ? null : MapRow(row));
    }

    public Task UpsertAsync(ReaderStatistics stats, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT INTO reader_statistics
                (id, user_id, asset_id, chapters_read, total_reading_time_secs,
                 words_read, sessions_count, avg_words_per_minute, last_session_at)
            VALUES
                (@id, @userId, @assetId, @chaptersRead, @totalReadingTimeSecs,
                 @wordsRead, @sessionsCount, @avgWordsPerMinute, @lastSessionAt)
            ON CONFLICT(user_id, asset_id) DO UPDATE SET
                chapters_read           = @chaptersRead,
                total_reading_time_secs = @totalReadingTimeSecs,
                words_read              = @wordsRead,
                sessions_count          = @sessionsCount,
                avg_words_per_minute    = @avgWordsPerMinute,
                last_session_at         = @lastSessionAt;
            """, new
        {
            id                   = stats.Id.ToString(),
            userId               = stats.UserId,
            assetId              = stats.AssetId.ToString(),
            chaptersRead         = stats.ChaptersRead,
            totalReadingTimeSecs = stats.TotalReadingTimeSecs,
            wordsRead            = stats.WordsRead,
            sessionsCount        = stats.SessionsCount,
            avgWordsPerMinute    = stats.AvgWordsPerMinute,
            lastSessionAt        = stats.LastSessionAt.HasValue
                                       ? (object)stats.LastSessionAt.Value.ToString("O")
                                       : null,
        });

        return Task.CompletedTask;
    }

    // ── Private DTO + mapper ─────────────────────────────────────────────────
    // SQLite stores Guid and DateTime as TEXT strings; Dapper cannot auto-convert
    // them to Guid/DateTime, so we read into a flat string DTO and convert in code.

    private sealed class StatsRow
    {
        public string  Id                   { get; set; } = string.Empty;
        public string  UserId               { get; set; } = string.Empty;
        public string  AssetId              { get; set; } = string.Empty;
        public int     ChaptersRead         { get; set; }
        public long    TotalReadingTimeSecs { get; set; }
        public long    WordsRead            { get; set; }
        public int     SessionsCount        { get; set; }
        public double  AvgWordsPerMinute    { get; set; }
        public string? LastSessionAt        { get; set; }
    }

    private static ReaderStatistics MapRow(StatsRow r) => new()
    {
        Id                   = Guid.Parse(r.Id),
        UserId               = r.UserId,
        AssetId              = Guid.Parse(r.AssetId),
        ChaptersRead         = r.ChaptersRead,
        TotalReadingTimeSecs = r.TotalReadingTimeSecs,
        WordsRead            = r.WordsRead,
        SessionsCount        = r.SessionsCount,
        AvgWordsPerMinute    = r.AvgWordsPerMinute,
        LastSessionAt        = r.LastSessionAt is null ? null : DateTime.Parse(r.LastSessionAt),
    };
}
