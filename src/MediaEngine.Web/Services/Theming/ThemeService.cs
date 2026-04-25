using MudBlazor;

namespace MediaEngine.Web.Services.Theming;

/// <summary>
/// Manages the active UI theme. Dark mode only; light mode has been removed.
/// The theme is aligned to the centralized hybrid Paces + Metronic admin system.
/// </summary>
public sealed class ThemeService
{
    /// <summary>Always dark; light mode has been removed.</summary>
    public bool IsDarkMode => true;

    /// <summary>
    /// The active MudBlazor theme. Uses the centralized dark admin palette.
    /// </summary>
    public MudTheme Theme { get; } = BuildTheme();

    /// <summary>
    /// Legacy API retained so existing callers compile. No-op: accent is fixed.
    /// </summary>
    public void SetCollectionAccent(string hexColor) { }

    private static MudTheme BuildTheme()
    {
        const string primaryHex = "#236DC9";

        return new MudTheme
        {
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = "5px",
            },

            PaletteDark = new PaletteDark
            {
                Primary          = primaryHex,
                PrimaryDarken    = DarkenHex(primaryHex),
                PrimaryLighten   = LightenHex(primaryHex),
                Secondary        = "#7B70EF",
                SecondaryDarken  = "#8391A2",
                Background       = "#17181E",
                BackgroundGray   = "#1E1F27",
                Surface          = "#1E1F27",
                AppbarBackground = "#1E1F27",
                DrawerBackground = "#1E1F27",
                DrawerText       = "#AAB8C5",
                DrawerIcon       = "#8391A2",
                TextPrimary      = "#AAB8C5",
                TextSecondary    = "#8391A2",
                TextDisabled     = "rgba(131,145,162,0.52)",
                ActionDefault    = "#8391A2",
                LinesDefault     = "#293036",
                Divider          = "#293036",
                OverlayDark      = "rgba(0,0,0,0.72)",
                Error            = "#F8285A",
                Warning          = "#F6C000",
                Info             = "#5BC3E1",
                Success          = "#17C653",
            },

            Typography = new Typography
            {
                Default = new DefaultTypography
                {
                    FontFamily = ["Nunito", "Segoe UI", "sans-serif"],
                    FontSize = "14px",
                    FontWeight = "400",
                    LineHeight = "1.5",
                },
                H1 = new H1Typography { FontWeight = "700", FontSize = "20px", LetterSpacing = "0", LineHeight = "1.3" },
                H2 = new H2Typography { FontWeight = "700", FontSize = "18px", LetterSpacing = "0", LineHeight = "1.35" },
                H3 = new H3Typography { FontWeight = "700", FontSize = "17px", LetterSpacing = "0", LineHeight = "1.35" },
                H4 = new H4Typography { FontWeight = "600", FontSize = "16px", LetterSpacing = "0", LineHeight = "1.4" },
                H5 = new H5Typography { FontWeight = "600", FontSize = "14px", LetterSpacing = "0", LineHeight = "1.4" },
                H6 = new H6Typography { FontWeight = "600", FontSize = "13px", LetterSpacing = "0", LineHeight = "1.4" },
                Body1 = new Body1Typography { FontWeight = "400", FontSize = "14px", LineHeight = "1.5" },
                Body2 = new Body2Typography { FontWeight = "400", FontSize = "13px", LineHeight = "1.5" },
                Subtitle1 = new Subtitle1Typography { FontWeight = "600", FontSize = "14px", LineHeight = "1.5" },
                Subtitle2 = new Subtitle2Typography { FontWeight = "600", FontSize = "13px", LineHeight = "1.45" },
                Button = new ButtonTypography { FontWeight = "500", FontSize = "14px", TextTransform = "none" },
                Caption = new CaptionTypography { FontWeight = "400", FontSize = "12px", LineHeight = "1.4" },
                Overline = new OverlineTypography { FontWeight = "600", FontSize = "11px", LetterSpacing = "0.02em", TextTransform = "uppercase" },
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
