using MediaEngine.AI.Llama;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Features;

public sealed class WatchingOrderAdvisor : IWatchingOrderAdvisor
{
    private readonly LlamaInferenceService _llama;
    private readonly ILogger<WatchingOrderAdvisor> _logger;

    public WatchingOrderAdvisor(LlamaInferenceService llama, ILogger<WatchingOrderAdvisor> logger)
    {
        _llama = llama;
        _logger = logger;
    }

    public async Task<WatchingOrder> RecommendOrderAsync(
        Guid hubId,
        string orderType,
        CancellationToken ct = default)
    {
        // This will be called with the Hub's works list from the API endpoint.
        // For now, return an empty order — the full implementation needs
        // Hub/Work repository access which will be wired in the API layer.
        _logger.LogInformation("WatchingOrderAdvisor: order type '{Type}' for hub {Hub}", orderType, hubId);

        return new WatchingOrder
        {
            HubId = hubId,
            OrderType = orderType,
            Entries = [],
            Explanation = "Watching order generation requires Hub data — will be wired via API endpoint.",
        };
    }
}
