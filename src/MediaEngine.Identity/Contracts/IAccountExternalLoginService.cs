using MediaEngine.Domain.Entities;

namespace MediaEngine.Identity.Contracts;

public interface IAccountExternalLoginService
{
    Task<IReadOnlyList<AccountExternalLogin>> GetByAccountAsync(Guid accountId, CancellationToken ct = default);

    Task<AccountExternalLogin?> ResolveAsync(string provider, string issuer, string subject, CancellationToken ct = default);

    Task<AccountExternalLogin> LinkAsync(
        Guid accountId,
        string provider,
        string issuer,
        string subject,
        string? email,
        string? displayName,
        CancellationToken ct = default);

    Task<bool> UnlinkAsync(Guid id, CancellationToken ct = default);

    Task<bool> RecordLoginAsync(Guid id, CancellationToken ct = default);
}
