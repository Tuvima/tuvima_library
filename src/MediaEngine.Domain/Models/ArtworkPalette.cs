using System.Text.Json.Serialization;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Models;

public sealed record class ArtworkPalette
{
    [JsonPropertyName("base_color")]
    public string BaseColor { get; init; } = "";

    [JsonPropertyName("base_color_dark")]
    public string BaseColorDark { get; init; } = "";

    [JsonPropertyName("accent_color")]
    public string AccentColor { get; init; } = "";

    [JsonPropertyName("accent_color_muted")]
    public string AccentColorMuted { get; init; } = "";

    [JsonPropertyName("glow_color")]
    public string GlowColor { get; init; } = "";

    [JsonPropertyName("secondary_glow_color")]
    public string SecondaryGlowColor { get; init; } = "";

    [JsonPropertyName("text_overlay_color")]
    public string TextOverlayColor { get; init; } = "";

    [JsonPropertyName("border_color")]
    public string BorderColor { get; init; } = "";

    [JsonPropertyName("shadow_color")]
    public string ShadowColor { get; init; } = "";

    [JsonPropertyName("css_gradient")]
    public string CssGradient { get; init; } = "";

    [JsonPropertyName("css_radial_glow")]
    public string CssRadialGlow { get; init; } = "";

    [JsonPropertyName("css_variables")]
    public IReadOnlyDictionary<string, string> CssVariables { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("css_variable_style")]
    public string CssVariableStyle { get; init; } = "";

    [JsonPropertyName("is_dark_safe")]
    public bool IsDarkSafe { get; init; }

    [JsonPropertyName("contrast_score")]
    public double ContrastScore { get; init; }

    public static ArtworkPalette TuvimaDefault(bool generateCssStrings = true)
    {
        var palette = new ArtworkPalette
        {
            BaseColor = "#0c0a14",
            BaseColorDark = "#070812",
            AccentColor = "#7c5cff",
            AccentColorMuted = "rgba(124, 92, 255, 0.28)",
            GlowColor = "rgba(245, 158, 80, 0.28)",
            SecondaryGlowColor = "rgba(82, 184, 255, 0.20)",
            TextOverlayColor = "rgba(5, 8, 16, 0.74)",
            BorderColor = "rgba(255, 255, 255, 0.12)",
            ShadowColor = "rgba(0, 0, 0, 0.48)",
            IsDarkSafe = true,
            ContrastScore = 17.5d,
        };

        return generateCssStrings
            ? WithCss()
            : palette with
            {
                CssVariables = BuildVariables(palette),
                CssVariableStyle = BuildVariableStyle(BuildVariables(palette)),
            };

        ArtworkPalette WithCss()
        {
            var gradient = "linear-gradient(90deg, rgba(7, 8, 18, 0.96) 0%, rgba(12, 10, 20, 0.90) 42%, rgba(124, 92, 255, 0.34) 100%)";
            var glow = "radial-gradient(circle at 72% 48%, rgba(245, 158, 80, 0.28) 0%, rgba(82, 184, 255, 0.20) 38%, transparent 72%)";
            var completed = palette with
            {
                CssGradient = gradient,
                CssRadialGlow = glow,
            };
            var variables = BuildVariables(completed);
            return completed with
            {
                CssVariables = variables,
                CssVariableStyle = BuildVariableStyle(variables),
            };
        }
    }

    public static IReadOnlyDictionary<string, string> BuildVariables(ArtworkPalette palette) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["--art-bg-base"] = palette.BaseColor,
            ["--art-bg-base-dark"] = palette.BaseColorDark,
            ["--art-bg-accent"] = palette.AccentColor,
            ["--art-bg-accent-muted"] = palette.AccentColorMuted,
            ["--art-bg-glow"] = palette.GlowColor,
            ["--art-bg-glow-secondary"] = palette.SecondaryGlowColor,
            ["--art-bg-border"] = palette.BorderColor,
            ["--art-bg-shadow"] = palette.ShadowColor,
            ["--art-bg-overlay"] = palette.TextOverlayColor,
            ["--art-bg-gradient"] = palette.CssGradient,
            ["--art-bg-radial-glow"] = palette.CssRadialGlow,
        };

    public static string BuildVariableStyle(IReadOnlyDictionary<string, string> variables) =>
        string.Join("; ", variables.Select(pair => $"{pair.Key}: {pair.Value}")) + ";";
}

public sealed class ArtworkPaletteSource
{
    public string Id { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public string? LocalPath { get; init; }
    public MediaType? MediaType { get; init; }
    public ArtworkShape? Shape { get; init; }
}

public enum ArtworkShape
{
    Square,
    Portrait,
    Wide,
}

public sealed class ArtworkPaletteOptions
{
    public bool PreferDarkBackground { get; init; } = true;
    public bool GenerateCssStrings { get; init; } = true;
    public int MaxImagesToAnalyze { get; init; } = 5;
    public string? StableSeed { get; init; }
}
