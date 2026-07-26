using MediaEngine.Application.ReadModels;
using MediaEngine.Contracts.Metadata;

namespace MediaEngine.Application.Services;

public interface IMediaEditorNavigationReadService
{
    Task<MediaEditorNavigatorEnvelope?> GetNavigatorAsync(
        Guid entityId,
        CancellationToken ct);
}

public interface IMediaEditorMembershipReadService
{
    Task<IReadOnlyList<MembershipSuggestionEnvelope>> GetSuggestionsAsync(
        Guid entityId,
        string field,
        string? query,
        string? source,
        Guid? parentEntityId,
        string? parentValue,
        CancellationToken ct);

    Task<MembershipPreviewEnvelope?> PreviewAsync(
        Guid entityId,
        MembershipPreviewRequest request,
        CancellationToken ct);

    Task<MembershipPreviewEnvelope?> ApplyAsync(
        Guid entityId,
        MembershipPreviewRequest request,
        CancellationToken ct);
}

public interface IMetadataClaimHistoryReadService
{
    Task<List<ClaimDto>> GetClaimHistoryAsync(Guid entityId, CancellationToken ct);
}
