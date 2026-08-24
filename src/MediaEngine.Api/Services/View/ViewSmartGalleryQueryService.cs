using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.PersonalMedia;

namespace MediaEngine.Api.Services.View;

public sealed class ViewSmartGalleryQueryService(IViewGalleryRepository galleries)
    : IViewSmartGalleryQueryService
{
    public async Task EnsureQuerySupportedAsync(Guid galleryId, CancellationToken ct = default)
    {
        var gallery = await galleries.GetAsync(galleryId, ct).ConfigureAwait(false);
        if (gallery?.Kind == ViewGalleryKind.Smart)
        {
            throw new InvalidOperationException(
                "Smart Gallery rule evaluation is not available until the shared View rule evaluator is configured.");
        }
    }
}
