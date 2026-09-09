using System.Collections.ObjectModel;

namespace MediaEngine.Domain.Authorization;

public sealed record AuthorizationAuditEvent
{
    public AuthorizationAuditEvent(
        string eventType,
        DateTimeOffset occurredAt,
        Guid? actorAccountId,
        Guid? actorProfileId,
        Guid? actorApplicationId,
        string subjectType,
        string subjectId,
        IReadOnlyDictionary<string, string?> changes)
    {
        EventType = RequireText(eventType, nameof(eventType));
        OccurredAt = occurredAt;
        ActorAccountId = actorAccountId;
        ActorProfileId = actorProfileId;
        ActorApplicationId = actorApplicationId;
        SubjectType = RequireText(subjectType, nameof(subjectType));
        SubjectId = RequireText(subjectId, nameof(subjectId));
        Changes = new ReadOnlyDictionary<string, string?>(
            new Dictionary<string, string?>(changes ?? throw new ArgumentNullException(nameof(changes)), StringComparer.Ordinal));
    }

    public string EventType { get; }
    public DateTimeOffset OccurredAt { get; }
    public Guid? ActorAccountId { get; }
    public Guid? ActorProfileId { get; }
    public Guid? ActorApplicationId { get; }
    public string SubjectType { get; }
    public string SubjectId { get; }
    public IReadOnlyDictionary<string, string?> Changes { get; }

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
}
