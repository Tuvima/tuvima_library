using System.Text.RegularExpressions;

namespace MediaEngine.Domain.Tests;

/// <summary>
/// Guards the shared primitives introduced under <c>src/MediaEngine.Domain/Services/</c>
/// (<c>StringHelpers.FirstNonBlank</c> / <c>FirstNonBlankOr</c> and the cached
/// <c>MediaEngineJson</c> options) against regressing back into per-file copies.
/// Both allowlists are seeded with the exact sites the stage-4 shared-primitives
/// audit found still declaring their own copy; wave 2 of stage 4 migrates each
/// call site onto the shared helper and removes its entry. New entries are
/// forbidden — new code must use the shared helper directly.
/// </summary>
public sealed class SharedPrimitivesGuardrailTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private const string StringHelpersOwnerFile = "src/MediaEngine.Domain/Services/StringHelpers.cs";
    private const string MediaEngineJsonOwnerFile = "src/MediaEngine.Domain/Services/MediaEngineJson.cs";

    private static readonly Regex FirstNonBlankDeclarationRegex =
        new(@"private\s+static\s+string\??\s+FirstNonBlank", RegexOptions.Compiled);

    private static readonly Regex NewJsonSerializerOptionsRegex =
        new(@"\bnew\s+(?:System\.Text\.Json\.)?JsonSerializerOptions\b", RegexOptions.Compiled);

    [Fact]
    public void OnlyStringHelpers_DeclaresAPrivateFirstNonBlankHelper()
    {
        var allowlist = ReadAllowlist("FirstNonBlankGuardrailAllowlist.txt");

        var offenders = ScanSourceFiles()
            .Where(relative => !relative.Equals(StringHelpersOwnerFile, StringComparison.OrdinalIgnoreCase))
            .Where(relative => !allowlist.Contains(relative))
            .Where(relative => FirstNonBlankDeclarationRegex.IsMatch(File.ReadAllText(Path.Combine(RepoRoot, relative))))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void FirstNonBlankAllowlist_ContainsNoStaleEntries()
    {
        // Keeps the allowlist honest: once wave 2 migrates a site off its local
        // FirstNonBlank copy, the entry must be deleted rather than left behind
        // to silently mask a future regression at that same file.
        var allowlist = ReadAllowlist("FirstNonBlankGuardrailAllowlist.txt");

        var stale = allowlist
            .Where(relative =>
            {
                var fullPath = Path.Combine(RepoRoot, relative);
                return !File.Exists(fullPath) || !FirstNonBlankDeclarationRegex.IsMatch(File.ReadAllText(fullPath));
            })
            .ToList();

        Assert.Empty(stale);
    }

    [Fact]
    public void OnlyMediaEngineJson_AllocatesAFreshJsonSerializerOptions()
    {
        var allowlist = ReadAllowlist("JsonSerializerOptionsGuardrailAllowlist.txt");

        var offenders = ScanSourceFiles()
            .Where(relative => !relative.Equals(MediaEngineJsonOwnerFile, StringComparison.OrdinalIgnoreCase))
            .Where(relative => !allowlist.Contains(relative))
            .Where(relative => NewJsonSerializerOptionsRegex.IsMatch(File.ReadAllText(Path.Combine(RepoRoot, relative))))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void JsonSerializerOptionsAllowlist_ContainsNoStaleEntries()
    {
        var allowlist = ReadAllowlist("JsonSerializerOptionsGuardrailAllowlist.txt");

        var stale = allowlist
            .Where(relative =>
            {
                var fullPath = Path.Combine(RepoRoot, relative);
                return !File.Exists(fullPath) || !NewJsonSerializerOptionsRegex.IsMatch(File.ReadAllText(fullPath));
            })
            .ToList();

        Assert.Empty(stale);
    }

    private static IEnumerable<string> ScanSourceFiles()
    {
        var srcRoot = Path.Combine(RepoRoot, "src");

        return Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(srcRoot, "*.razor", SearchOption.AllDirectories))
            .Select(ToRelativePath)
            .Where(IsActiveSourcePath);
    }

    private static bool IsActiveSourcePath(string relativePath)
    {
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return !segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private static string ToRelativePath(string path) =>
        Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');

    private static HashSet<string> ReadAllowlist(string fileName)
    {
        var path = Path.Combine(RepoRoot, "tests", "MediaEngine.Domain.Tests", fileName);
        Assert.True(File.Exists(path), $"Missing guardrail allowlist: {fileName}");

        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MediaEngine.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
