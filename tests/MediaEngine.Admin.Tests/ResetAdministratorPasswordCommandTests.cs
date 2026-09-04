using MediaEngine.Identity.Contracts;

namespace MediaEngine.Admin.Tests;

public sealed class ResetAdministratorPasswordCommandTests
{
    [Fact]
    public async Task UnauthorizedHost_CannotReadInputOrChangeCredential()
    {
        var recovery = new RecordingRecoveryService();
        var console = new RecordingConsole([], []);
        var command = new ResetAdministratorPasswordCommand(
            new DeniedAuthorizer(),
            recovery,
            console);

        var exitCode = await command.ExecuteAsync("administrator@example.com");

        Assert.Equal(3, exitCode);
        Assert.Equal(0, console.SecretReadCount);
        Assert.Null(recovery.Email);
        Assert.Contains(console.Errors, message => message.Contains("elevated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MismatchedConfirmation_DoesNotChangeCredential()
    {
        var recovery = new RecordingRecoveryService();
        var console = new RecordingConsole([], ["new password", "different password"]);
        var command = new ResetAdministratorPasswordCommand(
            new AllowedAuthorizer(),
            recovery,
            console);

        var exitCode = await command.ExecuteAsync("administrator@example.com");

        Assert.Equal(2, exitCode);
        Assert.Null(recovery.Email);
        Assert.Contains(console.Errors, message => message.Contains("do not match", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AuthorizedReset_RevokesSessionsAndPrintsRotatedRecoveryCodes()
    {
        var recovery = new RecordingRecoveryService
        {
            RecoveryCodes = ["alpha-bravo", "charlie-delta"],
        };
        var console = new RecordingConsole([], ["new password", "new password"]);
        var command = new ResetAdministratorPasswordCommand(
            new AllowedAuthorizer(),
            recovery,
            console);

        var exitCode = await command.ExecuteAsync("administrator@example.com");

        Assert.Equal(0, exitCode);
        Assert.Equal("administrator@example.com", recovery.Email);
        Assert.Equal("new password", recovery.Password);
        Assert.Contains(console.Output, message => message.Contains("Every existing session has been revoked", StringComparison.Ordinal));
        Assert.Contains("alpha-bravo", console.Output);
        Assert.Contains("charlie-delta", console.Output);
    }

    private sealed class AllowedAuthorizer : IHostRecoveryAuthorizer
    {
        public void EnsureAuthorized()
        {
        }
    }

    private sealed class DeniedAuthorizer : IHostRecoveryAuthorizer
    {
        public void EnsureAuthorized() => throw new UnauthorizedAccessException(
            "Host recovery requires an elevated terminal.");
    }

    private sealed class RecordingRecoveryService : IHostAdministratorRecoveryService
    {
        public string? Email { get; private set; }
        public string? Password { get; private set; }
        public IReadOnlyList<string> RecoveryCodes { get; init; } = [];

        public Task<IReadOnlyList<string>> ResetAdministratorPasswordFromHostAsync(
            string email,
            string newPassword,
            CancellationToken ct = default)
        {
            Email = email;
            Password = newPassword;
            return Task.FromResult(RecoveryCodes);
        }
    }

    private sealed class RecordingConsole(
        IEnumerable<string?> lines,
        IEnumerable<string> secrets) : IAdminConsole
    {
        private readonly Queue<string?> _lines = new(lines);
        private readonly Queue<string> _secrets = new(secrets);

        public List<string> Output { get; } = [];
        public List<string> Errors { get; } = [];
        public int SecretReadCount { get; private set; }

        public string? ReadLine(string prompt) => _lines.Dequeue();

        public string ReadSecret(string prompt)
        {
            SecretReadCount++;
            return _secrets.Dequeue();
        }

        public void WriteLine(string message) => Output.Add(message);

        public void WriteError(string message) => Errors.Add(message);
    }
}
