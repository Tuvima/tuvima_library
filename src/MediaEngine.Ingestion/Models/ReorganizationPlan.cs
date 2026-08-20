using System.Security.Cryptography;
using System.Text;

namespace MediaEngine.Ingestion.Models;

public sealed record ReorganizationCandidate
{
    public required string SourceId { get; init; }
    public required string DestinationSourceId { get; init; }
    public required string CurrentPath { get; init; }
    public string? ProposedPath { get; init; }
    public long SizeBytes { get; init; }
    public string? UnresolvedReason { get; init; }
    public string? Error { get; init; }
}

public sealed record ReorganizationPlanningRequest
{
    public Guid PlanId { get; init; } = Guid.NewGuid();
    public required string LibraryId { get; init; }
    public required IReadOnlyList<FileSourceMutationPolicy> Sources { get; init; }
    public required IReadOnlyList<ReorganizationCandidate> Candidates { get; init; }
    public IReadOnlySet<string> ExistingPaths { get; init; } = new HashSet<string>(PathSafety.Comparer);
    public IReadOnlyDictionary<string, long> AvailableBytesByDestinationSource { get; init; }
        = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum ReorganizationDisposition
{
    Unchanged,
    Renamed,
    Moved,
    Conflict,
    Unresolved,
    Blocked,
    Error,
}

public sealed record ReorganizationPlanOperation
{
    public required int Sequence { get; init; }
    public required string SourceId { get; init; }
    public required string DestinationSourceId { get; init; }
    public required string CurrentPath { get; init; }
    public string? ProposedPath { get; init; }
    public required ReorganizationDisposition Disposition { get; init; }
    public long SizeBytes { get; init; }
    public string? Reason { get; init; }

    public bool IsExecutable => Disposition is ReorganizationDisposition.Renamed or ReorganizationDisposition.Moved;
}

public sealed record ReorganizationPlanSummary
{
    public int Total { get; init; }
    public int Unchanged { get; init; }
    public int Renamed { get; init; }
    public int Moved { get; init; }
    public int Conflicts { get; init; }
    public int Unresolved { get; init; }
    public int Blocked { get; init; }
    public int Errors { get; init; }
}

public enum ReorganizationPlanStatus
{
    Draft,
    Confirmed,
}

/// <summary>
/// Serializable plan aggregate. The fingerprint binds confirmation to the
/// exact ordered operation set so a stale UI cannot confirm a changed preview.
/// </summary>
public sealed record ReorganizationPlan
{
    public required Guid Id { get; init; }
    public required string LibraryId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required IReadOnlyList<ReorganizationPlanOperation> Operations { get; init; }
    public required ReorganizationPlanSummary Summary { get; init; }
    public required string Fingerprint { get; init; }
    public ReorganizationPlanStatus Status { get; init; } = ReorganizationPlanStatus.Draft;
    public DateTimeOffset? ConfirmedAt { get; init; }

    public bool IsNoOp => Summary.Renamed == 0 && Summary.Moved == 0;

    public bool CanConfirm => !IsNoOp
        && Summary.Conflicts == 0
        && Summary.Unresolved == 0
        && Summary.Blocked == 0
        && Summary.Errors == 0;

    public bool CanExecute => Status == ReorganizationPlanStatus.Confirmed && CanConfirm;

    /// <summary>
    /// Returns the immutable work list only after confirmation has bound the
    /// caller to this plan fingerprint. Future executors should use this method
    /// instead of reading <see cref="Operations"/> directly.
    /// </summary>
    public IReadOnlyList<ReorganizationPlanOperation> GetConfirmedOperations()
    {
        if (!CanExecute)
            throw new InvalidOperationException("Reorganization operations cannot execute before the plan is confirmed.");

        return Operations.Where(static operation => operation.IsExecutable).ToList();
    }

    public ReorganizationPlan Confirm(string expectedFingerprint, DateTimeOffset confirmedAt)
    {
        if (Status != ReorganizationPlanStatus.Draft)
            throw new InvalidOperationException("Only a draft reorganization plan can be confirmed.");

        if (!CanConfirm)
            throw new InvalidOperationException("The reorganization plan contains no executable changes or has unresolved safety issues.");

        if (!string.Equals(Fingerprint, expectedFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The reorganization plan changed after it was previewed.");

        return this with
        {
            Status = ReorganizationPlanStatus.Confirmed,
            ConfirmedAt = confirmedAt,
        };
    }

    internal static string CalculateFingerprint(
        Guid id,
        string libraryId,
        IEnumerable<ReorganizationPlanOperation> operations)
    {
        var payload = new StringBuilder()
            .Append(id.ToString("N"))
            .Append('|')
            .Append(libraryId);

        foreach (var operation in operations.OrderBy(static item => item.Sequence))
        {
            payload.Append('\n')
                .Append(operation.Sequence).Append('|')
                .Append(operation.SourceId).Append('|')
                .Append(operation.DestinationSourceId).Append('|')
                .Append(operation.CurrentPath).Append('|')
                .Append(operation.ProposedPath).Append('|')
                .Append(operation.Disposition).Append('|')
                .Append(operation.SizeBytes).Append('|')
                .Append(operation.Reason);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())));
    }
}
