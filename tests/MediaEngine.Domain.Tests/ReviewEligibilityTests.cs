using MediaEngine.Domain.Capabilities;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Tests;

public sealed class ReviewEligibilityTests
{
    [Theory]
    [InlineData(MediaOperationStatus.Pending)]
    [InlineData(MediaOperationStatus.Queued)]
    [InlineData(MediaOperationStatus.Leased)]
    [InlineData(MediaOperationStatus.Running)]
    [InlineData(MediaOperationStatus.RetryWaiting)]
    [InlineData(MediaOperationStatus.FailedRetryable)]
    [InlineData(MediaOperationStatus.Succeeded)]
    [InlineData(MediaOperationStatus.Skipped)]
    [InlineData(MediaOperationStatus.NotApplicable)]
    public void Operation_InProgressOrNonActionableStatus_IsNotReviewEligible(string status)
    {
        var operation = new MediaOperation
        {
            OperationType = MediaOperationType.IdentityWikidataBridge,
            OperationKind = MediaOperationKind.Identity,
            Status = status,
            IdempotencyKey = $"test:{status}"
        };

        Assert.False(ReviewEligibility.IsReviewEligible(operation));
    }

    [Theory]
    [InlineData(MediaOperationStatus.MissingConfirmed)]
    [InlineData(MediaOperationStatus.Blocked)]
    [InlineData(MediaOperationStatus.FailedTerminal)]
    [InlineData(MediaOperationStatus.DeadLettered)]
    public void Operation_ActionableTerminalStatus_IsReviewEligible(string status)
    {
        var operation = new MediaOperation
        {
            OperationType = MediaOperationType.IdentityWikidataBridge,
            OperationKind = MediaOperationKind.Identity,
            Status = status,
            IdempotencyKey = $"test:{status}"
        };

        Assert.True(ReviewEligibility.IsReviewEligible(operation));
    }

    [Theory]
    [InlineData(EntityCapabilityStatus.Pending)]
    [InlineData(EntityCapabilityStatus.Queued)]
    [InlineData(EntityCapabilityStatus.Running)]
    [InlineData(EntityCapabilityStatus.FailedRetryable)]
    [InlineData(EntityCapabilityStatus.Stale)]
    [InlineData(EntityCapabilityStatus.Succeeded)]
    [InlineData(EntityCapabilityStatus.Skipped)]
    [InlineData(EntityCapabilityStatus.NotApplicable)]
    public void Capability_InProgressOrNonActionableStatus_IsNotReviewEligible(string status)
    {
        var state = NewState(status, CapabilityRequiredness.Required);
        var definition = NewDefinition(createNoResultReview: true);

        Assert.False(ReviewEligibility.IsReviewEligible(state, definition));
    }

    [Fact]
    public void OptionalCapability_NoResult_DoesNotCreateReviewByDefault()
    {
        var state = NewState(EntityCapabilityStatus.NoResult, CapabilityRequiredness.Optional);
        var definition = NewDefinition(createNoResultReview: false);

        Assert.False(ReviewEligibility.IsReviewEligible(state, definition));
    }

    [Fact]
    public void RequiredCapability_NoResult_CreatesReviewWhenPolicyAllows()
    {
        var state = NewState(EntityCapabilityStatus.NoResult, CapabilityRequiredness.Required);
        var definition = NewDefinition(createNoResultReview: true);

        Assert.True(ReviewEligibility.IsReviewEligible(state, definition));
    }

    [Fact]
    public void Capability_TerminalFailure_CreatesReviewWhenPolicyAllows()
    {
        var state = NewState(EntityCapabilityStatus.FailedTerminal, CapabilityRequiredness.Optional);
        var definition = NewDefinition(createNoResultReview: false);

        Assert.True(ReviewEligibility.IsReviewEligible(state, definition));
    }

    private static EntityCapabilityState NewState(string status, string requiredness) => new()
    {
        EntityId = Guid.NewGuid(),
        EntityKind = "asset",
        CapabilityId = CapabilityId.IdentityMediaTypeClassification,
        CapabilityKind = MediaOperationKind.Identity,
        Status = status,
        Requiredness = requiredness
    };

    private static CapabilityDefinition NewDefinition(bool createNoResultReview) => new(
        CapabilityId.IdentityMediaTypeClassification,
        MediaOperationKind.Identity,
        "1.0",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "asset" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        CapabilityRequiredness.Required,
        "auto",
        RerunOnVersionChange: true,
        new ReviewPolicy(
            CreateReviewOnNoResult: createNoResultReview,
            CreateReviewOnMissingConfirmed: true,
            CreateReviewOnBlocked: true,
            CreateReviewOnTerminalFailure: true,
            ReviewBelowConfidence: null));
}
