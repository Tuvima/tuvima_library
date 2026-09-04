namespace MediaEngine.Identity.Contracts;

/// <summary>
/// Break-glass password recovery available only to host-side administrative tooling.
/// This contract must not be exposed through the anonymous HTTP surface.
/// </summary>
public interface IHostAdministratorRecoveryService
{
    Task<IReadOnlyList<string>> ResetAdministratorPasswordFromHostAsync(
        string email,
        string newPassword,
        CancellationToken ct = default);
}
