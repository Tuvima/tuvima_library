using MudBlazor;

namespace MediaEngine.Web.Services.Theming;

/// <summary>
/// Manages the active UI theme. Dark mode only â€” light mode has been removed.
/// The accent colour is fixed to golden amber, sourced from <see cref="PaletteProvider"/>.
/// </summary>
public sealed class ThemeService
{
    /// <summary>Always dark â€” light mode has been removed.</summary>
    public bool IsDarkMode => true;

    /// <summary>
    /// The active MudBlazor theme. Uses the cinematic dark palette exclusively.
    /// Colours are sourced from <see cref="PaletteProvider.Current"/> at construction time.
    /// </summary>
    public MudTheme Theme { get; } = BuildTheme();

    /// <summary>
    /// Legacy API â€” retained so existing callers compile. No-op: accent is fixed.
    /// </summary>
    public void SetCollectionAccent(string hexColor) { }

    private static MudTheme BuildTheme()
    {
        var t = PaletteProvider.Current.Theme;
        var primaryHex = t.Primary;

        return new MudTheme
        {
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = "4px",
            },

            PaletteDark = new PaletteDark
            {
                Primary          = primaryHex,
                PrimaryDarken    = DarkenHex(primaryHex),
                PrimaryLighten   = LightenHex(primaryHex),
                Secondary        = t.Secondary,
                SecondaryDarken  = t.TextDisabled,
                Background       = t.Background,
                BackgroundGray   = t.Surface,
                Surface          = t.Surface,
                AppbarBackground = t.Background,
                DrawerBackground = t.Surface,
                DrawerText       = t.TextPrimary,
                DrawerIcon       = t.TextSecondary,
                TextPrimary      = t.TextPrimary,
                TextSecondary    = t.TextSecondary,
                TextDisabled     = t.TextDisabled,
                ActionDefault    = "rgba(243,244,246,0.54)",
                LinesDefault     = "rgba(255,255,255,0.08)",
                Divider          = "rgba(255,255,255,0.08)",
                OverlayDark      = "rgba(0,0,0,0.8)",
                Error            = t.Error,
                Warning          = t.Warning,
                Info             = t.Info,
                Success          = t.Success,
            },

            Typography = new Typography
            {
                Default = new DefaultTypography
                {
                    FontFamily = ["Inter", "Montserrat", "Segoe UI", "sans-serif"],
                    FontSize = "1rem",
                    LineHeight = "1.55",
                },
                H1 = new H1Typography { FontWeight = "800", LetterSpacing = "-0.04em", LineHeight = "1.05" },
                H2 = new H2Typography { FontWeight = "700", LetterSpacing = "-0.03em", LineHeight = "1.1" },
                H3 = new H3Typography { FontWeight = "700", LetterSpacing = "-0.025em", LineHeight = "1.15" },
                H4 = new H4Typography { FontWeight = "700", LetterSpacing = "-0.02em", LineHeight = "1.2" },
                H5 = new H5Typography { FontWeight = "700", LetterSpacing = "-0.015em", LineHeight = "1.25" },
                H6 = new H6Typography { FontWeight = "700", LetterSpacing = "-0.01em", LineHeight = "1.3" },
                Body1 = new Body1Typography { FontWeight = "400", FontSize = "1rem", LineHeight = "1.6" },
                Body2 = new Body2Typography { FontWeight = "400", FontSize = "0.92rem", LineHeight = "1.58" },
                Subtitle1 = new Subtitle1Typography { FontWeight = "600", FontSize = "1rem", LineHeight = "1.5" },
                Subtitle2 = new Subtitle2Typography { FontWeight = "600", FontSize = "0.875rem", LineHeight = "1.45" },
                Overline = new OverlineTypography { FontWeight = "700", FontSize = "0.72rem", LetterSpacing = "0.12em", TextTransform = "uppercase" },
            },
        };
    }

    /// <summary>Multiplies each RGB channel by <paramref name="factor"/> to produce a darker shade.</summary>
    private static string DarkenHex(string hex, double factor = 0.72)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 3) hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
            if (hex.Length != 6) return $"#{hex}";

            var r = Math.Clamp((int)(Convert.ToInt32(hex[..2], 16) * factor), 0, 255);
            var g = Math.Clamp((int)(Convert.ToInt32(hex[2..4], 16) * factor), 0, 255);
            var b = Math.Clamp((int)(Convert.ToInt32(hex[4..6], 16) * factor), 0, 255);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        catch
        {
            return hex;
        }
    }

    /// <summary>Adds <paramref name="amount"/> to each RGB channel to produce a lighter shade.</summary>
    private static string LightenHex(string hex, int amount = 48)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 3) hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
            if (hex.Length != 6) return $"#{hex}";

            var r = Math.Clamp(Convert.ToInt32(hex[..2], 16) + amount, 0, 255);
            var g = Math.Clamp(Convert.ToInt32(hex[2..4], 16) + amount, 0, 255);
            var b = Math.Clamp(Convert.ToInt32(hex[4..6], 16) + amount, 0, 255);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        catch
        {
            return hex;
        }
    }
}
