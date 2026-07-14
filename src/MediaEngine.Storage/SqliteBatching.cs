namespace MediaEngine.Storage;

/// <summary>
/// Shared bounds for dynamically parameterized SQLite queries. Keeping well
/// below SQLite's build-dependent variable ceiling makes batch reads portable
/// across the desktop, service, and test runtimes.
/// </summary>
internal static class SqliteBatching
{
    public const int MaxParametersPerQuery = 500;
}
