using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IReaderBookmarkRepository"/>.
/// </summary>
public sealed class ReaderBookmarkRepository : IReaderBookmarkRepository
{
    private readonly IDatabaseConnection _db;

    public ReaderBookmarkRepository(IDatabaseConnection db) => _db = db;

    public Task<IReadOnlyList<ReaderBookmark>> ListByAssetAsync(string userId, Guid assetId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = conn.Query<BookmarkRow>("""
            SELECT id            AS Id,
                   user_id       AS UserId,
                   asset_id      AS AssetId,
                   chapter_index AS ChapterIndex,
                   cfi_position  AS CfiPosition,
                   label         AS Label,
                   created_at    AS CreatedAt
            FROM   reader_bookmarks
            WHERE  user_id = @userId AND asset_id = @assetId
            ORDER BY created_at DESC;
            """, new { userId, assetId = assetId.ToString() }).AsList();

        return Task.FromResult<IReadOnlyList<ReaderBookmark>>(rows.ConvertAll(MapRow));
    }

    public Task<ReaderBookmark?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<BookmarkRow>("""
            SELECT id            AS Id,
                   user_id       AS UserId,
                   asset_id      AS AssetId,
                   chapter_index AS ChapterIndex,
                   cfi_position  AS CfiPosition,
                   label         AS Label,
                   created_at    AS CreatedAt
            FROM   reader_bookmarks
            WHERE  id = @id
            LIMIT  1;
            """, new { id = id.ToString() });

        return Task.FromResult(row is null ? null : MapRow(row));
    }

    public Task InsertAsync(ReaderBookmark bookmark, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT INTO reader_bookmarks (id, user_id, asset_id, chapter_index, cfi_position, label, created_at)
            VALUES (@id, @userId, @assetId, @chapterIndex, @cfiPosition, @label, @createdAt);
            """, new
        {
            id           = bookmark.Id.ToString(),
            userId       = bookmark.UserId,
            assetId      = bookmark.AssetId.ToString(),
            chapterIndex = bookmark.ChapterIndex,
            cfiPosition  = bookmark.CfiPosition,
            label        = bookmark.Label,
            createdAt    = bookmark.CreatedAt.ToString("O"),
        });

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        conn.Execute(
            "DELETE FROM reader_bookmarks WHERE id = @id;",
            new { id = id.ToString() });

        return Task.CompletedTask;
    }

    // ── Private DTO + mapper ─────────────────────────────────────────────────
    // SQLite stores Guid and DateTime as TEXT strings; Dapper cannot auto-convert
    // them to Guid/DateTime, so we read into a flat string DTO and convert in code.

    private sealed class BookmarkRow
    {
        public string  Id           { get; set; } = string.Empty;
        public string  UserId       { get; set; } = string.Empty;
        public string  AssetId      { get; set; } = string.Empty;
        public int     ChapterIndex { get; set; }
        public string? CfiPosition  { get; set; }
        public string? Label        { get; set; }
        public string  CreatedAt    { get; set; } = string.Empty;
    }

    private static ReaderBookmark MapRow(BookmarkRow r) => new()
    {
        Id           = Guid.Parse(r.Id),
        UserId       = r.UserId,
        AssetId      = Guid.Parse(r.AssetId),
        ChapterIndex = r.ChapterIndex,
        CfiPosition  = r.CfiPosition,
        Label        = r.Label,
        CreatedAt    = DateTime.Parse(r.CreatedAt),
    };
}
