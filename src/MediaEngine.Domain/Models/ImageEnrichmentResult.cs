namespace MediaEngine.Domain.Models;

public sealed record ImageEnrichmentResult
{
    public string Provider { get; init; } = "fanart_tv";
    public string ProviderName { get; init; } = "Fanart.tv";
    public string Status { get; init; } = "Skipped";
    public string? SkippedReason { get; init; }
    public string? Message { get; init; }
    public string? MediaType { get; init; }
    public string? BridgeKey { get; init; }
    public string? BridgeId { get; init; }
    public string? Endpoint { get; init; }
    public int? HttpStatusCode { get; init; }
    public int DownloadedCount { get; init; }
    public int UpdatedPreferredCount { get; init; }
    public IReadOnlyDictionary<string, int> StoredVariantCounts { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
    public DateTimeOffset LastCheckedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool Success => string.Equals(Status, "Completed", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(Status, "NoImages", StringComparison.OrdinalIgnoreCase);

    public bool Skipped => string.Equals(Status, "Skipped", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(Status, "NoResult", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(Status, "NoImages", StringComparison.OrdinalIgnoreCase);
}
