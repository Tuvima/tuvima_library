namespace MediaEngine.Contracts.Playback;

/// <summary>
/// Response for <c>POST /stream/{assetId}/text-tracks/refresh</c>. Property names (including
/// snake_case spelling) are byte-identical to the anonymous object this record replaced
/// (Stage 5A wave 2 response-shape promotion) so the wire shape does not change.
/// </summary>
public sealed record RefreshTextTracksResponse
{
    public Guid asset_id { get; init; }
    public string enrichment_type { get; init; } = string.Empty;
    public bool refreshed { get; init; }
}
