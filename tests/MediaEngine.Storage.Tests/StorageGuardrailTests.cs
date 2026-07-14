using System.Text.RegularExpressions;

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

    [Fact]
    public void DatabaseCode_DoesNotStringifyOrTextParseInternalGuidParameters()
    {
        var repoRoot = FindRepositoryRoot();
        var sourceRoots = new[]
        {
            Path.Combine(repoRoot, "src", "MediaEngine.Storage"),
            Path.Combine(repoRoot, "src", "MediaEngine.Api"),
        };
        var internalIdName = "(?:entity|work|asset|edition|collection|parent|root|show|batch|profile|person|operation|event|job|segment|placement)Id";
        Regex[] forbiddenPatterns =
        [
            new($@"new\s*\{{[^\r\n}}]*\b{internalIdName}\s*=\s*[^\r\n,}}]*\.ToString\(", RegexOptions.IgnoreCase),
            new($@"AddWithValue\([^\r\n]*\b{internalIdName}\b[^\r\n]*\.ToString\(", RegexOptions.IgnoreCase),
            new($@"\.Value\s*=\s*\b{internalIdName}\b\.ToString\(", RegexOptions.IgnoreCase),
            new(@"Guid\.(?:Parse|TryParse)\([^\r\n]*GetString\(", RegexOptions.IgnoreCase),
        ];

        var offenders = sourceRoots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(
                Path.Combine("Services", "Display", "DisplayComposerService.cs"),
                StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => forbiddenPatterns
                .SelectMany(pattern => pattern.Matches(File.ReadAllText(path)))
                .Select(match => $"{Path.GetRelativePath(repoRoot, path)}: {match.Value}"))
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
