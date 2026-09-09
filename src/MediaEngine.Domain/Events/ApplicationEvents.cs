using System.Text.Json;
using MediaEngine.Domain.Authorization;

namespace MediaEngine.Domain.Events;

public sealed record ApplicationEventSubject(
    string Type,
    string Id,
    Guid? LibraryId = null,
    Guid? ProfileId = null,
    AccountFeatureId? FeatureId = null);

public sealed record ApplicationEventDraft(
    string EventType,
    int Version,
    DateTimeOffset OccurredAt,
    ApplicationEventSubject Subject,
    JsonElement Payload);

public sealed record StoredApplicationEvent(
    long Sequence,
    Guid EventId,
    string EventType,
    int Version,
    DateTimeOffset OccurredAt,
    string ServerId,
    ApplicationEventSubject Subject,
    string PayloadJson);

public sealed record ApplicationEventBounds(long? OldestSequence, Guid? OldestEventId, long? LatestSequence, Guid? LatestEventId);

public interface IApplicationEventRepository
{
    Task<StoredApplicationEvent> AppendAsync(StoredApplicationEvent value, CancellationToken ct = default);
    Task<IReadOnlyList<StoredApplicationEvent>> ReadAfterAsync(long sequence, int limit, CancellationToken ct = default);
    Task<long?> FindSequenceAsync(Guid eventId, CancellationToken ct = default);
    Task<ApplicationEventBounds> GetBoundsAsync(CancellationToken ct = default);
    Task<int> PruneAsync(DateTimeOffset olderThan, int retainNewest, CancellationToken ct = default);
}

public interface IApplicationEventProducer
{
    Task PublishAsync(ApplicationEventDraft value, CancellationToken ct = default);
}

public interface IApplicationEventResourceResolver
{
    Task<ApplicationEventResourceProvenance?> ResolveAsync(Guid assetId, CancellationToken ct = default);
}

public sealed record ApplicationEventResourceProvenance(Guid LibraryId, AccountFeatureId FeatureId);

public interface IApplicationEventDeliveryAuthorizer
{
    ValueTask<bool> CanSubscribeAsync(Guid applicationId, IReadOnlyList<string> eventTypes, CancellationToken ct = default);
    ValueTask<bool> CanDeliverAsync(Guid applicationId, StoredApplicationEvent value, CancellationToken ct = default);
}

public sealed record ApplicationEventDefinition(
    string EventType,
    ApplicationPermissionId ReadPermission,
    bool IsLibraryScoped,
    bool IsProfileScoped = false);
