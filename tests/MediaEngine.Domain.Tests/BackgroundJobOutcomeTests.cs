using System.Net;
using MediaEngine.Domain.Jobs;

namespace MediaEngine.Domain.Tests;

public sealed class BackgroundJobOutcomeTests
{
    [Fact]
    public void Classifier_CoversEveryLaunchOutcomeCategory()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Equal(
            BackgroundJobOutcomeCategory.ContentFailure,
            BackgroundJobOutcomeClassifier.Classify(new InvalidDataException("bad media")));
        Assert.Equal(
            BackgroundJobOutcomeCategory.TransientDependencyFailure,
            BackgroundJobOutcomeClassifier.Classify(new HttpRequestException("offline")));
        Assert.Equal(
            BackgroundJobOutcomeCategory.TransientDependencyFailure,
            BackgroundJobOutcomeClassifier.Classify(
                new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests)));
        Assert.Equal(
            BackgroundJobOutcomeCategory.UnavailableCapability,
            BackgroundJobOutcomeClassifier.Classify(new UnavailableCapabilityException("model disabled")));
        Assert.Equal(
            BackgroundJobOutcomeCategory.Cancellation,
            BackgroundJobOutcomeClassifier.Classify(new OperationCanceledException(), cancelled.Token));
        Assert.Equal(
            BackgroundJobOutcomeCategory.PermanentFailure,
            BackgroundJobOutcomeClassifier.Classify(new NotSupportedException("unsupported")));
    }

    [Theory]
    [InlineData(BackgroundJobOutcomeCategory.ContentFailure, true)]
    [InlineData(BackgroundJobOutcomeCategory.TransientDependencyFailure, false)]
    [InlineData(BackgroundJobOutcomeCategory.UnavailableCapability, false)]
    [InlineData(BackgroundJobOutcomeCategory.Cancellation, false)]
    [InlineData(BackgroundJobOutcomeCategory.PermanentFailure, false)]
    public void OnlyContentFailuresConsumePoisonAttempts(
        BackgroundJobOutcomeCategory category,
        bool consumesPoisonBudget)
    {
        Assert.Equal(consumesPoisonBudget, BackgroundJobOutcomePolicy.For(category).ConsumesPoisonBudget);
    }
}
