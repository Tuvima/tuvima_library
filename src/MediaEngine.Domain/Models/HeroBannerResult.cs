namespace MediaEngine.Domain.Models;

/// <summary>
/// Result of a hero banner generation operation.
/// </summary>
public sealed record HeroBannerResult(
    string HeroImagePath,
    string DominantHexColor,
    bool WasRegenerated);
