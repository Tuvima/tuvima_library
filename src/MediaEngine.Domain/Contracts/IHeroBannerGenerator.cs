using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Generates cinematic hero banner images from cover art.
/// </summary>
public interface IHeroBannerGenerator
{
    /// <summary>
    /// Generates a hero banner from the cover image at <paramref name="coverImagePath"/>,
    /// saving the result as <c>hero.jpg</c> in <paramref name="outputDirectory"/>.
    /// Skips regeneration if the cached hero is newer than the cover.
    /// </summary>
    Task<HeroBannerResult> GenerateAsync(
        string coverImagePath,
        string outputDirectory,
        CancellationToken ct = default);
}
