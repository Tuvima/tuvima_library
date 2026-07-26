namespace MediaEngine.Api.Services;

public interface IInitialSweepCommandService
{
    bool IsPendingOrRunning { get; }

    /// <summary>
    /// Schedules one sweep when no sweep is already queued or executing.
    /// </summary>
    bool TrySchedule();
}
