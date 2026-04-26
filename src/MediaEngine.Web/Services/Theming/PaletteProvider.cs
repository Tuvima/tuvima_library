using MediaEngine.Domain.Models;

namespace MediaEngine.Web.Services.Theming;

/// <summary>
/// Static accessor for the colour palette, initialized at app startup.
/// Allows static helper classes (LibraryHelpers, LibraryItemHelpers) to read
/// palette colours without DI injection.
/// </summary>
public static class PaletteProvider
{
    private static PaletteConfiguration _palette = new();

    /// <summary>The current palette. Returns defaults if not initialized.</summary>
    public static PaletteConfiguration Current => _palette;

    /// <summary>Called once at startup to set the loaded palette.</summary>
    public static void Initialize(PaletteConfiguration palette) =>
        _palette = palette ?? new();
}
