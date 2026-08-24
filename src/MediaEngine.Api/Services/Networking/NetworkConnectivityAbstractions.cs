namespace MediaEngine.Api.Services.Networking;

public enum NetworkCapabilityState
{
    Unavailable,
    Available,
    Active,
    Failed,
}

public sealed record RouterMappingRequest(
    string Description,
    string InternalAddress,
    int InternalPort,
    int ExternalPort,
    TimeSpan LeaseDuration);

public sealed record RouterMappingResult(
    NetworkCapabilityState State,
    string Method,
    string Message,
    int? ExternalPort = null,
    DateTimeOffset? ExpiresAt = null,
    string? PublicAddress = null);

public interface IRouterPortMapper
{
    string Method { get; }
    int Priority { get; }
    Task<RouterMappingResult> TryCreateAsync(RouterMappingRequest request, CancellationToken ct);
    Task<RouterMappingResult> TryRenewAsync(RouterMappingRequest request, CancellationToken ct);
    Task RemoveOwnedAsync(RouterMappingRequest request, CancellationToken ct);
}

public enum RemoteProviderState
{
    Unconfigured,
    Connecting,
    Connected,
    Degraded,
    Error,
}

public sealed record RemoteProviderSnapshot(
    string Key,
    string DisplayName,
    RemoteProviderState State,
    string? PublicAddress,
    string Message);

public interface IRemoteConnectivityProvider
{
    string Key { get; }
    string DisplayName { get; }
    Task<RemoteProviderSnapshot> GetStateAsync(CancellationToken ct);
    Task<RemoteProviderSnapshot> TestAsync(CancellationToken ct);
}
