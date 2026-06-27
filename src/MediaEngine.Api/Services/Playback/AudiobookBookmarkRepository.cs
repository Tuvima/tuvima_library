using Dapper;
using MediaEngine.Contracts.Playback;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Playback;

public sealed class AudiobookBookmarkRepository
{
    private readonly IDatabaseConnection _db;

    public AudiobookBookmarkRepository(IDatabaseConnection db)
    {
        _db = db;
    }

    public Task<IReadOnlyList<AudiobookBookmarkDto>> GetByWorkAsync(
        Guid profileId,
        Guid workId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        EnsureTables(conn);
        var rows = conn.Query<BookmarkRow>(
            """
            SELECT id AS Id,
                   profile_id AS ProfileId,
                   work_id AS WorkId,
                   asset_id AS AssetId,
                   chapter_index AS ChapterIndex,
                   chapter_title AS ChapterTitle,
                   position_seconds AS PositionSeconds,
                   duration_seconds AS DurationSeconds,
                   label AS Label,
                   created_at AS CreatedAt
            FROM audiobook_bookmarks
            WHERE profile_id = @profileId
              AND work_id = @workId
            ORDER BY created_at DESC;
            """,
            new { profileId, workId }).ToList();

        return Task.FromResult<IReadOnlyList<AudiobookBookmarkDto>>(rows.Select(ToDto).ToList());
    }

    public Task<AudiobookBookmarkDto> CreateAsync(
        Guid profileId,
        Guid workId,
        CreateAudiobookBookmarkRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        EnsureTables(conn);

        var now = DateTimeOffset.UtcNow;
        var bookmark = new BookmarkRow
        {
            Id = Guid.NewGuid(),
            ProfileId = profileId,
            WorkId = workId,
            AssetId = request.AssetId,
            ChapterIndex = request.ChapterIndex,
            ChapterTitle = BlankToNull(request.ChapterTitle),
            PositionSeconds = Math.Max(0, request.PositionSeconds),
            DurationSeconds = request.DurationSeconds is > 0 ? request.DurationSeconds : null,
            Label = BlankToNull(request.Label),
            CreatedAt = now,
        };

        conn.Execute(
            """
            INSERT INTO audiobook_bookmarks
                (id, profile_id, work_id, asset_id, chapter_index, chapter_title,
                 position_seconds, duration_seconds, label, created_at)
            VALUES
                (@Id, @ProfileId, @WorkId, @AssetId, @ChapterIndex, @ChapterTitle,
                 @PositionSeconds, @DurationSeconds, @Label, @CreatedAt);
            """,
            bookmark);

        return Task.FromResult(ToDto(bookmark));
    }

    public Task<bool> DeleteAsync(Guid profileId, Guid bookmarkId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        EnsureTables(conn);
        var affected = conn.Execute(
            """
            DELETE FROM audiobook_bookmarks
            WHERE profile_id = @profileId
              AND id = @bookmarkId;
            """,
            new { profileId, bookmarkId });
        return Task.FromResult(affected > 0);
    }

    private static void EnsureTables(System.Data.IDbConnection conn)
    {
        conn.Execute(
            """
            CREATE TABLE IF NOT EXISTS audiobook_bookmarks (
                id                 BLOB NOT NULL PRIMARY KEY,
                profile_id         BLOB NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
                work_id            BLOB NOT NULL REFERENCES works(id) ON DELETE CASCADE,
                asset_id           BLOB NOT NULL REFERENCES media_assets(id) ON DELETE CASCADE,
                chapter_index      INTEGER,
                chapter_title      TEXT,
                position_seconds   REAL NOT NULL DEFAULT 0.0,
                duration_seconds   REAL,
                label              TEXT,
                created_at         TEXT NOT NULL
            );
            """);

        conn.Execute(
            """
            CREATE INDEX IF NOT EXISTS idx_audiobook_bookmarks_profile_work
                ON audiobook_bookmarks (profile_id, work_id, created_at DESC);
            """);
    }

    private static AudiobookBookmarkDto ToDto(BookmarkRow row) => new()
    {
        Id = row.Id,
        ProfileId = row.ProfileId,
        WorkId = row.WorkId,
        AssetId = row.AssetId,
        ChapterIndex = row.ChapterIndex,
        ChapterTitle = row.ChapterTitle,
        PositionSeconds = row.PositionSeconds,
        DurationSeconds = row.DurationSeconds,
        Label = row.Label,
        CreatedAt = row.CreatedAt,
    };

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record BookmarkRow
    {
        public Guid Id { get; init; }
        public Guid ProfileId { get; init; }
        public Guid WorkId { get; init; }
        public Guid AssetId { get; init; }
        public int? ChapterIndex { get; init; }
        public string? ChapterTitle { get; init; }
        public double PositionSeconds { get; init; }
        public double? DurationSeconds { get; init; }
        public string? Label { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }
}
