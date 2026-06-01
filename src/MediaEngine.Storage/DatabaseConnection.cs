using MediaEngine.Storage.Contracts;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage;

/// <summary>
/// Facade for the SQLite database lifecycle.
/// Owns the shared startup connection and delegates focused lifecycle work to
/// connection, schema, current-startup-task, integrity, and maintenance collaborators.
/// </summary>
public sealed class DatabaseConnection : IDatabaseConnection
{
    private readonly string _databasePath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SchemaInitializer _schemaInitializer;
    private readonly SchemaMigrator _schemaMigrator;
    private readonly DatabaseIntegrityChecker _integrityChecker;
    private readonly SqliteMaintenanceService _maintenanceService;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private SqliteConnection? _connection;

    /// <param name="databasePath">
    /// Absolute or relative path to the <c>.db</c> file.
    /// Typically sourced from <c>config/core.json</c>.
    /// </param>
    public DatabaseConnection(string databasePath)
        : this(
            databasePath,
            new SqliteConnectionFactory(databasePath),
            new SchemaInitializer(),
            new SchemaMigrator(),
            new DatabaseIntegrityChecker(),
            new SqliteMaintenanceService())
    {
    }

    internal DatabaseConnection(
        string databasePath,
        SqliteConnectionFactory connectionFactory,
        SchemaInitializer schemaInitializer,
        SchemaMigrator schemaMigrator,
        DatabaseIntegrityChecker integrityChecker,
        SqliteMaintenanceService maintenanceService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DapperConfiguration.Configure();
        _databasePath = databasePath;
        _connectionFactory = connectionFactory;
        _schemaInitializer = schemaInitializer;
        _schemaMigrator = schemaMigrator;
        _integrityChecker = integrityChecker;
        _maintenanceService = maintenanceService;
    }

    /// <inheritdoc/>
    public SqliteConnection Open()
    {
        if (_connection is not null)
        {
            return _connection;
        }

        _connection = _connectionFactory.OpenSharedConnection();
        return _connection;
    }

    /// <inheritdoc/>
    public SqliteConnection CreateConnection()
        => _connectionFactory.CreateOperationConnection();

    /// <inheritdoc/>
    public void InitializeSchema()
    {
        StorageEpochGuard.EnsureCurrentOrReset(_databasePath);
        var conn = Open();
        _schemaInitializer.Initialize(conn);
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>PRAGMA integrity_check</c> returns anything other than "ok".
    /// </exception>
    public void RunStartupChecks()
    {
        var conn = Open();
        _integrityChecker.RunStartupChecks(conn, _databasePath);
        _schemaMigrator.RunStartupTasks(conn);
    }

    /// <summary>
    /// Executes a VACUUM to reclaim unused pages.
    /// Spec: "SHOULD perform a VACUUM during low-activity maintenance windows."
    /// Call when <c>MaintenanceSettings.VacuumOnStartup</c> is <c>true</c>.
    /// </summary>
    public void Vacuum()
        => _maintenanceService.Vacuum(Open());

    /// <inheritdoc/>
    public Task AcquireWriteLockAsync(CancellationToken ct = default)
        => _writeLock.WaitAsync(ct);

    /// <inheritdoc/>
    public void ReleaseWriteLock()
        => _writeLock.Release();

    /// <inheritdoc/>
    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
        _writeLock.Dispose();
    }
}
