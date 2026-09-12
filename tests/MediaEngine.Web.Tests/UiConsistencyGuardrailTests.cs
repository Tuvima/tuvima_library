using System.Globalization;
using System.Text.RegularExpressions;

namespace MediaEngine.Web.Tests;

public sealed partial class UiConsistencyGuardrailTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void AppButtons_UseOnlyTheSharedSemanticApi()
    {
        var violations = RazorFiles()
            .SelectMany(path => AppButtonTag().Matches(File.ReadAllText(path)).Select(match => (path, match.Value)))
            .Where(item => LegacyButtonAttribute().IsMatch(item.Value))
            .Select(item => Relative(item.path))
            .Distinct()
            .ToArray();

        Assert.True(violations.Length == 0,
            $"AppButton and AppIconButton must use ButtonStyle, Tone, and AppControlSize. Legacy Variant/Color attributes remain in: {string.Join(", ", violations)}");

        var appButton = Read("src/MediaEngine.Web/Components/Shared/AppButton.razor");
        Assert.DoesNotContain("[Parameter] public Color? Color", appButton, StringComparison.Ordinal);
        Assert.DoesNotContain("[Parameter] public Variant? Variant", appButton, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public bool Loading", appButton, StringComparison.Ordinal);
    }

    [Fact]
    public void RawMudButtons_RemainInsideSharedControlComponents()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "src/MediaEngine.Web/Components/Shared/AppButton.razor",
            "src/MediaEngine.Web/Components/Shared/AppDialogShell.razor",
            "src/MediaEngine.Web/Components/Shared/AppIconButton.razor",
        };

        var violations = RazorFiles()
            .Where(path => RawMudButton().IsMatch(File.ReadAllText(path)))
            .Select(Relative)
            .Where(path => !allowed.Contains(path))
            .ToArray();

        Assert.True(violations.Length == 0,
            $"Raw MudButton/MudIconButton usage bypasses the shared visual contract in: {string.Join(", ", violations)}");
    }

    [Fact]
    public void UserFacingType_DoesNotFallBelowTwelvePixels()
    {
        var roots = new[]
        {
            Path.Combine(RepoRoot, "src", "MediaEngine.Web", "Components"),
            Path.Combine(RepoRoot, "src", "MediaEngine.Web", "Shared"),
            Path.Combine(RepoRoot, "src", "MediaEngine.Web", "wwwroot"),
        };

        var violations = roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Where(path => NumericFontSize().Matches(File.ReadAllText(path)).Any(match => ToPixels(match) < 12d))
            .Select(Relative)
            .ToArray();

        Assert.True(violations.Length == 0,
            $"Readable UI text has a 12px minimum. Sub-floor declarations remain in: {string.Join(", ", violations)}");
    }

    [Fact]
    public void PrimarySettingsActions_KeepTheCanonicalFilledTreatment()
    {
        var expectations = new Dictionary<string, string>
        {
            ["src/MediaEngine.Web/Components/Settings/IngestionScanAction.razor"] = "Label=\"Scan now\"",
            ["src/MediaEngine.Web/Components/Settings/LocalNetworkSettingsPanel.razor"] = "Label=\"Save\" ButtonStyle=\"AppButtonStyle.Filled\" Tone=\"AppUiTone.Primary\"",
            ["src/MediaEngine.Web/Components/Settings/NetworkStreamingSettingsPanel.razor"] = "Label=\"Save streaming settings\" ButtonStyle=\"AppButtonStyle.Filled\" Tone=\"AppUiTone.Primary\"",
            ["src/MediaEngine.Web/Components/Settings/RemoteAccessSettingsPanel.razor"] = "Label=\"Save and verify\" ButtonStyle=\"AppButtonStyle.Filled\" Tone=\"AppUiTone.Primary\"",
        };

        foreach (var (path, expected) in expectations)
            Assert.Contains(expected, Read(path), StringComparison.Ordinal);
    }

    private static IEnumerable<string> RazorFiles() =>
        Directory.EnumerateFiles(
            Path.Combine(RepoRoot, "src", "MediaEngine.Web"),
            "*.razor",
            SearchOption.AllDirectories)
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static double ToPixels(Match match)
    {
        var value = double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        return match.Groups["unit"].Value.Equals("rem", StringComparison.OrdinalIgnoreCase)
            ? value * 16d
            : value;
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath));

    private static string Relative(string path) =>
        Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');

    [GeneratedRegex("<App(?:Icon)?Button\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex AppButtonTag();

    [GeneratedRegex("\\s(?:Variant|Color)\\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyButtonAttribute();

    [GeneratedRegex("<Mud(?:Icon)?Button\\b", RegexOptions.IgnoreCase)]
    private static partial Regex RawMudButton();

    [GeneratedRegex("font-size\\s*:\\s*(?<value>(?:\\d+(?:\\.\\d+)?|\\.\\d+))(?<unit>px|rem)", RegexOptions.IgnoreCase)]
    private static partial Regex NumericFontSize();
}
