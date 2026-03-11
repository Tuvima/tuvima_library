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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, user_id, asset_id, chapters_read, total_reading_time_secs,
                   words_read, sessions_count, avg_words_per_minute, last_session_at
            FROM   reader_statistics
            WHERE  user_id = @user_id AND asset_id = @asset_id
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@user_id", userId);
        cmd.Parameters.AddWithValue("@asset_id", assetId.ToString());

        using var reader = cmd.ExecuteReader();
        var result = reader.Read() ? MapStats(reader) : null;
        return Task.FromResult(result);
    }

    public Task UpsertAsync(ReaderStatistics stats, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO reader_statistics
                (id, user_id, asset_id, chapters_read, total_reading_time_secs,
                 words_read, sessions_count, avg_words_per_minute, last_session_at)
            VALUES
                (@id, @user_id, @asset_id, @chapters_read, @total_reading_time_secs,
                 @words_read, @sessions_count, @avg_words_per_minute, @last_session_at)
            ON CONFLICT(user_id, asset_id) DO UPDATE SET
                chapters_read           = @chapters_read,
                total_reading_time_secs = @total_reading_time_secs,
                words_read              = @words_read,
                sessions_count          = @sessions_count,
                avg_words_per_minute    = @avg_words_per_minute,
                last_session_at         = @last_session_at;
            """;
        cmd.Parameters.AddWithValue("@id", stats.Id.ToString());
        cmd.Parameters.AddWithValue("@user_id", stats.UserId);
        cmd.Parameters.AddWithValue("@asset_id", stats.AssetId.ToString());
        cmd.Parameters.AddWithValue("@chapters_read", stats.ChaptersRead);
        cmd.Parameters.AddWithValue("@total_reading_time_secs", stats.TotalReadingTimeSecs);
        cmd.Parameters.AddWithValue("@words_read", stats.WordsRead);
        cmd.Parameters.AddWithValue("@sessions_count", stats.SessionsCount);
        cmd.Parameters.AddWithValue("@avg_words_per_minute", stats.AvgWordsPerMinute);
        cmd.Parameters.AddWithValue("@last_session_at", stats.LastSessionAt.HasValue
            ? (object)stats.LastSessionAt.Value.ToString("O")
            : DBNull.Value);
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    private static ReaderStatistics MapStats(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id                   = Guid.Parse(r.GetString(0)),
        UserId               = r.GetString(1),
        AssetId              = Guid.Parse(r.GetString(2)),
        ChaptersRead         = r.GetInt32(3),
        TotalReadingTimeSecs = r.GetInt64(4),
        WordsRead            = r.GetInt64(5),
        SessionsCount        = r.GetInt32(6),
        AvgWordsPerMinute    = r.GetDouble(7),
        LastSessionAt        = r.IsDBNull(8) ? null : DateTime.Parse(r.GetString(8)),
    };
}
