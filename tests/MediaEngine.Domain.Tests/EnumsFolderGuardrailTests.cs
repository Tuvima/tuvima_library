namespace MediaEngine.Domain.Tests;

/// <summary>
/// Guards against static-class string-constant holders being reintroduced under
/// <c>src/MediaEngine.Domain/Enums/</c>. That folder is reserved for genuine
/// C# <c>enum</c> declarations; string-constant catalogues belong in
/// <c>src/MediaEngine.Domain/Constants/</c> instead.
/// </summary>
public sealed class EnumsFolderGuardrailTests
{
    [Fact]
    public void EveryFileInEnumsFolder_DeclaresAnEnum()
    {
        var repoRoot = FindRepoRoot();
        var enumsRoot = Path.Combine(repoRoot, "src", "MediaEngine.Domain", "Enums");

        var offenders = Directory.EnumerateFiles(enumsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !File.ReadAllText(path).Contains("enum ", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .ToList();

        Assert.Empty(offenders);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MediaEngine.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
