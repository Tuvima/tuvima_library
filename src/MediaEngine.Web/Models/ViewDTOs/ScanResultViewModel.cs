namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>Result of a POST /ingestion/scan dry run.</summary>
public sealed class ScanResultViewModel
{
    public List<PendingOperationViewModel> Operations { get; init; } = [];
    public int TotalCount => Operations.Count;
}

public sealed class PendingOperationViewModel
{
    public string  SourcePath      { get; init; } = string.Empty;
    public string  DestinationPath { get; init; } = string.Empty;
    public string  OperationKind   { get; init; } = string.Empty;
    public string? Reason          { get; init; }
}
