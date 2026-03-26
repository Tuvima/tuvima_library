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

    public Task<WatchingOrder> RecommendOrderAsync(
        Guid hubId,
        string orderType,
        CancellationToken ct = default)
    {
        // The API endpoint will query Hub works and pass them to the LLM for ordering.
        // This implementation is ready for endpoint integration — the service is wired
        // and registered; the caller must supply pre-loaded work titles.
        _logger.LogInformation("WatchingOrderAdvisor: order type '{Type}' for hub {Hub}", orderType, hubId);

        return Task.FromResult(new WatchingOrder
        {
            HubId       = hubId,
            OrderType   = orderType,
            Entries     = [],
            Explanation = "Order generation requires Hub work data — call via API endpoint with pre-loaded works.",
        });
    }
}
