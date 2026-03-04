using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard view model for a single metadata claim in the Curator's Drawer claim history.
/// Maps from the Engine's <c>GET /metadata/claims/{entityId}</c> response.
/// </summary>
public sealed record ClaimHistoryDto(
    [property: JsonPropertyName("id")]             Guid Id,
    [property: JsonPropertyName("claim_key")]      string ClaimKey,
    [property: JsonPropertyName("claim_value")]    string ClaimValue,
    [property: JsonPropertyName("provider_id")]    Guid ProviderId,
    [property: JsonPropertyName("confidence")]     double Confidence,
    [property: JsonPropertyName("is_user_locked")] bool IsUserLocked,
    [property: JsonPropertyName("claimed_at")]     DateTimeOffset ClaimedAt);
