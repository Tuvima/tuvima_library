using MediaEngine.AI.Llama;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Features;

public sealed class TasteProfiler : ITasteProfiler
{
    private readonly LlamaInferenceService _llama;
    private readonly ILogger<TasteProfiler> _logger;

    public TasteProfiler(LlamaInferenceService llama, ILogger<TasteProfiler> logger)
    {
        _llama = llama;
        _logger = logger;
    }

    public Task<TasteProfile> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        // Full implementation requires querying canonical values for the user's library.
        // Will be wired when the user_taste_profiles migration (M-058) is created
        // and the TasteProfileBackgroundService provides the data.
        _logger.LogDebug("TasteProfiler.GetProfileAsync: awaiting migration and background service wiring");
        return Task.FromResult(new TasteProfile
        {
            UserId = userId,
            Summary = "Profile not yet generated — awaiting library analysis.",
            LastUpdatedAt = DateTimeOffset.UtcNow,
        });
    }

    public Task UpdateAsync(Guid userId, Guid assetId, CancellationToken ct = default)
    {
        _logger.LogDebug("TasteProfiler.UpdateAsync: incremental update for user {User}, asset {Asset}", userId, assetId);
        return Task.CompletedTask;
    }
}
