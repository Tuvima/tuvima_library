using MediaEngine.Domain.Enums;

namespace MediaEngine.Contracts.ProfileState;

public sealed record ProfileSavedItemDto(
    ProfileEntityKind EntityKind,
    Guid EntityId,
    DateTimeOffset SavedAt,
    int? Position);

public sealed record ProfileReactionDto(
    ProfileEntityKind EntityKind,
    Guid EntityId,
    ProfileReactionKind Reaction,
    DateTimeOffset UpdatedAt);

public sealed record ProfileStateMutationDto(
    ProfileEntityKind EntityKind,
    Guid EntityId,
    bool IsSaved,
    ProfileReactionKind? Reaction,
    DateTimeOffset UpdatedAt);
