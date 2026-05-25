namespace MediaEngine.Web.Components.Shared;

public enum AppMediaCardVariant
{
    Portrait,
    Square,
    Landscape,
    Wide,
    Compact,
    List,
}

public enum AppCardHoverBehavior
{
    None,
    Quiet,
    Standard,
    Media,
}

public enum AppStatusTone
{
    Neutral,
    Info,
    Success,
    Warning,
    Error,
    Primary,
}

public enum AppPageStateKind
{
    Loading,
    Empty,
    Error,
    Unavailable,
    Redirecting,
}

public enum AppSurfaceDensity
{
    Compact,
    Normal,
    Comfortable,
}

public enum AppControlSize
{
    Compact,
    Normal,
    Large,
}

public enum AppUiTone
{
    Neutral,
    Primary,
    Info,
    Success,
    Warning,
    Error,
}

public enum AppButtonStyle
{
    Filled,
    Outlined,
    Text,
    Ghost,
}

public enum AppEmphasis
{
    Low,
    Medium,
    High,
}

public enum AppHorizontalAlignment
{
    Start,
    Center,
    End,
    Stretch,
}

public enum AppSurfaceVariant
{
    Default,
    Raised,
    Selected,
    Warning,
    Danger,
}

public sealed record AppSelectOption(string Value, string Label, string? Icon = null);
