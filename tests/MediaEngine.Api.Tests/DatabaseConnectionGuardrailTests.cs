namespace MediaEngine.Api.Tests;

public sealed class DatabaseConnectionGuardrailTests
{
    [Fact]
    public void EndpointFiles_DoNotUseSharedDatabaseOpen()
    {
        var repoRoot = FindRepoRoot();
        var endpointDir = Path.Combine(repoRoot, "src", "MediaEngine.Api", "Endpoints");
        var offenders = Directory.EnumerateFiles(endpointDir, "*.cs", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = path,
                Text = File.ReadAllText(path),
            })
            .Where(file => file.Text.Contains("db.Open()", StringComparison.Ordinal)
                           || file.Text.Contains(".Open();", StringComparison.Ordinal)
                              && file.Text.Contains("IDatabaseConnection", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(repoRoot, file.Path))
            .ToList();

        Assert.Empty(offenders);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MediaEngine.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
