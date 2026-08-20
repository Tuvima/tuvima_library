using MediaEngine.Contracts.LocalAssets;
using MediaEngine.Contracts.Ingestion;

namespace MediaEngine.Web.Services.Integration;

public partial interface IEngineApiClient
{
    Task<IReadOnlyList<ViewLibrarySummaryDto>> GetViewLibrariesAsync(Guid profileId, CancellationToken ct = default);

    Task<LocalAssetPageDto?> GetViewItemsAsync(
        Guid libraryId,
        Guid profileId,
        string? search = null,
        string? kind = null,
        bool favorites = false,
        bool hidden = false,
        int offset = 0,
        int limit = 120,
        CancellationToken ct = default);

    Task<LocalAssetScanResultDto?> ScanViewLibraryAsync(Guid libraryId, Guid profileId, CancellationToken ct = default);

    Task<bool> SetViewItemFavoriteAsync(Guid libraryId, Guid itemId, Guid profileId, bool value, CancellationToken ct = default);

    Task<bool> SetViewItemHiddenAsync(Guid libraryId, Guid itemId, Guid profileId, bool value, CancellationToken ct = default);

    Task<ViewMediaUploadResult> UploadViewMediaAsync(
        Guid destinationLibraryId,
        Stream fileStream,
        string fileName,
        string? contentType = null,
        CancellationToken ct = default);
}

public sealed record ViewMediaUploadResult(
    bool Success,
    UploadMediaResponse? Upload = null,
    string? ErrorMessage = null);
