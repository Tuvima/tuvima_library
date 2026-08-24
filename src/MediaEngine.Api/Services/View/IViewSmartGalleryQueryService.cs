using MediaEngine.Domain.Models;

namespace MediaEngine.Api.Services.View;

/// <summary>
/// Resolves the already-authorized Gallery into a validated dynamic rule.
/// Manual Galleries return null and continue to use stored membership rows.
/// </summary>
public interface IViewSmartGalleryQueryService
{
    Task<CollectionRuleDefinition?> ResolveRuleAsync(Guid galleryId, CancellationToken ct = default);
}

