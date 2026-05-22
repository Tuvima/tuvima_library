using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

public interface IArtworkPaletteService
{
    Task<ArtworkPalette> GeneratePaletteAsync(
        IReadOnlyList<ArtworkPaletteSource> sources,
        ArtworkPaletteOptions? options = null,
        CancellationToken cancellationToken = default);
}
