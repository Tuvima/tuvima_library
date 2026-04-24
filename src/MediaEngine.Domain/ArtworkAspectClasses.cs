namespace MediaEngine.Domain;

/// <summary>
/// Normalized artwork aspect buckets used by ingest, APIs, and UI surface selection.
/// </summary>
public static class ArtworkAspectClasses
{
    public const string Portrait = "Portrait";
    public const string Square = "Square";
    public const string LandscapeWide = "LandscapeWide";
    public const string BannerStrip = "BannerStrip";
    public const string UnsupportedRect = "UnsupportedRect";
}
