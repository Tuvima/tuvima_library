using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

public interface IProfileExternalLoginRepository
{
    Task<IReadOnlyList<ProfileExternalLogin>> GetByProfileAsync(Guid profileId, CancellationToken ct = default);

    Task<ProfileExternalLogin?> GetByProviderSubjectAsync(
        string provider,
        string subject,
        CancellationToken ct = default);

    Task InsertAsync(ProfileExternalLogin login, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<bool> TouchLastLoginAsync(Guid id, DateTimeOffset lastLoginAt, CancellationToken ct = default);
}
