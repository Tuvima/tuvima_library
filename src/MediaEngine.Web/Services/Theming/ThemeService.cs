using MudBlazor;

namespace MediaEngine.Web.Services.Theming;

/// <summary>
/// Manages the active UI theme. Dark mode only — light mode has been removed.
/// The accent colour is fixed to golden amber.
/// </summary>
public sealed class ThemeService
{
    private const string DefaultPrimary = "#EAB308"; // amber gold — cinematic accent

    /// <summary>Always dark — light mode has been removed.</summary>
    public bool IsDarkMode => true;

    /// <summary>
    /// The active MudBlazor theme. Uses the cinematic dark palette exclusively.
    /// </summary>
    public MudTheme Theme { get; } = BuildTheme(DefaultPrimary);

    /// <summary>
    /// Legacy API — retained so existing callers compile. No-op: accent is fixed.
    /// </summary>
    public void SetHubAccent(string hexColor) { }

    // ── Theme construction ─────────────────────────────────────────────────────

    private static MudTheme BuildTheme(string primaryHex) => new()
    {
        LayoutProperties = new LayoutProperties
        {
            // 8 px border radius — rectangular with a light curve.
            DefaultBorderRadius = "8px",
        },

        PaletteDark = new PaletteDark
        {
            // Cinematic dark palette - muted and ultra-premium
            Primary             = primaryHex,
            PrimaryDarken       = DarkenHex(primaryHex),
            PrimaryLighten      = LightenHex(primaryHex),
            Secondary           = "#9CA3AF",
            SecondaryDarken     = "#4B5563",
            Background          = "#080B14",
            BackgroundGray      = "#0C1020",
            Surface             = "#0C1020",
            AppbarBackground    = "#080B14",
            DrawerBackground    = "#0C1020",
            DrawerText          = "#F3F4F6",
            DrawerIcon          = "#9CA3AF",
            TextPrimary         = "#F3F4F6",
            TextSecondary       = "#9CA3AF",
            TextDisabled        = "#4B5563",
            ActionDefault       = "rgba(243,244,246,0.54)",
            LinesDefault        = "rgba(255,255,255,0.08)",
            Divider             = "rgba(255,255,255,0.08)",
            OverlayDark         = "rgba(0,0,0,0.8)",
            Error               = "#CF6679",
            Warning             = "#FFB74D",
            Info                = "#4FC3F7",
            Success             = "#81C784",
        },

        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily   = ["Montserrat", "sans-serif"],
                FontSize     = "1rem",
                LineHeight   = "1.6",
            },
            // Headlines — Bold, tight tracking
            H1    = new H1Typography    { FontWeight = "800", LetterSpacing = "-0.03em", LineHeight = "1.1" },
            // Section headers — SemiBold
            H2    = new H2Typography    { FontWeight = "700", LetterSpacing = "-0.02em" },
            H3    = new H3Typography    { FontWeight = "700", LetterSpacing = "-0.02em" },
            // Media titles — SemiBold (H4/H5/H6)
            H4    = new H4Typography    { FontWeight = "600", LetterSpacing = "-0.01em" },
            H5    = new H5Typography    { FontWeight = "600", LetterSpacing = "-0.01em" },
            H6    = new H6Typography    { FontWeight = "600", LetterSpacing = "-0.01em" },
            // Body — Regular, comfortable line-height for readability
            Body1 = new Body1Typography { FontWeight = "400", FontSize = "1rem",   LineHeight = "1.6" },
            Body2 = new Body2Typography { FontWeight = "400", FontSize = "0.9rem", LineHeight = "1.6" },
            // Subtitles & Component Titles
            Subtitle1 = new Subtitle1Typography { FontWeight = "600", FontSize = "1rem", LineHeight = "1.5" },
            Subtitle2 = new Subtitle2Typography { FontWeight = "500", FontSize = "0.875rem", LineHeight = "1.5" },
            // Metadata Tags (Tiny, bold, wide tracking, uppercase)
            Overline = new OverlineTypography { FontWeight = "600", FontSize = "0.7rem", LetterSpacing = "0.08em", TextTransform = "uppercase" }
        },
    };

    // ── Colour helpers ─────────────────────────────────────────────────────────

    /// <summary>Multiplies each RGB channel by <paramref name="factor"/> to produce a darker shade.</summary>
    private static string DarkenHex(string hex, double factor = 0.72)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 3) hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
            if (hex.Length != 6) return $"#{hex}";

            var r = Math.Clamp((int)(Convert.ToInt32(hex[..2],  16) * factor), 0, 255);
            var g = Math.Clamp((int)(Convert.ToInt32(hex[2..4], 16) * factor), 0, 255);
            var b = Math.Clamp((int)(Convert.ToInt32(hex[4..6], 16) * factor), 0, 255);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        catch { return hex; }
    }

    /// <summary>Adds <paramref name="amount"/> to each RGB channel to produce a lighter shade.</summary>
    private static string LightenHex(string hex, int amount = 48)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 3) hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
            if (hex.Length != 6) return $"#{hex}";

            var r = Math.Clamp(Convert.ToInt32(hex[..2],  16) + amount, 0, 255);
            var g = Math.Clamp(Convert.ToInt32(hex[2..4], 16) + amount, 0, 255);
            var b = Math.Clamp(Convert.ToInt32(hex[4..6], 16) + amount, 0, 255);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        catch { return hex; }
    }
}
