namespace MediaEngine.Api.Tests;

public sealed class SourceEncodingGuardrailTests
{
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".razor",
        ".sql",
        ".json",
        ".js",
        ".css",
        ".ps1",
        ".props",
        ".targets",
        ".xml",
        ".html",
        ".md",
    };

    private static readonly IReadOnlyList<string> MojibakeMarkers =
    [
        FromCodePoints(0x00C2, 0x00B7),
        FromCodePoints(0x00C2, 0x00A7),
        FromCodePoints(0x00C3, 0x201A),
        FromCodePoints(0x00C3, 0x00A2),
        FromCodePoints(0x00C3, 0x00B1),
        FromCodePoints(0x00E2, 0x20AC),
        FromCodePoints(0x00E2, 0x201D),
        FromCodePoints(0x00E2, 0x2020),
        FromCodePoints(0x00E2, 0x2022),
        FromCodePoints(0x00E2, 0x0153),
        FromCodePoints(0xFFFD),
    ];

    [Fact]
    public void ProductAndHarnessSource_DoesNotContainMojibake()
    {
        var repoRoot = FindRepoRoot();
        var roots = new[]
        {
            Path.Combine(repoRoot, "src"),
            Path.Combine(repoRoot, "config"),
            Path.Combine(repoRoot, "tools"),
        };
        var directorySeparator = Path.DirectorySeparatorChar;
        var excludedSegments = new[]
        {
            $"{directorySeparator}bin{directorySeparator}",
            $"{directorySeparator}obj{directorySeparator}",
            $"{directorySeparator}reports{directorySeparator}",
        };
        var offenders = roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(path => SourceExtensions.Contains(Path.GetExtension(path)))
            .Where(path => excludedSegments.All(segment =>
                !path.Contains(segment, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                return MojibakeMarkers
                    .Where(source.Contains)
                    .Select(marker =>
                        $"{Path.GetRelativePath(repoRoot, path)} contains {FormatCodePoints(marker)}");
            })
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Source files contain text that was decoded with the wrong character encoding:\n"
            + string.Join("\n", offenders));
    }

    private static string FromCodePoints(params int[] codePoints)
        => string.Concat(codePoints.Select(char.ConvertFromUtf32));

    private static string FormatCodePoints(string value)
        => string.Join(" ", value.Select(character => $"U+{(int)character:X4}"));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
