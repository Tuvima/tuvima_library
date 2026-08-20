namespace MediaEngine.Ingestion.Models;

public enum ReorganizationExecutionDisposition
{
    Moved,
    Blocked,
    Failed,
}

public sealed record ReorganizationExecutionItemResult
{
    public required int Sequence { get; init; }
    public required string CurrentPath { get; init; }
    public required string ProposedPath { get; init; }
    public required ReorganizationExecutionDisposition Disposition { get; init; }
    public string? Reason { get; init; }
}

public sealed record ReorganizationExecutionResult
{
    public required Guid PlanId { get; init; }
    public required string LibraryId { get; init; }
    public required string Fingerprint { get; init; }
    public required IReadOnlyList<ReorganizationExecutionItemResult> Items { get; init; }
    public int Succeeded => Items.Count(item => item.Disposition == ReorganizationExecutionDisposition.Moved);
    public int Blocked => Items.Count(item => item.Disposition == ReorganizationExecutionDisposition.Blocked);
    public int Failed => Items.Count(item => item.Disposition == ReorganizationExecutionDisposition.Failed);
}
