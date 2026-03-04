using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// Appends rows to <c>transaction_log</c> and prunes old entries.
/// ORM-less: uses raw <see cref="Microsoft.Data.Sqlite.SqliteCommand"/>.
/// Spec: Phase 4 – ITransactionJournal interface.
/// </summary>
public sealed class TransactionJournal : ITransactionJournal
{
    private readonly IDatabaseConnection _db;

    public TransactionJournal(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    // -------------------------------------------------------------------------
    // ITransactionJournal
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public void Log(string eventType, string entityType, string entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        var conn = _db.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO transaction_log (event_type, entity_type, entity_id)
            VALUES (@event_type, @entity_type, @entity_id);
            """;

        cmd.Parameters.AddWithValue("@event_type",  eventType);
        cmd.Parameters.AddWithValue("@entity_type", entityType);
        cmd.Parameters.AddWithValue("@entity_id",   entityId);

        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Deletes the <em>oldest</em> rows (smallest <c>id</c>) to bring the table
    /// back to <paramref name="maxEntries"/>.  Uses a subquery so the DELETE is
    /// compatible with SQLite's limited DELETE … LIMIT support (which requires
    /// the SQLITE_ENABLE_UPDATE_DELETE_LIMIT compile-time option that is absent
    /// on most distributions).
    /// Spec: "transaction_log SHOULD be archived or truncated after reaching
    ///        100,000 entries."
    /// </remarks>
    public void Prune(int maxEntries = 100_000)
    {
        if (maxEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntries), "Must be > 0.");

        var conn = _db.Open();

        // Read current row count first to avoid an unnecessary DELETE.
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM transaction_log;";
        var count = Convert.ToInt64(countCmd.ExecuteScalar()!);

        if (count <= maxEntries)
            return;

        var excess = count - maxEntries;

        // Delete the oldest [excess] rows identified by the lowest id values.
        using var deleteCmd = conn.CreateCommand();
        deleteCmd.CommandText = """
            DELETE FROM transaction_log
            WHERE id IN (
                SELECT id
                FROM   transaction_log
                ORDER  BY id ASC
                LIMIT  @excess
            );
            """;
        deleteCmd.Parameters.AddWithValue("@excess", excess);
        deleteCmd.ExecuteNonQuery();
    }
}
