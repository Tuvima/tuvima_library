using System.Text.RegularExpressions;

namespace MediaEngine.Providers.Tests;

/// <summary>
/// Guards against reintroducing a private copy of comparable-text normalization.
/// <see cref="MediaEngine.Providers.Services.RetailTextSimilarity.NormalizeComparableText"/>
/// is THE canonical implementation shared by Stage 1 retail matching and Stage 2
/// Wikidata bridging (see <c>RetailMatchScoringService</c>, <c>RetailMatchWorker</c>,
/// <c>WikidataBridgeWorker</c>, and <c>SearchService</c>). Before this guardrail, four
/// separate copies existed — one of which (<c>WikidataBridgeWorker</c>) silently
/// diverged from the others (no diacritic stripping, no ampersand mapping), so the
/// same title could compare equal in Stage 1 and unequal in Stage 2. This test fails
/// the build if any file under <c>src/MediaEngine.Providers</c> other than
/// <c>RetailTextSimilarity.cs</c> declares a method named <c>NormalizeComparableText</c>.
/// </summary>
public sealed class PrivateNormalizationGuardrailTests
{
    private static readonly Regex MethodDeclarationRegex =
        new(@"\bstring\s+NormalizeComparableText\s*\(", RegexOptions.Compiled);

    // No allowlist entries expected — RetailTextSimilarity.cs is the sole canonical owner.
    private static readonly string[] AllowedFiles =
    [
        "src/MediaEngine.Providers/Services/RetailTextSimilarity.cs",
    ];

    [Fact]
    public void OnlyRetailTextSimilarity_DeclaresNormalizeComparableText()
    {
        var repoRoot = FindRepoRoot();
        var providersDir = Path.Combine(repoRoot, "src", "MediaEngine.Providers");

        var offenders = Directory.EnumerateFiles(providersDir, "*.cs", SearchOption.AllDirectories)
            .Where(IsActiveSourcePath)
            .Where(path => !AllowedFiles.Contains(ToRelativePath(repoRoot, path), StringComparer.OrdinalIgnoreCase))
            .Where(path => MethodDeclarationRegex.IsMatch(File.ReadAllText(path)))
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

    private static bool IsActiveSourcePath(string path)
    {
        var segments = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        return !segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }
}
