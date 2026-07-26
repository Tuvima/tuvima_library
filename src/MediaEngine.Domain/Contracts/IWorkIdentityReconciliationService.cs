namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Reconciles duplicate work identities discovered during enrichment.
/// </summary>
public interface IWorkIdentityReconciliationService
{
    Task<int> MergeDuplicateReadWorksByQidAsync(CancellationToken ct = default);
}
