using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Progress payload broadcast by the Engine over SignalR during an
/// initial hash sweep. Matches the anonymous object emitted in
/// <c>InitialSweepService.RunAsync</c>.
/// Spec: side-by-side-with-Plex plan §M.
/// </summary>
public sealed class InitialSweepProgressDto
{
    [JsonPropertyName("discovered")]
    public int Discovered { get; set; }

    [JsonPropertyName("processed")]
    public int Processed { get; set; }

    [JsonPropertyName("hashed")]
    public int Hashed { get; set; }

    [JsonPropertyName("cached")]
    public int Cached { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("bytes_hashed")]
    public long BytesHashed { get; set; }

    [JsonPropertyName("elapsed_seconds")]
    public double ElapsedSeconds { get; set; }
}
