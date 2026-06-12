using MediaEngine.Domain.Enums;

namespace MediaEngine.Providers.Models;

internal sealed record ShelfIdentity(
    MediaType MediaType,
    string Label,
    string? Qid,
    string? ProviderKey,
    IReadOnlyList<string> RelationshipKeys)
{
    public string LockKey => !string.IsNullOrWhiteSpace(Qid)
        ? $"qid:{Qid}"
        : $"key:{ProviderKey}";
}
