using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

public sealed class TextTrackViewModel
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = "und";

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("sourceFormat")]
    public string SourceFormat { get; set; } = string.Empty;

    [JsonPropertyName("normalizedFormat")]
    public string NormalizedFormat { get; set; } = string.Empty;

    [JsonPropertyName("timingMode")]
    public string TimingMode { get; set; } = string.Empty;

    [JsonPropertyName("isHearingImpaired")]
    public bool IsHearingImpaired { get; set; }

    [JsonPropertyName("isPreferred")]
    public bool IsPreferred { get; set; }

    [JsonPropertyName("isUserOwned")]
    public bool IsUserOwned { get; set; }

    [JsonPropertyName("isLocallyExported")]
    public bool IsLocallyExported { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}
