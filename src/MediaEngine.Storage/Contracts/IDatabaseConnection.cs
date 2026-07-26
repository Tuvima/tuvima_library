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
    /// This exists primarily for <see cref="ExecuteInTransactionAsync{T}"/> to use
    /// internally, plus rare advanced scenarios that cannot express their work as a
    /// single <c>body</c> callback. Calling this directly alongside a raw
    /// <c>BeginTransaction()</c> is deprecated — use
    /// <see cref="ExecuteInTransactionAsync{T}"/> or
    /// <see cref="ExecuteInTransactionAsync(Func{SqliteConnection, SqliteTransaction, CancellationToken, Task}, CancellationToken)"/>
    /// instead so the write-lock contract is enforced structurally.
    /// </summary>
    Task AcquireWriteLockAsync(CancellationToken ct = default);

    /// <summary>
    /// Releases the global write-serialization lock.
    /// This exists primarily for <see cref="ExecuteInTransactionAsync{T}"/> to use
    /// internally, plus rare advanced scenarios that cannot express their work as a
    /// single <c>body</c> callback. Must be called in a <c>finally</c> block after
    /// the transaction completes. Calling this directly alongside a raw
    /// <c>BeginTransaction()</c> is deprecated — use
    /// <see cref="ExecuteInTransactionAsync{T}"/> or
    /// <see cref="ExecuteInTransactionAsync(Func{SqliteConnection, SqliteTransaction, CancellationToken, Task}, CancellationToken)"/>
    /// instead so the write-lock contract is enforced structurally.
    /// </summary>
    void ReleaseWriteLock();

    /// <summary>
    /// Runs <paramref name="body"/> inside a dedicated transaction, enforcing the
    /// write-lock contract structurally: acquires the global write-serialization
    /// lock, opens a fresh pooled connection via <see cref="CreateConnection"/>,
    /// begins a transaction, invokes <paramref name="body"/>, commits on success,
    /// rolls back on any exception (the exception is rethrown to the caller), and
    /// always releases the write lock in a <c>finally</c> block.
    /// New code MUST use this instead of calling <c>BeginTransaction()</c> directly;
    /// the write-lock contract is enforced structurally here.
    /// </summary>
    /// <typeparam name="T">The type returned by <paramref name="body"/>.</typeparam>
    /// <param name="body">
    /// The transactional work to run. Receives the open connection, the active
    /// transaction, and the cancellation token.
    /// </param>
    /// <param name="ct">Token observed before the lock is acquired and passed through to <paramref name="body"/>.</param>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task<T>> body,
        CancellationToken ct = default);

    /// <summary>
    /// Non-generic overload of <see cref="ExecuteInTransactionAsync{T}"/> for
    /// transactional work with no return value. Same guarantees apply: the
    /// write-lock contract is enforced structurally, the transaction commits on
    /// success and rolls back (with the exception rethrown) on failure, and the
    /// write lock is always released in a <c>finally</c> block.
    /// New code MUST use this instead of calling <c>BeginTransaction()</c> directly;
    /// the write-lock contract is enforced structurally here.
    /// </summary>
    /// <param name="body">
    /// The transactional work to run. Receives the open connection, the active
    /// transaction, and the cancellation token.
    /// </param>
    /// <param name="ct">Token observed before the lock is acquired and passed through to <paramref name="body"/>.</param>
    Task ExecuteInTransactionAsync(
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task> body,
        CancellationToken ct = default);
}
