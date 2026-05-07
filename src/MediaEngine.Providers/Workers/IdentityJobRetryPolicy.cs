using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Providers.Workers;

internal static class IdentityJobRetryPolicy
{
    public const int MaxAttempts = 5;

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
        CancellationToken ct)
    {
        var nextAttempt = job.AttemptCount + 1;
        if (IsTerminalDataFailure(exception) || nextAttempt >= MaxAttempts)
        {
            await repository.MarkDeadLetteredAsync(job.Id, exception.Message, ct).ConfigureAwait(false);
            return;
        }

        var delay = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, nextAttempt) * 10))
                    + TimeSpan.FromMilliseconds(Random.Shared.Next(250, 1750));
        await repository.ScheduleRetryAsync(
                job.Id,
                retryState,
                DateTimeOffset.UtcNow.Add(delay),
                exception.Message,
                ct)
            .ConfigureAwait(false);
    }
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
