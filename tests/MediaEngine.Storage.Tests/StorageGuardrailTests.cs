namespace MediaEngine.Storage.Tests;

public sealed class StorageGuardrailTests
{
    [Fact]
    public void SourceCode_DoesNotContainPackedMultiValueDelimiter()
    {
        var repoRoot = FindRepositoryRoot();
        var delimiter = new string('|', 3);
        var roots = new[] { Path.Combine(repoRoot, "src"), Path.Combine(repoRoot, "tests") };
        var offenders = roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => Path.GetExtension(path) is ".cs" or ".razor")
            .Where(path => File.ReadAllText(path).Contains(delimiter, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Empty(offenders);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MediaEngine.slnx")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
