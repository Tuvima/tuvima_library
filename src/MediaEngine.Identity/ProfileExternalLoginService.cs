using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Identity.Contracts;

namespace MediaEngine.Identity;

public sealed class ProfileExternalLoginService : IProfileExternalLoginService
{
    private readonly IProfileExternalLoginRepository _loginRepository;
    private readonly IProfileRepository _profileRepository;

    public ProfileExternalLoginService(
        IProfileExternalLoginRepository loginRepository,
        IProfileRepository profileRepository)
    {
        ArgumentNullException.ThrowIfNull(loginRepository);
        ArgumentNullException.ThrowIfNull(profileRepository);

        _loginRepository = loginRepository;
        _profileRepository = profileRepository;
    }

    public Task<IReadOnlyList<ProfileExternalLogin>> GetByProfileAsync(Guid profileId, CancellationToken ct = default) =>
        _loginRepository.GetByProfileAsync(profileId, ct);

    public Task<ProfileExternalLogin?> ResolveAsync(string provider, string issuer, string subject, CancellationToken ct = default)
    {
        ValidateIdentity(provider, issuer, subject);
        return _loginRepository.GetByProviderSubjectAsync(provider.Trim(), NormalizeIssuer(issuer), subject.Trim(), ct);
    }

    public async Task<ProfileExternalLogin> LinkAsync(
        Guid profileId,
        string provider,
        string issuer,
        string subject,
        string? email,
        string? displayName,
        CancellationToken ct = default)
    {
        ValidateIdentity(provider, issuer, subject);

        var profile = await _profileRepository.GetByIdAsync(profileId, ct).ConfigureAwait(false);
        if (profile is null)
            throw new InvalidOperationException($"Profile '{profileId}' was not found.");

        var normalizedProvider = provider.Trim();
        var normalizedIssuer = NormalizeIssuer(issuer);
        var normalizedSubject = subject.Trim();
        var existing = await _loginRepository
            .GetByProviderSubjectAsync(normalizedProvider, normalizedIssuer, normalizedSubject, ct)
            .ConfigureAwait(false);
        if (existing is not null)
            throw new InvalidOperationException("That external sign-in account is already linked.");

        var login = new ProfileExternalLogin
        {
            Id = Guid.NewGuid(),
            ProfileId = profileId,
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
