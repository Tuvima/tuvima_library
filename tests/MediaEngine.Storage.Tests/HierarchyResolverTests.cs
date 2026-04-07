using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Services;

namespace MediaEngine.Storage.Tests;

/// <summary>
/// Tests for <see cref="HierarchyResolver"/> — the Phase 3 (M-082) service
/// that decides whether a newly-ingested file slots under an existing
/// parent Work or stands alone.
///
/// Each test uses a real SQLite database (no mocks) so the resolver,
/// repository, schema, and migrations are exercised end-to-end.
/// </summary>
public sealed class HierarchyResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;
    private readonly WorkRepository _works;
    private readonly HierarchyResolver _resolver;

    public HierarchyResolverTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_hier_test_{Guid.NewGuid():N}.db");
        _db = new DatabaseConnection(_dbPath);
        _db.InitializeSchema();
        _db.RunStartupChecks();

        _works    = new WorkRepository(_db);
        _resolver = new HierarchyResolver(_works);
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }

    // ── Music ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Music_ThreeTracksOfSameAlbum_ShareOneParent()
    {
        var t1 = await _resolver.ResolveAsync(MediaType.Music, Track("Pink Floyd", "Dark Side of the Moon", "Money", 6));
        var t2 = await _resolver.ResolveAsync(MediaType.Music, Track("Pink Floyd", "Dark Side of the Moon", "Time", 4));
        var t3 = await _resolver.ResolveAsync(MediaType.Music, Track("Pink Floyd", "Dark Side of the Moon", "Brain Damage", 9));

        Assert.NotNull(t1.ParentWorkId);
        Assert.Equal(t1.ParentWorkId, t2.ParentWorkId);
        Assert.Equal(t1.ParentWorkId, t3.ParentWorkId);

        Assert.Equal(WorkKind.Child, t1.WorkKind);
        Assert.Equal(6, t1.Ordinal);
        Assert.Equal(4, t2.Ordinal);
        Assert.Equal(9, t3.Ordinal);

        // All three child Work ids are distinct.
        Assert.NotEqual(t1.WorkId, t2.WorkId);
        Assert.NotEqual(t2.WorkId, t3.WorkId);
    }

    [Fact]
    public async Task Music_DifferentAlbums_GetDifferentParents()
    {
        var a = await _resolver.ResolveAsync(MediaType.Music, Track("Radiohead", "OK Computer",   "Karma Police", 6));
        var b = await _resolver.ResolveAsync(MediaType.Music, Track("Radiohead", "Kid A",         "Idioteque",    8));

        Assert.NotEqual(a.ParentWorkId, b.ParentWorkId);
    }

    [Fact]
    public async Task Music_SameTrackTwice_IsIdempotent()
    {
        var first  = await _resolver.ResolveAsync(MediaType.Music, Track("Radiohead", "OK Computer", "Karma Police", 6));
        var second = await _resolver.ResolveAsync(MediaType.Music, Track("Radiohead", "OK Computer", "Karma Police", 6));

        Assert.Equal(first.WorkId, second.WorkId);
        Assert.Equal(first.ParentWorkId, second.ParentWorkId);
        Assert.True(first.NewlyCreated);
        Assert.False(second.NewlyCreated);
    }

    [Fact]
    public async Task Music_DiacriticDifferences_NormalizeToSameParent()
    {
        // Note: only true diacritics (combining marks) are normalised away.
        // Ligatures like æ are distinct letters and remain as-is.
        var a = await _resolver.ResolveAsync(MediaType.Music, Track("Beyoncé", "Renaissance", "Cuff It",    3));
        var b = await _resolver.ResolveAsync(MediaType.Music, Track("Beyonce", "Renaissance", "Break My Soul", 6));

        Assert.Equal(a.ParentWorkId, b.ParentWorkId);
    }

    [Fact]
    public async Task Music_NoAlbum_FallsBackToStandalone()
    {
        var loose = await _resolver.ResolveAsync(MediaType.Music, new Dictionary<string, string>
        {
            ["title"]  = "Untitled Demo",
            ["artist"] = "Some Artist",
        });

        Assert.Equal(WorkKind.Standalone, loose.WorkKind);
        Assert.Null(loose.ParentWorkId);
    }

    // ── TV ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TV_ThreeEpisodes_BuildsShowSeasonEpisodeHierarchy()
    {
        var e1 = await _resolver.ResolveAsync(MediaType.TV, Episode("Breaking Bad", 1, 1, "Pilot"));
        var e2 = await _resolver.ResolveAsync(MediaType.TV, Episode("Breaking Bad", 1, 2, "Cat's in the Bag..."));
        var e3 = await _resolver.ResolveAsync(MediaType.TV, Episode("Breaking Bad", 2, 1, "Seven Thirty-Seven"));

        // e1 and e2 share Season 1 parent.
        Assert.NotNull(e1.ParentWorkId);
        Assert.Equal(e1.ParentWorkId, e2.ParentWorkId);

        // Season 2 is a different parent.
        Assert.NotEqual(e1.ParentWorkId, e3.ParentWorkId);

        Assert.Equal(WorkKind.Child, e1.WorkKind);
        Assert.Equal(1, e1.Ordinal);
        Assert.Equal(2, e2.Ordinal);
        Assert.Equal(1, e3.Ordinal);
    }

    [Fact]
    public async Task TV_TwoShowsWithSeasonOne_StayDistinct()
    {
        var bb = await _resolver.ResolveAsync(MediaType.TV, Episode("Breaking Bad",       1, 1, "Pilot"));
        var bc = await _resolver.ResolveAsync(MediaType.TV, Episode("Better Call Saul",   1, 1, "Uno"));

        Assert.NotEqual(bb.ParentWorkId, bc.ParentWorkId);
    }

    [Fact]
    public async Task TV_NoShowName_FallsBackToStandalone()
    {
        var orphan = await _resolver.ResolveAsync(MediaType.TV, new Dictionary<string, string>
        {
            ["title"] = "Some Episode",
        });

        Assert.Equal(WorkKind.Standalone, orphan.WorkKind);
    }

    // ── Comics ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Comics_TwoIssues_ShareSeriesParent()
    {
        var i1 = await _resolver.ResolveAsync(MediaType.Comics, new Dictionary<string, string>
        {
            ["series"]       = "Saga",
            ["issue_number"] = "1",
            ["title"]        = "Chapter One",
        });
        var i2 = await _resolver.ResolveAsync(MediaType.Comics, new Dictionary<string, string>
        {
            ["series"]       = "Saga",
            ["issue_number"] = "2",
            ["title"]        = "Chapter Two",
        });

        Assert.NotNull(i1.ParentWorkId);
        Assert.Equal(i1.ParentWorkId, i2.ParentWorkId);
        Assert.Equal(1, i1.Ordinal);
        Assert.Equal(2, i2.Ordinal);
    }

    // ── Standalone ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Movies_AlwaysStandalone_NoDedup()
    {
        var a = await _resolver.ResolveAsync(MediaType.Movies, new Dictionary<string, string>
        {
            ["title"]    = "Inception",
            ["director"] = "Christopher Nolan",
            ["year"]     = "2010",
        });
        var b = await _resolver.ResolveAsync(MediaType.Movies, new Dictionary<string, string>
        {
            ["title"]    = "Inception",
            ["director"] = "Christopher Nolan",
            ["year"]     = "2010",
        });

        // Two different files of the same movie create two different
        // standalone Works. The "is this the same file" check happens
        // upstream in the ingestion engine via content hash; the chain
        // factory is only invoked when a NEW file needs a Work.
        Assert.Equal(WorkKind.Standalone, a.WorkKind);
        Assert.Equal(WorkKind.Standalone, b.WorkKind);
        Assert.NotEqual(a.WorkId, b.WorkId);
        Assert.Null(a.ParentWorkId);
    }

    // ── Catalog promotion ─────────────────────────────────────────────────────

    [Fact]
    public async Task Music_CatalogRow_GetsPromoted_WhenFileArrives()
    {
        // Set up: ingest one track of an album so the parent exists.
        var first = await _resolver.ResolveAsync(MediaType.Music,
            Track("Daft Punk", "Discovery", "One More Time", 1));
        var parentId = first.ParentWorkId!.Value;

        // Simulate Wikidata adding a catalog row for track #2 (we don't own it yet).
        var catalogId = await _works.InsertCatalogChildAsync(
            MediaType.Music, parentId, ordinal: 2, externalIdentifiers: null, default);

        // Now a file for track #2 arrives.
        var arrived = await _resolver.ResolveAsync(MediaType.Music,
            Track("Daft Punk", "Discovery", "Aerodynamic", 2));

        // The resolver should have found the catalog row and promoted it.
        Assert.Equal(catalogId, arrived.WorkId);
        Assert.False(arrived.NewlyCreated);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Dictionary<string, string> Track(string artist, string album, string title, int trackNum) => new()
    {
        ["artist"]       = artist,
        ["album"]        = album,
        ["title"]        = title,
        ["track_number"] = trackNum.ToString(),
    };

    private static Dictionary<string, string> Episode(string show, int season, int episode, string title) => new()
    {
        ["show_name"]      = show,
        ["season_number"]  = season.ToString(),
        ["episode_number"] = episode.ToString(),
        ["episode_title"]  = title,
    };
}
