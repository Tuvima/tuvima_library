namespace MediaEngine.Contracts.Realtime;

public sealed record ModelDownloadProgressEvent(
    string Role,
    int Percent,
    long BytesDownloaded,
    long TotalBytes);

public sealed record ModelStateChangedEvent(
    string Role,
    string OldState,
    string NewState);

public sealed record AiReadyEvent(DateTimeOffset ReadyAt);

public sealed record MediaOperationChangedEvent(
    Guid Id,
    string OperationType,
    string OperationKind,
    string Status,
    string? Stage,
    int ProgressPercent,
    int ItemsTotal,
    int ItemsCompleted,
    DateTimeOffset UpdatedAt);

public sealed record ProviderStatusChangedEvent(
    string ProviderId,
    string Status,
    string Message);

public sealed record ProviderRecoveryFlushEvent(
    string ProviderId,
    int ItemCount,
    string Message);
