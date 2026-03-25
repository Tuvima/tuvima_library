namespace MediaEngine.Domain.Contracts;

using MediaEngine.Domain.Models;

/// <summary>
/// CRUD for the <c>pending_person_signals</c> table — stores unverified
/// person signals between inline extraction and batch Wikidata verification.
/// </summary>
public interface IPendingPersonSignalRepository
{
    Task InsertAsync(PendingPersonSignal signal, CancellationToken ct = default);
    Task InsertBatchAsync(IReadOnlyList<PendingPersonSignal> signals, CancellationToken ct = default);
    Task<IReadOnlyList<PendingPersonSignal>> GetAllPendingAsync(CancellationToken ct = default);
    Task<IReadOnlyList<(string Name, string Role)>> GetUniqueNameRolePairsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PendingPersonSignal>> GetByNameAndRoleAsync(string name, string role, CancellationToken ct = default);
    Task DeleteByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task DeleteAllAsync(CancellationToken ct = default);
    Task<int> GetCountAsync(CancellationToken ct = default);
}
