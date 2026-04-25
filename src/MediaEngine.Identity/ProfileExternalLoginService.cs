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

    public Task<ProfileExternalLogin?> ResolveAsync(string provider, string subject, CancellationToken ct = default)
    {
        ValidateProviderSubject(provider, subject);
        return _loginRepository.GetByProviderSubjectAsync(provider.Trim(), subject.Trim(), ct);
    }

    public async Task<ProfileExternalLogin> LinkAsync(
        Guid profileId,
        string provider,
        string subject,
        string? email,
        string? displayName,
        CancellationToken ct = default)
    {
        ValidateProviderSubject(provider, subject);

        var profile = await _profileRepository.GetByIdAsync(profileId, ct).ConfigureAwait(false);
        if (profile is null)
            throw new InvalidOperationException($"Profile '{profileId}' was not found.");

        var normalizedProvider = provider.Trim();
        var normalizedSubject = subject.Trim();
        var existing = await _loginRepository
            .GetByProviderSubjectAsync(normalizedProvider, normalizedSubject, ct)
            .ConfigureAwait(false);
        if (existing is not null)
            throw new InvalidOperationException("That external sign-in account is already linked.");

        var login = new ProfileExternalLogin
        {
            Id = Guid.NewGuid(),
            ProfileId = profileId,
            Provider = normalizedProvider,
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

    private static void ValidateProviderSubject(string provider, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        if (provider.Length > 100)
            throw new ArgumentException("Provider must be 100 characters or fewer.", nameof(provider));

        if (subject.Length > 300)
            throw new ArgumentException("Subject must be 300 characters or fewer.", nameof(subject));
    }
}
