using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Jobs;

namespace MediaEngine.Providers.Workers;

internal static class IdentityJobRetryPolicy
{
    public const int MaxAttempts = 5;
    private const int DefaultBaseDelaySeconds = 10;
    private const int DefaultMaxDelaySeconds = 300;
    private const int DefaultJitterMinMilliseconds = 250;
    private const int DefaultJitterMaxMilliseconds = 1750;

    public static bool IsTransient(Exception ex) =>
        BackgroundJobOutcomeClassifier.Classify(ex) == BackgroundJobOutcomeCategory.TransientDependencyFailure;

    public static async Task ScheduleRetryOrDeadLetterAsync(
        IIdentityJobRepository repository,
        IdentityJob job,
        IdentityJobState retryState,
        Exception exception,
        HydrationSettings? settings,
        CancellationToken ct)
    {
        settings ??= new HydrationSettings();
        var category = BackgroundJobOutcomeClassifier.Classify(exception, ct);
        var policy = BackgroundJobOutcomePolicy.For(category);
        if (category == BackgroundJobOutcomeCategory.Cancellation)
        {
            await repository.ReleaseLeaseAsync(job.Id, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        var nextPoisonAttempt = job.PoisonAttemptCount + (policy.ConsumesPoisonBudget ? 1 : 0);
        var maxAttempts = settings.IdentityRetryMaxAttempts > 0
            ? settings.IdentityRetryMaxAttempts
            : MaxAttempts;

        if (policy.IsTerminal || (policy.ConsumesPoisonBudget && nextPoisonAttempt >= maxAttempts))
        {
            await repository.MarkDeadLetteredForOutcomeAsync(job.Id, exception.Message, category, ct)
                .ConfigureAwait(false);
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

        var executionAttempt = Math.Max(1, job.AttemptCount + 1);
        var delay = exception is RateLimitedDependencyException { RetryAfter: { } retryAfter }
            ? retryAfter
            : policy.IsCapabilityBlocked
                ? TimeSpan.FromMinutes(30)
                : TimeSpan.FromSeconds(Math.Min(maxDelaySeconds, Math.Pow(2, executionAttempt) * baseDelaySeconds))
                  + TimeSpan.FromMilliseconds(Random.Shared.Next(jitterMin, jitterMax));
        await repository.ScheduleRetryForOutcomeAsync(
                job.Id,
                retryState,
                DateTimeOffset.UtcNow.Add(delay),
                exception.Message,
                category,
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

}
