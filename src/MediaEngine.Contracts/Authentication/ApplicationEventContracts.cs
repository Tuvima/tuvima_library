using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Authentication;

public sealed record ApplicationEventSubjectDto(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("library_id")] Guid? LibraryId,
    [property: JsonPropertyName("profile_id")] Guid? ProfileId);

public sealed record ApplicationEventEnvelope(
    [property: JsonPropertyName("event_id")] Guid EventId,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("server_id")] string ServerId,
    [property: JsonPropertyName("subject")] ApplicationEventSubjectDto Subject,
    [property: JsonPropertyName("payload")] JsonElement Payload);

public sealed record ApplicationEventSubscriptionRequest(
    [property: JsonPropertyName("event_types")] IReadOnlyList<string> EventTypes,
    [property: JsonPropertyName("after_event_id")] Guid? AfterEventId,
    [property: JsonPropertyName("library_ids")] IReadOnlyList<Guid>? LibraryIds = null);

public sealed record ApplicationEventSubscriptionResult(
    [property: JsonPropertyName("subscription_id")] Guid SubscriptionId,
    [property: JsonPropertyName("replayed_count")] int ReplayedCount,
    [property: JsonPropertyName("gap_detected")] bool GapDetected,
    [property: JsonPropertyName("oldest_available_event_id")] Guid? OldestAvailableEventId,
    [property: JsonPropertyName("latest_event_id")] Guid? LatestEventId);

public sealed record ApplicationEventGapDto(
    [property: JsonPropertyName("last_delivered_event_id")] Guid? LastDeliveredEventId,
    [property: JsonPropertyName("oldest_available_event_id")] Guid? OldestAvailableEventId,
    [property: JsonPropertyName("reason")] string Reason);

public static class ApplicationEventClientMethods
{
    public const string HubPath = "/application-events";
    public const string Event = "ApplicationEvent";
    public const string Gap = "ApplicationEventGap";
    public const string Revoked = "ApplicationEventRevoked";
}
