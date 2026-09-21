using MediaEngine.Contracts.Artwork;

namespace MediaEngine.Web.Services.Integration;

public partial interface IEngineApiClient
{
    Task<ArtworkLibraryPageDto?> GetArtworkLibraryAsync(
        string? entityKind = null,
        string? artworkType = null,
        string? search = null,
        int offset = 0,
        int limit = 48,
        CancellationToken ct = default);
}
