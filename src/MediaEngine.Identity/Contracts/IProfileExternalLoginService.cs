using MediaEngine.Domain.Entities;

namespace MediaEngine.Identity.Contracts;

public interface IProfileExternalLoginService
{
    Task<IReadOnlyList<ProfileExternalLogin>> GetByProfileAsync(Guid profileId, CancellationToken ct = default);

    Task<ProfileExternalLogin?> ResolveAsync(string provider, string subject, CancellationToken ct = default);

    Task<ProfileExternalLogin> LinkAsync(
        Guid profileId,
        string provider,
        string subject,
        string? email,
        string? displayName,
        CancellationToken ct = default);

    Task<bool> UnlinkAsync(Guid id, CancellationToken ct = default);

    Task<bool> RecordLoginAsync(Guid id, CancellationToken ct = default);
}
