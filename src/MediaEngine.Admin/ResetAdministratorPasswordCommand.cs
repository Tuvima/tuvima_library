using MediaEngine.Identity.Contracts;

namespace MediaEngine.Admin;

public sealed class ResetAdministratorPasswordCommand(
    IHostRecoveryAuthorizer authorizer,
    IHostAdministratorRecoveryService recovery,
    IAdminConsole console)
{
    public async Task<int> ExecuteAsync(string? suppliedUsername, CancellationToken ct = default)
    {
        try
        {
            authorizer.EnsureAuthorized();
            var username = string.IsNullOrWhiteSpace(suppliedUsername)
                ? console.ReadLine("Administrator username: ")
                : suppliedUsername;
            if (string.IsNullOrWhiteSpace(username))
            {
                console.WriteError("Administrator username is required.");
                return 2;
            }

            var password = console.ReadSecret("New password: ");
            var confirmation = console.ReadSecret("Confirm new password: ");
            if (!password.Equals(confirmation, StringComparison.Ordinal))
            {
                console.WriteError("The passwords do not match. No changes were made.");
                return 2;
            }

            var recoveryCodes = await recovery.ResetAdministratorPasswordFromHostAsync(
                username,
                password,
                ct).ConfigureAwait(false);

            console.WriteLine("Administrator password reset successfully. Every existing session has been revoked.");
            console.WriteLine("Save these new one-time recovery codes. Previous recovery codes are no longer valid:");
            foreach (var code in recoveryCodes)
            {
                console.WriteLine(code);
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            console.WriteError("Password reset was cancelled. No changes were made.");
            return 130;
        }
        catch (UnauthorizedAccessException ex)
        {
            console.WriteError(ex.Message);
            return 3;
        }
        catch (ArgumentException ex)
        {
            console.WriteError(ex.Message);
            return 2;
        }
        catch (InvalidOperationException ex)
        {
            console.WriteError(ex.Message);
            return 1;
        }
    }
}
