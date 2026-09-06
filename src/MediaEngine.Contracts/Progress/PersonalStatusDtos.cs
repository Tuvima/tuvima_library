using MediaEngine.Contracts.Details;
namespace MediaEngine.Contracts.Progress;

public enum PersonalStatusAction { Complete, Reset, HideContinue, ShowContinue }
public sealed record PersonalStatusRequest(Guid CommandId, DetailEntityType EntityType, Guid TargetId,
    PersonalStatusAction Action, string ExpectedRevision);
public sealed record PersonalStatusInfo(string Revision, int OwnedCount, int StartedCount, int CompletedCount, bool Hidden);
public sealed record PersonalStatusCommandResult(Guid CommandId, int AffectedCount, string Revision);
public sealed record PersonalStatusHistoryItem(Guid Id, string Command, DateTimeOffset ChangedAt, bool Undone);
