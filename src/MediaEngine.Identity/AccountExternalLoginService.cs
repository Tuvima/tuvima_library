using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Identity.Contracts;

namespace MediaEngine.Identity;

public sealed class AccountExternalLoginService : IAccountExternalLoginService
{
    private readonly IAccountExternalLoginRepository _loginRepository;
    private readonly IAccountRepository _accountRepository;

    public AccountExternalLoginService(
        IAccountExternalLoginRepository loginRepository,
        IAccountRepository accountRepository)
    {
        ArgumentNullException.ThrowIfNull(loginRepository);
        ArgumentNullException.ThrowIfNull(accountRepository);

        _loginRepository = loginRepository;
        _accountRepository = accountRepository;
    }

    public Task<IReadOnlyList<AccountExternalLogin>> GetByAccountAsync(Guid accountId, CancellationToken ct = default) =>
        _loginRepository.GetByAccountAsync(accountId, ct);

    public Task<AccountExternalLogin?> ResolveAsync(string provider, string issuer, string subject, CancellationToken ct = default)
    {
        ValidateIdentity(provider, issuer, subject);
        return _loginRepository.GetByProviderSubjectAsync(provider.Trim(), NormalizeIssuer(issuer), subject.Trim(), ct);
    }

    public async Task<AccountExternalLogin> LinkAsync(
        Guid accountId,
        string provider,
        string issuer,
        string subject,
        string? email,
        string? displayName,
        CancellationToken ct = default)
    {
        ValidateIdentity(provider, issuer, subject);

        var account = await _accountRepository.GetByIdAsync(accountId, ct).ConfigureAwait(false);
        if (account is null)
            throw new InvalidOperationException($"Account '{accountId}' was not found.");

        var normalizedProvider = provider.Trim();
        var normalizedIssuer = NormalizeIssuer(issuer);
        var normalizedSubject = subject.Trim();
        var existing = await _loginRepository
            .GetByProviderSubjectAsync(normalizedProvider, normalizedIssuer, normalizedSubject, ct)
            .ConfigureAwait(false);
        if (existing is not null)
            throw new InvalidOperationException("That external sign-in account is already linked.");

        var login = new AccountExternalLogin
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Provider = normalizedProvider,
            Issuer = normalizedIssuer,
            Subject = normalizedSubject,
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            LinkedAt = DateTimeOffset.UtcNow,
        };

        await _loginRepository.InsertAsync(login, ct).ConfigureAwait(false);
        return login;
    }

    public Task<bool> UnlinkAsync(Guid id, CancellationToken ct = default) =>
        _loginRepository.DeleteAsync(id, ct);

    public Task<bool> RecordLoginAsync(Guid id, CancellationToken ct = default) =>
        _loginRepository.TouchLastLoginAsync(id, DateTimeOffset.UtcNow, ct);

    private static void ValidateIdentity(string provider, string issuer, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        if (provider.Length > 100)
            throw new ArgumentException("Provider must be 100 characters or fewer.", nameof(provider));

        if (issuer.Length > 300 || !Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri) || issuerUri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Issuer must be an absolute HTTPS URL of 300 characters or fewer.", nameof(issuer));

        if (subject.Length > 300)
            throw new ArgumentException("Subject must be 300 characters or fewer.", nameof(subject));
    }

    // OIDC issuer identifiers are exact values. Do not remove a trailing slash or
    // otherwise rewrite the validated issuer; doing so can merge distinct issuers.
    private static string NormalizeIssuer(string issuer) => issuer.Trim();
}
