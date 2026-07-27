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

    [Fact]
    public async Task LoadAsync_UsesOriginalDatesAcrossMediaTypes()
    {
        var cases = new[]
        {
            new ProjectionDateCase(
                "The Hobbit",
                "Books",
                "1937",
                new Dictionary<string, string>
                {
                    ["date"] = "1937-09-21",
                    ["year"] = "2012",
                }),
            new ProjectionDateCase(
                "The Hobbit",
                "Audiobooks",
                "1937",
                new Dictionary<string, string>
                {
                    ["release_year"] = "2000",
                    ["year"] = "2007",
                }),
            new ProjectionDateCase(
                "Interstellar",
                "Movies",
                "2014",
                new Dictionary<string, string>
                {
                    ["year"] = "2014",
                    ["release_year"] = "2025",
                }),
            new ProjectionDateCase(
                "Akira",
                "Comics",
                "1984",
                new Dictionary<string, string>
                {
                    ["year"] = "1984",
                    ["release_year"] = "1988",
                }),
            new ProjectionDateCase(
                "Hunky Dory",
                "Music",
                "1971",
                new Dictionary<string, string>
                {
                    ["original_release_year"] = "1971",
                    ["year"] = "2019",
                }),
        };

        using (var conn = _db.CreateConnection())
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO persons (id, name, created_at)
                VALUES (@personId, 'J. R. R. Tolkien', CURRENT_TIMESTAMP);
                """,
                new { personId = Guid.NewGuid() });

            foreach (var item in cases)
            {
                var workId = Guid.NewGuid();
                var editionId = Guid.NewGuid();
                var assetId = Guid.NewGuid();
                await conn.ExecuteAsync(
                    """
                    INSERT INTO works (id, media_type, work_kind, curator_state)
                    VALUES (@workId, @mediaType, 'standalone', 'accepted');
                    INSERT INTO editions (id, work_id)
                    VALUES (@editionId, @workId);
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
                        @filePath,
                        CURRENT_TIMESTAMP);
                    INSERT INTO canonical_values (entity_id, key, value, last_scored_at)
                    VALUES (@assetId, 'title', @title, CURRENT_TIMESTAMP);
                    """,
                    new
                    {
                        workId,
                        editionId,
                        assetId,
                        mediaType = item.MediaType,
                        contentHash = Guid.NewGuid().ToString("N"),
                        filePath = $"C:/library/{item.Title}.media",
                        title = item.Title,
                    });

                if (item.Title == "The Hobbit"
                    && item.MediaType is "Books" or "Audiobooks")
                {
                    await conn.ExecuteAsync(
                        """
                        INSERT INTO canonical_value_arrays (entity_id, key, ordinal, value)
                        VALUES (@assetId, 'author', 0, 'J. R. R. Tolkien');
                        """,
                        new { assetId });
                }

                foreach (var (key, value) in item.Dates)
                {
                    await conn.ExecuteAsync(
                        """
                        INSERT INTO canonical_values (entity_id, key, value, last_scored_at)
                        VALUES (@assetId, @key, @value, CURRENT_TIMESTAMP);
                        """,
                        new { assetId, key, value });
                }
            }
        }

        var rows = await new DisplayWorkProjectionReader(_db).LoadAsync(CancellationToken.None);

        Assert.Equal(cases.Length, rows.Count);
        foreach (var expected in cases)
        {
            var row = Assert.Single(
                rows,
                value => value.Title == expected.Title && value.MediaType == expected.MediaType);
            Assert.Equal(expected.ExpectedYear, row.Year);
        }
    }

    [Fact]
    public async Task LoadAsync_MusicUsesTrackWorkYearBeforeLegacyAlbumReissueYear()
    {
        var albumWorkId = Guid.NewGuid();
        var trackWorkId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        using (var conn = _db.CreateConnection())
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO works (id, media_type, work_kind, curator_state)
                VALUES (@albumWorkId, 'Music', 'parent', 'accepted');
                INSERT INTO works (id, parent_work_id, media_type, work_kind, curator_state)
                VALUES (@trackWorkId, @albumWorkId, 'Music', 'child', 'accepted');
                INSERT INTO editions (id, work_id)
                VALUES (@editionId, @trackWorkId);
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
                    'C:/library/music/original-track.flac',
                    CURRENT_TIMESTAMP);
                INSERT INTO canonical_values (entity_id, key, value, last_scored_at)
                VALUES (@albumWorkId, 'title', 'A Night at the Opera', CURRENT_TIMESTAMP);
                INSERT INTO canonical_values (entity_id, key, value, last_scored_at)
                VALUES (@albumWorkId, 'year', '2002', CURRENT_TIMESTAMP);
                INSERT INTO canonical_values (entity_id, key, value, last_scored_at)
                VALUES (@assetId, 'title', 'Love of My Life', CURRENT_TIMESTAMP);
                INSERT INTO canonical_values (entity_id, key, value, last_scored_at)
                VALUES (@assetId, 'year', '1975', CURRENT_TIMESTAMP);
                """,
                new
                {
                    albumWorkId,
                    trackWorkId,
                    editionId,
                    assetId,
                    contentHash = Guid.NewGuid().ToString("N"),
                });
        }

        var row = Assert.Single(await new DisplayWorkProjectionReader(_db).LoadAsync(CancellationToken.None));

        Assert.Equal("Love of My Life", row.Title);
        Assert.Equal("1975", row.Year);
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }

    private sealed record ProjectionDateCase(
        string Title,
        string MediaType,
        string ExpectedYear,
        IReadOnlyDictionary<string, string> Dates);
}
