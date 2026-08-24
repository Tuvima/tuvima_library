using MediaEngine.Contracts.Settings;

namespace MediaEngine.Api.Services.Networking;

public sealed class NetworkRuntimeState
{
    private readonly object _gate = new();
    private NetworkTestResultDto? _lastLocalTest;
    private NetworkTestResultDto? _lastRemoteTest;
    private NetworkBandwidthStatusDto _bandwidth = new();
    private RouterMappingResult? _routerMapping;
    private DateTimeOffset? _routerMappingCheckedAt;

    public NetworkTestResultDto? LastLocalTest
    {
        get { lock (_gate) return _lastLocalTest; }
    }

    public NetworkTestResultDto? LastRemoteTest
    {
        get { lock (_gate) return _lastRemoteTest; }
    }

    public NetworkBandwidthStatusDto Bandwidth
    {
        get { lock (_gate) return _bandwidth; }
    }

    public RouterMappingResult? RouterMapping
    {
        get { lock (_gate) return _routerMapping; }
    }

    public DateTimeOffset? RouterMappingCheckedAt
    {
        get { lock (_gate) return _routerMappingCheckedAt; }
    }

    public void RecordLocalTest(NetworkTestResultDto result)
    {
        lock (_gate) _lastLocalTest = result;
    }

    public void RecordRemoteTest(NetworkTestResultDto result)
    {
        lock (_gate) _lastRemoteTest = result;
    }

    public void RecordBandwidth(NetworkBandwidthStatusDto result)
    {
        lock (_gate) _bandwidth = result;
    }

    public void RecordRouterMapping(RouterMappingResult? result)
    {
        lock (_gate)
        {
            _routerMapping = result;
            _routerMappingCheckedAt = DateTimeOffset.UtcNow;
        }
    }
}
