namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>Reader settings stored in localStorage (per-device).</summary>
public sealed record ReaderSettingsDto
{
    public string FontFamily { get; set; } = "Merriweather";
    public int FontSize { get; set; } = 18;
    public double LineHeight { get; set; } = 1.8;
    public int Margins { get; set; } = 48;
}
