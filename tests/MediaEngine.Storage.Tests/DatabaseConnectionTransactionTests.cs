using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage.Tests;

/// <summary>
/// Covers <see cref="DatabaseConnection.ExecuteInTransactionAsync{T}"/> and its
/// non-generic overload: commit correctness, rollback-on-throw, write-lock
/// release after a failed body, write serialization under concurrent callers,
/// and cancellation before the transaction body ever runs.
/// </summary>
public sealed class DatabaseConnectionTransactionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public DatabaseConnectionTransactionTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_tx_{Guid.NewGuid():N}.db");
        _db = new DatabaseConnection(_dbPath);
        _db.InitializeSchema();
        _db.RunStartupChecks();
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { }
        TryDelete(_dbPath);
        TryDelete($"{_dbPath}-wal");
        TryDelete($"{_dbPath}-shm");
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_CommitsWrittenRowOnSuccess()
    {
        const string path = "C:/library/commit-test.mkv";

        await _db.ExecuteInTransactionAsync(async (conn, tx, _) =>
            await InsertHashRowAsync(conn, tx, path, "hash-commit"));

        Assert.Equal("hash-commit", await ReadHashAsync(path));
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_RollsBackWhenBodyThrows()
    {
        const string path = "C:/library/rollback-test.mkv";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _db.ExecuteInTransactionAsync(async (conn, tx, _) =>
            {
                await InsertHashRowAsync(conn, tx, path, "hash-rollback");
                throw new InvalidOperationException("Simulated failure after write.");
            }));

        Assert.Null(await ReadHashAsync(path));
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_ReleasesWriteLockAfterFailureSoNextCallSucceeds()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _db.ExecuteInTransactionAsync(async (conn, tx, _) =>
            {
                await InsertHashRowAsync(conn, tx, "C:/library/lock-release.mkv", "will-not-persist");
                throw new InvalidOperationException("Simulated failure after write.");
            }));

        // If the semaphore had leaked when the body above threw, this call would
        // block until the timeout token fires and surface OperationCanceledException
        // instead of completing — proving the lock was actually released in `finally`.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _db.ExecuteInTransactionAsync(
            async (conn, tx, _) => await InsertHashRowAsync(conn, tx, "C:/library/after-failure.mkv", "hash-after"),
            timeout.Token);

        Assert.Equal("hash-after", await ReadHashAsync("C:/library/after-failure.mkv"));
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_SerializesConcurrentWritersWithoutDatabaseLockedErrors()
    {
        const int writerCount = 8;

        var writers = Enumerable.Range(0, writerCount)
            .Select(i => _db.ExecuteInTransactionAsync(async (conn, tx, _) =>
                await InsertHashRowAsync(conn, tx, $"C:/library/concurrent-{i}.mkv", $"hash-{i}")))
            .ToArray();

        // Task.WhenAll surfaces the first failure; a "database is locked" SqliteException
        // here would mean write serialization was not actually enforced.
        await Task.WhenAll(writers);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM file_hash_cache WHERE absolute_path LIKE 'C:/library/concurrent-%';";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        Assert.Equal(writerCount, count);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_ThrowsBeforeInvokingBodyWhenTokenAlreadyCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var bodyInvoked = false;

        // ThrowsAnyAsync: SemaphoreSlim.WaitAsync surfaces a pre-cancelled token
        // as TaskCanceledException, a subclass of OperationCanceledException.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _db.ExecuteInTransactionAsync<int>(
                (_, _, _) =>
                {
                    bodyInvoked = true;
                    return Task.FromResult(0);
                },
                cts.Token));

        Assert.False(bodyInvoked);
    }

    private static async Task InsertHashRowAsync(SqliteConnection conn, SqliteTransaction tx, string path, string hash)
    {
        var now = DateTimeOffset.UtcNow.ToString("o");
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO file_hash_cache (absolute_path, size_bytes, mtime_utc, sha256, cached_at)
            VALUES ($path, $size, $mtime, $hash, $cachedAt);
            """;
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$size", 1024L);
        cmd.Parameters.AddWithValue("$mtime", now);
        cmd.Parameters.AddWithValue("$hash", hash);
        cmd.Parameters.AddWithValue("$cachedAt", now);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<string?> ReadHashAsync(string path)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sha256 FROM file_hash_cache WHERE absolute_path = $path;";
        cmd.Parameters.AddWithValue("$path", path);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Test cleanup is best effort; failure should not hide the test assertion result.
        }
    }
}
