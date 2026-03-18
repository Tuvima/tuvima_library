using Cronos;

namespace MediaEngine.Api.Services;

/// <summary>
/// Shared helper that calculates the next occurrence of a cron expression
/// and returns a delay suitable for <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
/// All cron expressions are evaluated in the machine's local timezone.
/// </summary>
public static class CronScheduler
{
    /// <summary>
    /// Calculates the <see cref="TimeSpan"/> until the next occurrence of the given
    /// cron expression.  Returns <paramref name="fallback"/> when the expression is
    /// invalid or the next occurrence cannot be determined.
    /// </summary>
    public static TimeSpan UntilNext(string cronExpression, TimeSpan fallback)
    {
        try
        {
            var cron = CronExpression.Parse(cronExpression);
            var next = cron.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local);
            if (next.HasValue)
            {
                var delay = next.Value - DateTimeOffset.Now;
                return delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1);
            }
        }
        catch
        {
            // Invalid expression — fall back to fixed interval.
        }

        return fallback;
    }
}
