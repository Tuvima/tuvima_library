namespace MediaEngine.Storage.Tests;

public sealed class StorageExecutionModelGuardrailTests
{
    [Fact]
    public void Storage_DoesNotUseMisleadingSqliteAsyncIoMethods()
    {
        var root = FindRepoRoot();
        var storageRoot = Path.Combine(root, "src", "MediaEngine.Storage");
        string[] forbidden =
        [
            ".QueryAsync",
            ".QueryFirstAsync",
            ".QueryFirstOrDefaultAsync",
            ".QuerySingleAsync",
            ".QuerySingleOrDefaultAsync",
            ".ExecuteAsync",
            ".ExecuteScalarAsync",
            ".ExecuteNonQueryAsync",
            ".ExecuteReaderAsync",
            ".ReadAsync",
        ];

        var offenders = Directory
            .EnumerateFiles(storageRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .SelectMany(file => forbidden
                .Where(token => file.Text.Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetRelativePath(root, file.Path)}: {token}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void StorageWrites_UseTheSynchronousCallbackTransactionTemplate()
    {
        var root = FindRepoRoot();
        var storageRoot = Path.Combine(root, "src", "MediaEngine.Storage");

        var offenders = Directory
            .EnumerateFiles(storageRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "ExecuteInTransactionAsync",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        Assert.Empty(offenders);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
