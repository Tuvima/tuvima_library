namespace MediaEngine.Contracts.Playback;

/// <summary>A lyrics or subtitle track available for a media asset.</summary>
public sealed class TextTrackDto
{
    [global::System.Text.Json.Serialization.JsonPropertyName("id")]
    public Guid Id { get; set; }

    [global::System.Text.Json.Serialization.JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [global::System.Text.Json.Serialization.JsonPropertyName("language")]
    public string Language { get; set; } = "und";

    [global::System.Text.Json.Serialization.JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [global::System.Text.Json.Serialization.JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [global::System.Text.Json.Serialization.JsonPropertyName("sourceFormat")]
    public string SourceFormat { get; set; } = string.Empty;

    [global::System.Text.Json.Serialization.JsonPropertyName("normalizedFormat")]
    public string NormalizedFormat { get; set; } = string.Empty;

    [global::System.Text.Json.Serialization.JsonPropertyName("timingMode")]
    public string TimingMode { get; set; } = string.Empty;

    [global::System.Text.Json.Serialization.JsonPropertyName("isHearingImpaired")]
    public bool IsHearingImpaired { get; set; }

    [global::System.Text.Json.Serialization.JsonPropertyName("isPreferred")]
    public bool IsPreferred { get; set; }

    [global::System.Text.Json.Serialization.JsonPropertyName("isUserOwned")]
    public bool IsUserOwned { get; set; }

    [global::System.Text.Json.Serialization.JsonPropertyName("isLocallyExported")]
    public bool IsLocallyExported { get; set; }

    [global::System.Text.Json.Serialization.JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Response for <c>POST /stream/{assetId}/text-tracks/refresh</c>. Property names (including
/// snake_case spelling) retain the original fields and add an explicit, truthful outcome
/// so editor clients can distinguish an update from an empty provider result.
/// </summary>
public sealed record RefreshTextTracksResponse
{
    public Guid asset_id { get; init; }
    public string enrichment_type { get; init; } = string.Empty;
    public bool refreshed { get; init; }
    public string status { get; init; } = string.Empty;
    public int before_count { get; init; }
    public int after_count { get; init; }
    public Guid? track_id { get; init; }
    public string message { get; init; } = string.Empty;
}
