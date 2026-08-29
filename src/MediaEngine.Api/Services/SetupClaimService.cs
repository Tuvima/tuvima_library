using System.Security.Cryptography;
using System.Text;
using MediaEngine.Contracts.Setup;
using MediaEngine.Identity.Contracts;
using MediaEngine.Storage;

namespace MediaEngine.Api.Services;

public sealed class SetupClaimService(
    OnboardingRepository repository,
    IFirstPartyIdentityService identity,
    TimeProvider timeProvider)
{
    public const string SessionHeader = "X-Tuvima-Setup-Session";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte[]? _claimHash;

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (await identity.IsAdministratorConfiguredAsync(ct).ConfigureAwait(false))
            return;
        await repository.ResetAbandonedClaimAsync(ct).ConfigureAwait(false);
        if (repository.Get().State == "unclaimed")
            await EnsureClaimTokenAsync(ct).ConfigureAwait(false);
    }

    public async Task<SetupClaimResponse?> ClaimAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureClaimTokenAsync(ct).ConfigureAwait(false);
            var supplied = Hash(Normalize(token));
            if (_claimHash is null || !CryptographicOperations.FixedTimeEquals(supplied, _claimHash))
                return null;

            var plaintextSession = Token(32);
            var sessionHash = Convert.ToHexStringLower(Hash(plaintextSession));
            var expires = timeProvider.GetUtcNow().AddHours(12);
            if (!await repository.TryClaimAsync(sessionHash, Guid.NewGuid(), expires, ct).ConfigureAwait(false))
                return null;
            _claimHash = null;
            return new SetupClaimResponse(plaintextSession, expires, await GetStatusAsync(ct).ConfigureAwait(false));
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<bool> ValidateSessionAsync(string? token, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(token)
            ? Task.FromResult(false)
            : repository.ValidateSessionAsync(Convert.ToHexStringLower(Hash(token)), ct);

    public async Task<SetupStatusDto> GetStatusAsync(CancellationToken ct)
    {
        var administrator = await identity.IsAdministratorConfiguredAsync(ct).ConfigureAwait(false);
        var workflow = repository.Get();
        return new SetupStatusDto(
            workflow.WorkflowVersion,
            workflow.State,
            workflow.CurrentStep,
            workflow.Revision,
            workflow.State == "unclaimed" && !administrator,
            administrator && workflow.State != "complete",
            administrator,
            workflow.Steps.Select(step => new SetupStepStatusDto(
                step.Key, step.Status, step.Detail, step.RepairTarget, step.CompletedAt)).ToList());
    }

    private async Task EnsureClaimTokenAsync(CancellationToken ct)
    {
        if (_claimHash is not null) return;
        if (await identity.IsAdministratorConfiguredAsync(ct).ConfigureAwait(false)) return;
        if (repository.Get().State != "unclaimed") return;
        var raw = Token(15).ToUpperInvariant();
        var display = string.Join('-', Enumerable.Range(0, (raw.Length + 4) / 5)
            .Select(index => raw.Substring(index * 5, Math.Min(5, raw.Length - index * 5))));
        _claimHash = Hash(Normalize(display));
        // Deliberately console-only: the claim secret must be visible in container
        // stdout without entering structured/file logs or HTTP responses.
        await Console.Out.WriteLineAsync($"[Tuvima Setup] Claim token: {display}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("[Tuvima Setup] Open /setup on your phone or browser and enter this one-time token.").ConfigureAwait(false);
    }

    private static string Normalize(string value) => value.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();
    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    private static string Token(int bytes) => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(bytes));
}

public sealed class SetupClaimHostedService(SetupClaimService claims) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => claims.InitializeAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
