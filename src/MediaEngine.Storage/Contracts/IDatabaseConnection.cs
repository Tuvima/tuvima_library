using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage.Contracts;

/// <summary>
/// Manages the lifecycle of the SQLite connection and WAL-mode settings.
/// Spec: Phase 4 – Interfaces § IDatabaseConnection
/// </summary>
public interface IDatabaseConnection : IDisposable
{
    /// <summary>
    /// Opens (or returns the already-open) shared connection used only for
    /// schema initialization and startup checks.  Do NOT use for normal
    /// repository operations — use <see cref="CreateConnection"/> instead.
    /// </summary>
    SqliteConnection Open();

    /// <summary>
    /// Returns a new pooled connection configured with WAL mode, foreign keys,
    /// and a busy timeout.  Callers MUST dispose the returned connection
    /// (use <c>using var conn = _db.CreateConnection();</c>).
    /// Each thread gets its own connection, eliminating internal command-list
    /// corruption when multiple threads operate concurrently.
    /// </summary>
    SqliteConnection CreateConnection();

    /// <summary>
    /// Applies the embedded schema DDL idempotently.
    /// Safe to call on every startup; all statements use CREATE … IF NOT EXISTS.
    /// </summary>
    void InitializeSchema();

    /// <summary>
    /// Runs PRAGMA integrity_check and PRAGMA optimize.
    /// Spec: "SHOULD execute on application startup."
    /// Throws <see cref="InvalidOperationException"/> if integrity_check does not return "ok".
    /// </summary>
    void RunStartupChecks();

    /// <summary>
    /// Acquires the global write-serialization lock.
    /// All code that calls <c>BeginTransaction()</c> MUST acquire this first
    /// to prevent "cannot start a transaction within a transaction" errors
    /// when multiple threads share the singleton connection.
    /// </summary>
    Task AcquireWriteLockAsync(CancellationToken ct = default);

    /// <summary>
    /// Releases the global write-serialization lock.
    /// Must be called in a <c>finally</c> block after the transaction completes.
    /// </summary>
    void ReleaseWriteLock();
}
