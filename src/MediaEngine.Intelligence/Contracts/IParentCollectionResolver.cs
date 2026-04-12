namespace MediaEngine.Intelligence.Contracts;

/// <summary>
/// Resolves Parent Collection membership for a given Collection by examining shared
/// franchise/universe relationships with other Collections.
/// </summary>
public interface IParentCollectionResolver
{
    /// <summary>
    /// Examines the given Collection's relationships and, if it shares a franchise or
    /// fictional universe QID with other Collections, creates or finds a Parent Collection
    /// and assigns this Collection (and its siblings) as children.
    ///
    /// Safe to call repeatedly — idempotent. If the Collection already has a parent,
    /// or no franchise relationship exists, this is a no-op.
    /// </summary>
    Task ResolveParentCollectionAsync(Guid collectionId, CancellationToken ct = default);
}
