namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Reconciles duplicate work identities discovered during enrichment.
/// </summary>
public interface IWorkIdentityReconciliationService
{
    Task<int> MergeDuplicateReadWorksByQidAsync(CancellationToken ct = default);

    /// <summary>
    /// Copies the canonical, QID-backed author identities from owned books to
    /// owned audiobook variants of the same creative work.
    /// </summary>
    Task<int> AlignAudiobookAuthorsWithBooksByQidAsync(CancellationToken ct = default);
}
