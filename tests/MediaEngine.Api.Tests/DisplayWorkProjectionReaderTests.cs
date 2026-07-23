using Dapper;
using MediaEngine.Api.Services.Display;
using MediaEngine.Storage;

namespace MediaEngine.Api.Tests;

public sealed class DisplayWorkProjectionReaderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public DisplayWorkProjectionReaderTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_display_work_{Guid.NewGuid():N}.db");
        _db = new DatabaseConnection(_dbPath);
        _db.InitializeSchema();
        _db.RunStartupChecks();
    }

    [Fact]
    public async Task LoadAsync_PrefersTheEnrichedPersonWhenDuplicateArtistNamesExist()
    {
        var workId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var localStubId = Guid.NewGuid();
        var enrichedPersonId = Guid.NewGuid();

        using (var conn = _db.CreateConnection())
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO works (id, media_type, curator_state)
                VALUES (@workId, 'Music', 'accepted');

                INSERT INTO editions (id, work_id, format_label)
                VALUES (@editionId, @workId, 'Audio');

                INSERT INTO media_assets (
                    id,
                    edition_id,
                    content_hash,
                    file_path_root,
                    presented_at)
                VALUES (
                    @assetId,
                    @editionId,
                    @contentHash,
                    'C:\library\music\track.flac',
                    CURRENT_TIMESTAMP);

                INSERT INTO canonical_value_arrays (
                    entity_id,
                    key,
                    ordinal,
                    value,
                    value_qid)
                VALUES (
                    @workId,
                    'artist',
                    0,
                    'Hans Zimmer',
                    'Q-WRONG');

                INSERT INTO canonical_values (
                    entity_id,
                    key,
                    value,
                    last_scored_at)
                VALUES (
                    @workId,
                    'title',
                    'Test Track',
                    CURRENT_TIMESTAMP);

                INSERT INTO persons (
                    id,
                    name,
                    wikidata_qid,
                    occupation,
                    created_at,
                    enriched_at)
                VALUES (
                    @localStubId,
                    'Hans Zimmer',
                    'Q-WRONG',
                    'writer',
                    '2026-01-01T00:00:00Z',
                    NULL);

                INSERT INTO persons (
                    id,
                    name,
                    wikidata_qid,
                    biography,
                    occupation,
                    local_headshot_path,
                    created_at,
                    enriched_at)
                VALUES (
                    @enrichedPersonId,
                    'Hans Zimmer',
                    'Q-CANONICAL',
                    'Canonical composer biography',
                    'composer',
                    'C:\assets\people\hans-zimmer.jpg',
                    '2026-02-01T00:00:00Z',
                    '2026-02-02T00:00:00Z');
                """,
                new
                {
                    workId,
                    editionId,
                    assetId,
                    contentHash = Guid.NewGuid().ToString("N"),
                    localStubId,
                    enrichedPersonId,
                });
        }

        var row = Assert.Single(await new DisplayWorkProjectionReader(_db).LoadAsync(CancellationToken.None));

        Assert.Equal(enrichedPersonId, row.ArtistPersonId);
        Assert.Equal("Hans Zimmer", row.ArtistPersonName);
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
