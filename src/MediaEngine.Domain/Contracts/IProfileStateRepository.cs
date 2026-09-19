using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Contracts;

public interface IProfileStateRepository
{
    Task<IReadOnlyList<ProfileSavedItem>> GetSavedItemsAsync(Guid profileId, CancellationToken ct = default);
    Task<ProfileSavedItem?> GetSavedItemAsync(Guid profileId, ProfileEntityKind entityKind, Guid entityId, CancellationToken ct = default);
    Task<ProfileSavedItem> SaveItemAsync(Guid profileId, ProfileEntityKind entityKind, Guid entityId, int? position = null, CancellationToken ct = default);
    Task<bool> RemoveSavedItemAsync(Guid profileId, ProfileEntityKind entityKind, Guid entityId, CancellationToken ct = default);

    Task<IReadOnlyList<ProfileReactionState>> GetReactionsAsync(Guid profileId, CancellationToken ct = default);
    Task<ProfileReactionState?> GetReactionAsync(Guid profileId, ProfileEntityKind entityKind, Guid entityId, CancellationToken ct = default);
    Task<ProfileReactionState> SetReactionAsync(Guid profileId, ProfileEntityKind entityKind, Guid entityId, ProfileReactionKind reaction, CancellationToken ct = default);
    Task<bool> RemoveReactionAsync(Guid profileId, ProfileEntityKind entityKind, Guid entityId, CancellationToken ct = default);
}

public sealed record ProfileSavedItem(
    Guid ProfileId,
    ProfileEntityKind EntityKind,
    Guid EntityId,
    DateTimeOffset SavedAt,
    int? Position);

public sealed record ProfileReactionState(
    Guid ProfileId,
    ProfileEntityKind EntityKind,
    Guid EntityId,
    ProfileReactionKind Reaction,
    DateTimeOffset UpdatedAt);
