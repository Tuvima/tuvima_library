using System.Security.Cryptography;
using System.Text;
using MediaEngine.Contracts.Setup;
using MediaEngine.Identity.Contracts;
using MediaEngine.Storage;

namespace MediaEngine.Api.Services;

/// <summary>
/// Issues a short-lived browser session while a new server has no administrator.
/// Setup sessions stop authorizing requests as soon as the first administrator exists.
/// </summary>
public sealed class SetupSessionService(
    OnboardingRepository repository,
    IFirstPartyIdentityService identity,
    TimeProvider timeProvider)
{
    public const string SessionHeader = "X-Tuvima-Setup-Session";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<SetupStartResponse?> BeginAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (await identity.IsAdministratorConfiguredAsync(ct).ConfigureAwait(false))
                return null;

            var plaintextSession = Token(32);
            var sessionHash = Convert.ToHexStringLower(Hash(plaintextSession));
            var expires = timeProvider.GetUtcNow().AddHours(12);
            if (!await repository.TryBeginAsync(sessionHash, Guid.NewGuid(), expires, ct).ConfigureAwait(false))
                return null;

            return new SetupStartResponse(
                plaintextSession,
                expires,
                await GetStatusAsync(ct).ConfigureAwait(false));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ValidateSessionAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)
            || await identity.IsAdministratorConfiguredAsync(ct).ConfigureAwait(false))
        {
            return false;
        }

        return await repository.ValidateSessionAsync(
            Convert.ToHexStringLower(Hash(token)),
            ct).ConfigureAwait(false);
    }

    public async Task<SetupStatusDto> GetStatusAsync(CancellationToken ct)
    {
        var administrator = await identity.IsAdministratorConfiguredAsync(ct).ConfigureAwait(false);
        var workflow = repository.Get();
        return new SetupStatusDto(
            workflow.WorkflowVersion,
            workflow.State,
            workflow.CurrentStep,
            workflow.Revision,
            !administrator && workflow.State != "complete",
            administrator && workflow.State != "complete",
            administrator,
            workflow.Steps.Select(step => new SetupStepStatusDto(
                step.Key, step.Status, step.Detail, step.RepairTarget, step.CompletedAt)).ToList());
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    private static string Token(int bytes) => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(bytes));
}
