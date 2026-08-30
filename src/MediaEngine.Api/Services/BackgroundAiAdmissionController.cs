using MediaEngine.AI.Configuration;
using MediaEngine.AI.Infrastructure;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage;

namespace MediaEngine.Api.Services;

/// <summary>
/// Makes local AI explicitly opportunistic. Background inference may start only
/// after onboarding, during an interactive quiet period, and when CPU, memory,
/// and transcoding pressure permit it. A newly arriving interactive request
/// cancels the active background lease so the item can be retried later.
/// </summary>
public sealed class BackgroundAiAdmissionController : IDisposable
{
    private readonly object _sync = new();
    private readonly AiSettings _settings;
    private readonly OnboardingActivationGate _onboarding;
    private readonly InteractiveRequestTracker _interactiveRequests;
    private readonly ResourceMonitorService _resources;
    private readonly ILogger<BackgroundAiAdmissionController> _logger;
    private CancellationTokenSource? _activeBackgroundWork;
    private int _disposed;

    public BackgroundAiAdmissionController(
        AiSettings settings,
        OnboardingActivationGate onboarding,
        InteractiveRequestTracker interactiveRequests,
        ResourceMonitorService resources,
        ILogger<BackgroundAiAdmissionController> logger)
    {
        _settings = settings;
        _onboarding = onboarding;
        _interactiveRequests = interactiveRequests;
        _resources = resources;
        _logger = logger;
        _interactiveRequests.RequestStarted += OnInteractiveRequestStarted;
    }

    public BackgroundAiLease? TryAcquire(AiModelRole role, CancellationToken stoppingToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (!_onboarding.IsComplete)
            return Defer(role, "onboarding-incomplete");

        var quietPeriod = TimeSpan.FromSeconds(_settings.BackgroundQuietSeconds);
        if (_interactiveRequests.HasPressure(quietPeriod))
            return Defer(role, "interactive-traffic");

        var modelSize = _settings.Models.GetByRole(role).SizeMB;
        var recommendation = _resources.CanLoadModel(modelSize);
        if (!recommendation.CanLoad)
            return Defer(role, recommendation.Reason);

        lock (_sync)
        {
            if (_activeBackgroundWork is not null)
                return Defer(role, "another-background-inference-is-active");

            _activeBackgroundWork = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            PerformanceMetrics.BackgroundAiAdmissions.Add(1, new KeyValuePair<string, object?>("role", role.ToString()));
            return new BackgroundAiLease(this, role, _activeBackgroundWork);
        }
    }

    private BackgroundAiLease? Defer(AiModelRole role, string reason)
    {
        PerformanceMetrics.BackgroundAiDeferrals.Add(
            1,
            new KeyValuePair<string, object?>("role", role.ToString()),
            new KeyValuePair<string, object?>("reason", reason));
        _logger.LogDebug("Background AI {Role} deferred: {Reason}", role, reason);
        return null;
    }

    private void OnInteractiveRequestStarted()
    {
        lock (_sync)
        {
            if (_activeBackgroundWork is null || _activeBackgroundWork.IsCancellationRequested)
                return;

            PerformanceMetrics.BackgroundAiPreemptions.Add(1);
            _logger.LogInformation("Cancelling background AI inference because interactive work arrived");
            _activeBackgroundWork.Cancel();
        }
    }

    private void Release(CancellationTokenSource source)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_activeBackgroundWork, source))
                return;

            _activeBackgroundWork = null;
        }

        source.Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _interactiveRequests.RequestStarted -= OnInteractiveRequestStarted;
        lock (_sync)
        {
            _activeBackgroundWork?.Cancel();
            _activeBackgroundWork?.Dispose();
            _activeBackgroundWork = null;
        }
    }

    public sealed class BackgroundAiLease(
        BackgroundAiAdmissionController owner,
        AiModelRole role,
        CancellationTokenSource source) : IDisposable
    {
        private BackgroundAiAdmissionController? _owner = owner;

        public AiModelRole Role { get; } = role;
        public CancellationToken Token => source.Token;
        public bool WasPreempted => source.IsCancellationRequested;

        public void Dispose() =>
            Interlocked.Exchange(ref _owner, null)?.Release(source);
    }
}
