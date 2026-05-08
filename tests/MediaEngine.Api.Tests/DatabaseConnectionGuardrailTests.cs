using System.Text.RegularExpressions;

namespace MediaEngine.Api.Tests;

public sealed class DatabaseConnectionGuardrailTests
{
    [Fact]
    public void EndpointFiles_DoNotUseSharedDatabaseOpen()
    {
        var repoRoot = FindRepoRoot();
        var endpointDir = Path.Combine(repoRoot, "src", "MediaEngine.Api", "Endpoints");
        var offenders = Directory.EnumerateFiles(endpointDir, "*.cs", SearchOption.AllDirectories)
            .Where(path => IsActiveSourcePath(repoRoot, path))
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

    [Fact]
    public void ActiveSourceFiles_DoNotUseSharedDatabaseOpenOutsideStartupSchemaOrFixtures()
    {
        var repoRoot = FindRepoRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => IsActiveSourcePath(repoRoot, path))
            .Where(path => !AllowedOpenUsageFiles.Contains(ToRelativePath(repoRoot, path), StringComparer.OrdinalIgnoreCase))
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("IDatabaseConnection", StringComparison.Ordinal)
                    && (text.Contains(".Open()", StringComparison.Ordinal)
                        || text.Contains(".Open();", StringComparison.Ordinal)
                        || text.Contains("db.Open(", StringComparison.Ordinal)
                        || text.Contains("_db.Open(", StringComparison.Ordinal));
            })
            .Select(path => ToRelativePath(repoRoot, path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SilentCatchBlocks_AreLimitedToDocumentedBestEffortLocations()
    {
        var repoRoot = FindRepoRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.razor", SearchOption.AllDirectories))
            .Where(path => IsActiveSourcePath(repoRoot, path))
            .Where(path =>
            {
                var relative = ToRelativePath(repoRoot, path);
                return !AllowedSilentCatchFiles.Contains(relative, StringComparer.OrdinalIgnoreCase)
                    && SilentCatchRegex.IsMatch(File.ReadAllText(path));
            })
            .Select(path => ToRelativePath(repoRoot, path))
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

    private static string ToRelativePath(string repoRoot, string path) =>
        Path.GetRelativePath(repoRoot, path).Replace('\\', '/');

    private static bool IsActiveSourcePath(string repoRoot, string path)
    {
        var segments = ToRelativePath(repoRoot, path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return !segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] AllowedOpenUsageFiles =
    [
        // Startup/schema lifecycle owns the shared connection.
        "src/MediaEngine.Api/Program.cs",
        "src/MediaEngine.Storage/DatabaseConnection.cs",
        // Dev harness reset is a non-production database maintenance path.
        "src/MediaEngine.Api/DevSupport/DevHarnessResetService.cs",
    ];

    private static readonly string[] AllowedSilentCatchFiles =
    [
    ];

    private static readonly Regex SilentCatchRegex =
        new(@"catch\s*\{\s*\}", RegexOptions.Compiled);
}
