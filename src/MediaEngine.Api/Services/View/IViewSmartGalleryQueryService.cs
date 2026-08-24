namespace MediaEngine.Api.Services.View;

/// <summary>
/// Extension seam for translating a versioned Smart Gallery rule into an
/// authorized asset query. The initial implementation rejects smart queries
/// explicitly until the shared rule evaluator is integrated.
/// </summary>
public interface IViewSmartGalleryQueryService
{
    Task EnsureQuerySupportedAsync(Guid galleryId, CancellationToken ct = default);
}

