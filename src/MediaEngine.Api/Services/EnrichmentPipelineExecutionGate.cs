namespace MediaEngine.Api.Services;

/// <summary>
/// Coordinates destructive development resets with background enrichment workers.
/// A pause prevents new work from starting and completes only after every active
/// worker has left its database-sensitive section.
/// </summary>
public sealed class EnrichmentPipelineExecutionGate
{
    private readonly object _sync = new();
    private bool _paused;
    private int _activeWorkers;
    private CancellationTokenSource _activePeriodCancellation = new();
    private TaskCompletionSource _resumeSignal = CreateCompletedSignal();
    private TaskCompletionSource? _drainedSignal;

    public async ValueTask<ExecutionLease> EnterAsync(CancellationToken ct = default)
    {
        while (true)
        {
            Task resumeTask;
            lock (_sync)
            {
                if (!_paused)
                {
                    _activeWorkers++;
                    return new ExecutionLease(this, _activePeriodCancellation.Token);
                }

                resumeTask = _resumeSignal.Task;
            }

            await resumeTask.WaitAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task PauseAndDrainAsync(CancellationToken ct = default)
    {
        Task drainedTask;
        CancellationTokenSource? activePeriodCancellation = null;
        lock (_sync)
        {
            if (!_paused)
            {
                _paused = true;
                _resumeSignal = CreateSignal();
                activePeriodCancellation = _activePeriodCancellation;
            }

            drainedTask = _activeWorkers == 0
                ? Task.CompletedTask
                : (_drainedSignal ??= CreateSignal()).Task;
        }

        activePeriodCancellation?.Cancel();
        await drainedTask.WaitAsync(ct).ConfigureAwait(false);
    }

    public void Resume()
    {
        TaskCompletionSource? resumeSignal;
        CancellationTokenSource? completedPeriodCancellation;
        lock (_sync)
        {
            if (!_paused)
            {
                return;
            }

            _paused = false;
            _drainedSignal = null;
            resumeSignal = _resumeSignal;
            completedPeriodCancellation = _activePeriodCancellation;
            _activePeriodCancellation = new CancellationTokenSource();
        }

        completedPeriodCancellation.Dispose();
        resumeSignal.TrySetResult();
    }

    private void Exit()
    {
        TaskCompletionSource? drainedSignal = null;
        lock (_sync)
        {
            if (_activeWorkers <= 0)
            {
                throw new InvalidOperationException("An enrichment pipeline execution lease was released more than once.");
            }

            _activeWorkers--;
            if (_paused && _activeWorkers == 0)
            {
                drainedSignal = _drainedSignal;
            }
        }

        drainedSignal?.TrySetResult();
    }

    private static TaskCompletionSource CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource CreateCompletedSignal()
    {
        var signal = CreateSignal();
        signal.TrySetResult();
        return signal;
    }

    public sealed class ExecutionLease : IDisposable
    {
        private EnrichmentPipelineExecutionGate? _owner;

        internal ExecutionLease(
            EnrichmentPipelineExecutionGate owner,
            CancellationToken pauseCancellationToken)
        {
            _owner = owner;
            PauseCancellationToken = pauseCancellationToken;
        }

        public CancellationToken PauseCancellationToken { get; }

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Exit();
    }
}
