using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Models;

namespace MediaEngine.Providers.Workers;

internal static class IdentityJobRetryPolicy
{
    public const int MaxAttempts = 5;
    private const int DefaultBaseDelaySeconds = 10;
    private const int DefaultMaxDelaySeconds = 300;
    private const int DefaultJitterMinMilliseconds = 250;
    private const int DefaultJitterMaxMilliseconds = 1750;

    public static bool IsTransient(Exception ex) =>
        ex is TimeoutException
        || ex is HttpRequestException
        || ex is TaskCanceledException
        || (ex.GetType().Name == "SqliteException" && IsBusySqliteError(ex));

    public static async Task ScheduleRetryOrDeadLetterAsync(
        IIdentityJobRepository repository,
        IdentityJob job,
        IdentityJobState retryState,
        Exception exception,
        HydrationSettings? settings,
        CancellationToken ct)
    {
        settings ??= new HydrationSettings();
        var nextAttempt = job.AttemptCount + 1;
        var maxAttempts = settings.IdentityRetryMaxAttempts > 0
            ? settings.IdentityRetryMaxAttempts
            : MaxAttempts;

        if (IsTerminalDataFailure(exception) || nextAttempt >= maxAttempts)
        {
            await repository.MarkDeadLetteredAsync(job.Id, exception.Message, ct).ConfigureAwait(false);
            return;
        }

        var baseDelaySeconds = settings.IdentityRetryBaseDelaySeconds > 0
            ? settings.IdentityRetryBaseDelaySeconds
            : DefaultBaseDelaySeconds;
        var maxDelaySeconds = settings.IdentityRetryMaxDelaySeconds > 0
            ? settings.IdentityRetryMaxDelaySeconds
            : DefaultMaxDelaySeconds;
        var jitterMin = settings.IdentityRetryJitterMinMilliseconds >= 0
            ? settings.IdentityRetryJitterMinMilliseconds
            : DefaultJitterMinMilliseconds;
        var jitterMax = settings.IdentityRetryJitterMaxMilliseconds > jitterMin
            ? settings.IdentityRetryJitterMaxMilliseconds
            : DefaultJitterMaxMilliseconds;
        if (jitterMax <= jitterMin)
            jitterMax = jitterMin + 1;

        var delay = TimeSpan.FromSeconds(Math.Min(maxDelaySeconds, Math.Pow(2, nextAttempt) * baseDelaySeconds))
                    + TimeSpan.FromMilliseconds(Random.Shared.Next(jitterMin, jitterMax));
        await repository.ScheduleRetryAsync(
                job.Id,
                retryState,
                DateTimeOffset.UtcNow.Add(delay),
                exception.Message,
                ct)
            .ConfigureAwait(false);
    }

    public static Task ScheduleRetryOrDeadLetterAsync(
        IIdentityJobRepository repository,
        IdentityJob job,
        IdentityJobState retryState,
        Exception exception,
        CancellationToken ct) =>
        ScheduleRetryOrDeadLetterAsync(repository, job, retryState, exception, null, ct);

    private static bool IsBusySqliteError(Exception ex)
    {
        var code = ex.GetType().GetProperty("SqliteErrorCode")?.GetValue(ex) as int?;
        return code is 5 or 6;
    }

    private static bool IsTerminalDataFailure(Exception ex) =>
        ex is ArgumentException
        || ex is InvalidDataException
        || ex is FormatException;
}
