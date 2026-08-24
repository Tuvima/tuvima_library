namespace MediaEngine.Api.Services.View;

/// <summary>
/// Persistence lookup for resource ownership. It returns one opaque descriptor,
/// never an enumerable list of inaccessible resources. For assets reached via
/// an explicitly shared Gallery, implementations populate SharedWithProfileIds
/// only for the profiles granted that Gallery.
/// </summary>
public interface IViewResourceStore
{
    Task<ViewResourceDescriptor?> FindAsync(
        ViewResourceKind kind,
        Guid resourceId,
        CancellationToken ct = default);
}

