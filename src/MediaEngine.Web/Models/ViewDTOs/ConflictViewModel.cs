using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard view model for a conflicted canonical value.
/// Maps from the Engine's <c>GET /metadata/conflicts</c> response.
/// Spec: Phase B – Conflict Surfacing (B-05).
/// </summary>
public sealed record ConflictViewModel(
    [property: JsonPropertyName("entity_id")]      Guid EntityId,
    [property: JsonPropertyName("key")]            string Key,
    [property: JsonPropertyName("value")]          string Value,
    [property: JsonPropertyName("last_scored_at")] DateTimeOffset LastScoredAt);
