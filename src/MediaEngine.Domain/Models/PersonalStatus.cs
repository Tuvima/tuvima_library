using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
namespace MediaEngine.Domain.Models;

public enum PersonalStatusCommand { Complete, Reset, HideContinue, ShowContinue }
public sealed record PersonalStatusTarget(Guid Id, MediaType MediaType)
{
    public IReadOnlySet<Guid>? AuthorizedAssetIds { get; init; }
}
public sealed record PersonalStatusSnapshot(string Revision, int OwnedCount, int StartedCount, int CompletedCount, bool Hidden);
public sealed record PersonalStatusResult(Guid CommandId, int AffectedCount, string Revision);
public sealed record PersonalStatusHistory(Guid Id, string Command, DateTimeOffset ChangedAt, bool Undone);
public sealed record PersonalStatusUndo(IReadOnlyList<UserState> Before, IReadOnlyList<UserState> After, int OwnedCount);
