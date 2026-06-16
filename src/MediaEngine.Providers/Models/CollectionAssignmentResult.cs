namespace MediaEngine.Providers.Models;

public enum CollectionAssignmentOutcome
{
    Assigned,
    AlreadyAssigned,
    SkippedNoWork,
    SkippedNoShelfIdentity,
    Failed,
}

public sealed record CollectionAssignmentResult(
    CollectionAssignmentOutcome Outcome,
    Guid EntityId,
    Guid? WorkId = null,
    Guid? CollectionId = null,
    bool CreatedCollection = false,
    int RelationshipsAdded = 0,
    string? IdentityKey = null,
    string? Message = null)
{
    public bool HasCollection => CollectionId.HasValue;
}
