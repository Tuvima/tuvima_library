using MudBlazor;

namespace MediaEngine.Web.Components.Shared;

internal static class AppUiMaps
{
    public static Size ToMudSize(AppControlSize size) => size switch
    {
        AppControlSize.Compact => Size.Small,
        AppControlSize.Large => Size.Large,
        _ => Size.Medium,
    };

    public static Size ToMudSize(object? size) => size switch
    {
        Size mudSize => mudSize,
        AppControlSize appSize => ToMudSize(appSize),
        _ => Size.Medium,
    };

    public static AppControlSize ToAppControlSize(object? size) => size switch
    {
        AppControlSize appSize => appSize,
        Size.Small => AppControlSize.Compact,
        Size.Large => AppControlSize.Large,
        _ => AppControlSize.Normal,
    };

    public static Color ToMudColor(AppUiTone tone) => tone switch
    {
        AppUiTone.Primary => Color.Primary,
        AppUiTone.Info => Color.Info,
        AppUiTone.Success => Color.Success,
        AppUiTone.Warning => Color.Warning,
        AppUiTone.Error => Color.Error,
        _ => Color.Default,
    };

    public static Variant ToMudVariant(AppButtonStyle style) => style switch
    {
        AppButtonStyle.Filled => Variant.Filled,
        AppButtonStyle.Text or AppButtonStyle.Ghost => Variant.Text,
        _ => Variant.Outlined,
    };

    public static string SizeClass(AppControlSize size) =>
        $"app-control--{size.ToString().ToLowerInvariant()}";

    public static string ToneClass(AppUiTone tone) =>
        $"app-tone--{tone.ToString().ToLowerInvariant()}";
}
