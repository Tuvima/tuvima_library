namespace MediaEngine.Intelligence.Contracts;

/// <summary>
/// Resolves Parent Hub membership for a given Hub by examining shared
/// franchise/universe relationships with other Hubs.
/// </summary>
public interface IParentHubResolver
{
    /// <summary>
    /// Examines the given Hub's relationships and, if it shares a franchise or
    /// fictional universe QID with other Hubs, creates or finds a Parent Hub
    /// and assigns this Hub (and its siblings) as children.
    ///
    /// Safe to call repeatedly — idempotent. If the Hub already has a parent,
    /// or no franchise relationship exists, this is a no-op.
    /// </summary>
    Task ResolveParentHubAsync(Guid hubId, CancellationToken ct = default);
}
