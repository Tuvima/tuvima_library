namespace MediaEngine.Api.Tests;

public sealed class LegacyIngestionFallbackGuardrailTests
{
    [Fact]
    public void ProductionSource_DoesNotReintroduceLegacyImageStorageFallbacks()
    {
        var repoRoot = FindRepoRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => IsActiveSourcePath(repoRoot, path))
            .Select(path => new
            {
                Path = ToRelativePath(repoRoot, path),
                Text = File.ReadAllText(path),
            })
            .Where(file => ForbiddenTokens.Any(token => file.Text.Contains(token, StringComparison.Ordinal)))
            .Select(file => file.Path)
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

    private static readonly string[] ForbiddenTokens =
    [
        "ImagePathService",
        ".data/images",
        "sweep-orphan-images",
        "_provisional",
        "PromoteToQid",
        "SweepPendingToQid",
        "GetWorkImageDir",
        "GetPersonImageDir",
        "\".people\"",
        ".people/",
        "person.xml",
        "TUVIMA_WATCH_FOLDER",
    ];
}
