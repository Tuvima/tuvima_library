using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// A resolved QID label entry returned by <c>POST /metadata/labels/resolve</c>.
/// </summary>
public sealed class LabelResolveViewModel
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("entity_type")]
    public string? EntityType { get; init; }
}
