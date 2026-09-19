using MediaEngine.Contracts.Universe;

namespace MediaEngine.Web.Services.Integration;

public partial interface IEngineApiClient
{
    Task<SharedEntityEditorContextDto?> GetSharedEntityEditorContextAsync(SharedEntityEditorTargetDto target, CancellationToken ct = default);
    Task<IReadOnlyList<SharedEntityCategorySummaryDto>> GetSharedEntityCategoriesAsync(string universeQid, CancellationToken ct = default);
    Task<SharedEntitySelectorPageDto?> GetSharedEntitySelectorPageAsync(string universeQid, string? category, string? search, int offset, int limit, CancellationToken ct = default);
    Task<SharedEntityDetailsDto?> GetSharedEntityDetailsAsync(SharedEntityEditorTargetDto target, CancellationToken ct = default);
    Task<SharedEntityDetailsDto?> UpdateSharedEntityDetailsAsync(SharedEntityEditorTargetDto target, SharedEntityDetailsUpdateRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SharedEntityArtworkDto>> GetSharedEntityArtworkAsync(SharedEntityEditorTargetDto target, CancellationToken ct = default);
    Task<IReadOnlyList<SharedEntityArtworkDto>?> UpdateSharedEntityArtworkAsync(SharedEntityEditorTargetDto target, SharedEntityArtworkUpdateRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SharedEntityArtworkDto>?> UploadSharedEntityArtworkAsync(SharedEntityEditorTargetDto target, string assetType, Stream stream, string fileName, string contentType, CancellationToken ct = default);
    Task<IReadOnlyList<SharedEntityAppearanceDto>> GetSharedEntityAppearancesAsync(SharedEntityEditorTargetDto target, CancellationToken ct = default);
    Task<IReadOnlyList<SharedEntityRelationshipDto>> GetSharedEntityRelationshipsAsync(SharedEntityEditorTargetDto target, CancellationToken ct = default);
    Task<IReadOnlyList<SharedEntityTimelineEntryDto>> GetSharedEntityTimelineAsync(SharedEntityEditorTargetDto target, CancellationToken ct = default);
    Task<IReadOnlyList<SharedEntitySourceDto>> GetSharedEntitySourcesAsync(SharedEntityEditorTargetDto target, CancellationToken ct = default);
    Task<IReadOnlyList<SharedEntityHistoryEntryDto>> GetSharedEntityHistoryAsync(SharedEntityEditorTargetDto target, CancellationToken ct = default);
    Task<SharedEntityEnrichmentStatusDto?> GetSharedEntityEnrichmentAsync(SharedEntityEditorTargetDto target, CancellationToken ct = default);
    Task<SharedEntityRefreshDto?> RefreshSharedEntityAsync(SharedEntityEditorTargetDto target, CancellationToken ct = default);
}
