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
    private const string DefaultPrimary = "#7C4DFF"; // deep violet

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
    /// Rebuilds the theme with <paramref name="hexColor"/> as the primary accent
    /// (darkened and lightened variants are computed automatically) and notifies all
    /// subscribed components to re-render with the new palette.
    /// </summary>
    /// <param name="hexColor">
    /// Hex colour string, with or without a leading <c>#</c>
    /// (e.g. <c>"#FF8F00"</c> or <c>"FF8F00"</c>).
    /// </param>
    public void SetHubAccent(string hexColor)
    {
        if (string.IsNullOrWhiteSpace(hexColor)) return;
        Theme = BuildTheme(hexColor);
        OnThemeChanged?.Invoke();
    }

    // ── Theme construction ─────────────────────────────────────────────────────

    private static MudTheme BuildTheme(string primaryHex) => new()
    {
        LayoutProperties = new LayoutProperties
        {
            // 32 px border radius for glassmorphic Spatial Bento design.
            DefaultBorderRadius = "32px",
        },

        PaletteDark = new PaletteDark
        {
            Primary             = primaryHex,
            PrimaryDarken       = DarkenHex(primaryHex),
            PrimaryLighten      = LightenHex(primaryHex),
            Secondary           = "#00BFA5",   // teal — constant across hub selections
            SecondaryDarken     = "#009688",
            Background          = "#0F0F14",
            BackgroundGray      = "#1A1A24",
            Surface             = "#1E1E2E",
            AppbarBackground    = "#13131D",
            DrawerBackground    = "#13131D",
            DrawerText          = "rgba(255,255,255,0.80)",
            DrawerIcon          = "rgba(255,255,255,0.60)",
            TextPrimary         = "rgba(255,255,255,0.87)",
            TextSecondary       = "rgba(255,255,255,0.60)",
            TextDisabled        = "rgba(255,255,255,0.38)",
            ActionDefault       = "rgba(255,255,255,0.54)",
            LinesDefault        = "rgba(255,255,255,0.12)",
            Divider             = "rgba(255,255,255,0.12)",
            OverlayDark         = "rgba(0,0,0,0.7)",
            Error               = "#CF6679",
            Warning             = "#FFB74D",
            Info                = "#4FC3F7",
            Success             = "#81C784",
        },

        PaletteLight = new PaletteLight
        {
            Primary             = DarkenHex(primaryHex, 0.80),
            PrimaryDarken       = DarkenHex(primaryHex, 0.55),
            PrimaryLighten      = LightenHex(primaryHex, 32),
            Secondary           = "#00897B",
            SecondaryDarken     = "#00695C",
            Background          = "#F4F4F8",
            BackgroundGray      = "#E8E8EE",
            Surface             = "#FFFFFF",
            AppbarBackground    = "#FFFFFF",
            AppbarText          = "rgba(0,0,0,0.87)",
            DrawerBackground    = "#FFFFFF",
            DrawerText          = "rgba(0,0,0,0.75)",
            DrawerIcon          = "rgba(0,0,0,0.55)",
            TextPrimary         = "rgba(0,0,0,0.87)",
            TextSecondary       = "rgba(0,0,0,0.60)",
            TextDisabled        = "rgba(0,0,0,0.38)",
            ActionDefault       = "rgba(0,0,0,0.54)",
            LinesDefault        = "rgba(0,0,0,0.12)",
            Divider             = "rgba(0,0,0,0.12)",
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
                FontFamily = ["Montserrat", "sans-serif"],
                FontSize   = "1rem",
            },
            Body1   = new Body1Typography  { FontSize   = "1rem" },
            Body2   = new Body2Typography  { FontSize   = "0.9rem" },
            H4      = new H4Typography     { FontWeight = "600" },
            H5      = new H5Typography     { FontWeight = "600" },
            H6      = new H6Typography     { FontWeight = "600" },
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
