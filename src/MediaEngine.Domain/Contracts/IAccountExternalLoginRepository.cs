using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

public interface IAccountExternalLoginRepository
{
    Task<IReadOnlyList<AccountExternalLogin>> GetByAccountAsync(Guid accountId, CancellationToken ct = default);

    Task<AccountExternalLogin?> GetByProviderSubjectAsync(
        string provider,
        string issuer,
        string subject,
        CancellationToken ct = default);

    Task InsertAsync(AccountExternalLogin login, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<bool> TouchLastLoginAsync(Guid id, DateTimeOffset lastLoginAt, CancellationToken ct = default);
}
