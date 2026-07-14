using MediaEngine.AI.Features;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.AI.Tests;

public sealed class WhyExplainerTests
{
    [Fact]
    public async Task ExplainAsync_UsesDeterministicGuidanceWhenTasteDataIsInsufficient()
    {
        var userId = Guid.NewGuid();
        var explainer = new WhyExplainer(
            StubLlamaInferenceService.ReturningJson("AI should not be called"),
            new ThrowingCanonicalRepository(),
            new InsufficientTasteProfiler(),
            NullLogger<WhyExplainer>.Instance);

        var result = await explainer.ExplainAsync(userId, Guid.NewGuid());

        Assert.Equal(
            "Keep reading, watching, or listening to improve personalized explanations for this profile.",
            result);
    }

    private sealed class InsufficientTasteProfiler : ITasteProfiler
    {
        public Task<TasteProfileBuildResult> GetProfileAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(new TasteProfileBuildResult(
                TasteProfileBuildStatus.InsufficientData,
                userId,
                Profile: null,
                SignalCount: 0,
                InputFingerprint: "empty",
                Reason: "No profile interactions."));
    }

    private sealed class ThrowingCanonicalRepository : ICanonicalValueRepository
    {
        public Task UpsertBatchAsync(IReadOnlyList<CanonicalValue> values, CancellationToken ct = default) =>
            throw new InvalidOperationException("Canonical values should not be loaded for insufficient taste data.");

        public Task<IReadOnlyList<CanonicalValue>> GetByEntityAsync(Guid entityId, CancellationToken ct = default) =>
            throw new InvalidOperationException("Canonical values should not be loaded for insufficient taste data.");

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<CanonicalValue>>> GetByEntitiesAsync(IReadOnlyList<Guid> entityIds, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CanonicalValue>> GetConflictedAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteByKeyAsync(Guid entityId, string key, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> FindByValueAsync(string key, string value, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CanonicalValue>> FindByKeyAndPrefixAsync(string key, string prefix, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> GetEntitiesNeedingEnrichmentAsync(string hasField, string missingField, int limit, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
