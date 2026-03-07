using MudBlazor;

namespace MediaEngine.Web.Services.Theming;

/// <summary>
/// Manages the active UI theme.  Singleton: the MudTheme object is shared across
/// all circuits; each circuit holds its own <c>_isDark</c> flag, synced via
/// <see cref="OnThemeChanged"/>.
///
/// <para>
/// <b>Dynamic accent:</b> call <see cref="SetHubAccent"/> when the user selects a Hub.
/// This rebuilds <see cref="Theme"/> with the Hub's brand colour as the primary
/// accent and fires <see cref="OnThemeChanged"/> so every subscribed circuit re-renders.
/// </para>
/// </summary>
public sealed class ThemeService
{
    private const string DefaultPrimary = "#EAB308"; // amber gold — cinematic accent

    /// <summary>Default to Dark Mode as specified in Section 1.2 of the UI ASD.</summary>
    public bool IsDarkMode { get; private set; } = true;

    /// <summary>
    /// The active MudBlazor theme.  Rebuilt each time <see cref="SetHubAccent"/> is called;
    /// <c>MainLayout</c> reads this property on every re-render triggered by <see cref="OnThemeChanged"/>.
    /// </summary>
    public MudTheme Theme { get; private set; } = BuildTheme(DefaultPrimary);

    /// <summary>
    /// Fired on dark/light toggle and on Hub accent changes so components can update
    /// their local state.  May fire from a background thread — use
    /// <c>InvokeAsync(StateHasChanged)</c> in component handlers.
    /// </summary>
    public event Action? OnThemeChanged;

    /// <summary>Toggles dark / light mode and notifies subscribers.</summary>
    public void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
        OnThemeChanged?.Invoke();
    }

    /// <summary>
    /// Legacy API — previously rebuilt the theme with a dynamic accent colour.
    /// Now a no-op: the accent is fixed to golden amber (<see cref="DefaultPrimary"/>).
    /// Retained so existing callers compile without changes; they will be cleaned up over time.
    /// </summary>
    public void SetHubAccent(string hexColor)
    {
        // No-op: accent is fixed to golden amber.
    }

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
            Secondary           = "#9CA3AF", // Muted grey for descriptions
            SecondaryDarken     = "#4B5563", // Disabled/Dark grey
            Background          = "#000000", // Pure black for immersive cinematic feel
            BackgroundGray      = "#0A0A0A",
            Surface             = "#0A0A0A", // Very dark grey for cards/surfaces
            AppbarBackground    = "#000000",
            DrawerBackground    = "#0A0A0A",
            DrawerText          = "#F3F4F6", // Off-white to prevent eye strain
            DrawerIcon          = "#9CA3AF",
            TextPrimary         = "#F3F4F6", // Off-white
            TextSecondary       = "#9CA3AF", // Muted grey
            TextDisabled        = "#4B5563", // Dark grey
            ActionDefault       = "rgba(243,244,246,0.54)",
            LinesDefault        = "rgba(255,255,255,0.08)", // Extremely subtle border
            Divider             = "rgba(255,255,255,0.08)", // Extremely subtle line
            OverlayDark         = "rgba(0,0,0,0.8)",
            Error               = "#CF6679",
            Warning             = "#FFB74D",
            Info                = "#4FC3F7",
            Success             = "#81C784",
        },

        PaletteLight = new PaletteLight
        {
            // Editorial light palette
            Primary             = DarkenHex(primaryHex, 0.80),
            PrimaryDarken       = DarkenHex(primaryHex, 0.55),
            PrimaryLighten      = LightenHex(primaryHex, 32),
            Secondary           = "#6B7280",
            SecondaryDarken     = "#4B5563",
            Background          = "#F5F5F5",   // Editorial — off-white
            BackgroundGray      = "#EBEBEB",
            Surface             = "#FFFFFF",   // Editorial surface — pure white
            AppbarBackground    = "#FFFFFF",
            AppbarText          = "#171717",
            DrawerBackground    = "#FFFFFF",
            DrawerText          = "#171717",
            DrawerIcon          = "#525252",
            TextPrimary         = "#171717",   // Editorial — near-black
            TextSecondary       = "#525252",   // Editorial secondary
            TextDisabled        = "rgba(23,23,23,0.38)",
            ActionDefault       = "rgba(23,23,23,0.54)",
            LinesDefault        = "rgba(0,0,0,0.10)",
            Divider             = "rgba(0,0,0,0.10)",
            OverlayDark         = "rgba(0,0,0,0.4)",
            Error               = "#D32F2F",
            Warning             = "#EF6C00",
            Info                = "#0277BD",
            Success             = "#2E7D32",
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
