using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage.Contracts;

/// <summary>
/// Manages the lifecycle of the SQLite connection and WAL-mode settings.
/// Spec: Phase 4 – Interfaces § IDatabaseConnection
/// </summary>
public interface IDatabaseConnection : IDisposable
{
    /// <summary>
    /// Opens (or returns the already-open) connection.
    /// Sets PRAGMA journal_mode=WAL, foreign_keys=ON, and temp_store=MEMORY.
    /// </summary>
    SqliteConnection Open();

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
}
