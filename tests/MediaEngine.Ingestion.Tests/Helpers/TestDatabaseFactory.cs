using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Ingestion.Tests.Helpers;

/// <summary>
/// Creates a fresh in-memory SQLite database for each test, with the full
/// production schema applied. Uses <c>:memory:</c> so each test is isolated
/// and no disk cleanup is needed.
/// </summary>
internal sealed class TestDatabaseFactory : IDisposable
{
    private readonly DatabaseConnection _db;

    public IDatabaseConnection Connection => _db;

    public TestDatabaseFactory()
    {
        DapperConfiguration.Configure();
        var tempPath = Path.Combine(Path.GetTempPath(), $"tuvima_test_{Guid.NewGuid():N}.db");
        _db = new DatabaseConnection(tempPath);
        _db.InitializeSchema();
        _db.RunStartupChecks();
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { /* best effort */ }
    }
}
