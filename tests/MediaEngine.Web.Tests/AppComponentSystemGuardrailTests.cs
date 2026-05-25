using System.Text.RegularExpressions;

namespace MediaEngine.Web.Tests;

public sealed class AppComponentSystemGuardrailTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string[] AppComponentMigratedFiles =
    [
        "src/MediaEngine.Web/Components/Settings/ProviderPriorityTab.razor",
        "src/MediaEngine.Web/Components/Pages/DesignSystemPreview.razor",
    ];

    private static readonly Regex RawPrimitiveRegex =
        new(@"<(?:button|input|select|textarea)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InlineStyleRegex =
        new(@"\b[Ss]tyle\s*=",
            RegexOptions.Compiled);

    private static readonly Regex RazorStyleBlockRegex =
        new(@"<style\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DirectMudPrimitiveRegex =
        new(@"<Mud(?:TextField|Select|NumericField|Autocomplete|Button|IconButton|Chip|Paper|Alert|Switch|Tabs|Table|Dialog)\b",
            RegexOptions.Compiled);

    public static IEnumerable<object[]> MigratedFilesData() =>
        AppComponentMigratedFiles.Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(MigratedFilesData))]
    public void MigratedFiles_UseAppComponentsForPrimitiveControls(string relativePath)
    {
        var contents = ReadRepoFile(relativePath);

        Assert.False(RawPrimitiveRegex.IsMatch(contents), $"Raw interactive HTML found in {relativePath}.");
        Assert.False(InlineStyleRegex.IsMatch(contents), $"Inline style attribute found in {relativePath}.");
        Assert.False(RazorStyleBlockRegex.IsMatch(contents), $"Razor style block found in {relativePath}.");
        Assert.False(DirectMudPrimitiveRegex.IsMatch(contents), $"Direct MudBlazor primitive found in {relativePath}.");
    }

    [Fact]
    public void ProviderPriorityTab_UsesApprovedProviderComponentLayer()
    {
        var contents = ReadRepoFile("src/MediaEngine.Web/Components/Settings/ProviderPriorityTab.razor");

        Assert.Contains("<AppTextField", contents, StringComparison.Ordinal);
        Assert.Contains("<AppSelect", contents, StringComparison.Ordinal);
        Assert.Contains("<AppNumericField", contents, StringComparison.Ordinal);
        Assert.Contains("<AppSwitchRow", contents, StringComparison.Ordinal);
        Assert.Contains("<AppButton", contents, StringComparison.Ordinal);
        Assert.Contains("<AppIconButton", contents, StringComparison.Ordinal);
        Assert.Contains("<AppChip", contents, StringComparison.Ordinal);
        Assert.Contains("<AppAlert", contents, StringComparison.Ordinal);
        Assert.Contains("<AppProviderLogo", contents, StringComparison.Ordinal);
        Assert.Contains("<AppDialog", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void NewRazorFilesOutsideShared_DoNotAddLegacyPrimitiveControls()
    {
        var allowedLegacyFiles = ReadLegacyAllowlist();
        var componentRoot = Path.Combine(RepoRoot, "src", "MediaEngine.Web", "Components");

        var offenders = Directory.EnumerateFiles(componentRoot, "*.razor", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = path,
                RelativePath = ToRelativePath(path),
                Contents = File.ReadAllText(path),
            })
            .Where(file => !file.RelativePath.Contains("/Components/Shared/", StringComparison.OrdinalIgnoreCase))
            .Where(file => ContainsLegacyPrimitive(file.Contents))
            .Where(file => !allowedLegacyFiles.Contains(file.RelativePath))
            .Select(file => file.RelativePath)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SharedCss_DefinesCanonicalControlSizingContract()
    {
        var tokens = ReadRepoFile("src/MediaEngine.Web/wwwroot/tuvima.tokens.css");
        var appCss = ReadRepoFile("src/MediaEngine.Web/wwwroot/app.css");

        Assert.Contains("--tl-control-height-compact: var(--tl-control-height-sm);", tokens, StringComparison.Ordinal);
        Assert.Contains("--tl-control-height-normal: var(--tl-control-height-md);", tokens, StringComparison.Ordinal);
        Assert.Contains("--tl-control-height-large: var(--tl-control-height-lg);", tokens, StringComparison.Ordinal);
        Assert.Contains(".app-control--compact", appCss, StringComparison.Ordinal);
        Assert.Contains(".app-control--normal", appCss, StringComparison.Ordinal);
        Assert.Contains(".app-control--large", appCss, StringComparison.Ordinal);
        Assert.Contains(".app-button", appCss, StringComparison.Ordinal);
        Assert.Contains(".app-chip", appCss, StringComparison.Ordinal);
        Assert.Contains(".app-provider-logo", appCss, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Missing guardrail target: {relativePath}.");
        return File.ReadAllText(path);
    }

    private static HashSet<string> ReadLegacyAllowlist()
    {
        var path = Path.Combine(RepoRoot, "tests", "MediaEngine.Web.Tests", "AppComponentSystemLegacyAllowlist.txt");
        Assert.True(File.Exists(path), "Missing app component system legacy allowlist.");

        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool ContainsLegacyPrimitive(string contents) =>
        RawPrimitiveRegex.IsMatch(contents)
        || InlineStyleRegex.IsMatch(contents)
        || RazorStyleBlockRegex.IsMatch(contents)
        || DirectMudPrimitiveRegex.IsMatch(contents);

    private static string ToRelativePath(string path) =>
        Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');
}
