using MediaEngine.Domain.Entities;
using MediaEngine.Identity;
using MediaEngine.Identity.Contracts;
using MediaEngine.Storage;
using Microsoft.AspNetCore.Identity;

namespace MediaEngine.Admin;

public static class TuvimaAdminApplication
{
    public static async Task<int> RunAsync(
        string[] args,
        CancellationToken ct = default)
    {
        var console = new SystemAdminConsole();
        if (!AdminCommandOptions.TryParse(args, out var options, out var error))
        {
            console.WriteError(error);
            WriteHelp(console);
            return 2;
        }

        if (options.ShowHelp)
        {
            WriteHelp(console);
            return 0;
        }

        var authorizer = new SystemHostRecoveryAuthorizer();
        try
        {
            // Authorize before resolving or probing the library path so an
            // unprivileged caller cannot use this tool to inspect host state.
            authorizer.EnsureAuthorized();
        }
        catch (UnauthorizedAccessException ex)
        {
            console.WriteError(ex.Message);
            return 3;
        }

        var configDirectory = options.ConfigDirectory
            ?? Environment.GetEnvironmentVariable("TUVIMA_CONFIG_DIR")
            ?? "config";
        var databasePath = TuvimaDataPathResolver.ResolveDatabasePath(
            configDirectory,
            Environment.GetEnvironmentVariable("TUVIMA_DB_PATH"),
            Environment.GetEnvironmentVariable("TUVIMA_LIBRARY_ROOT"));
        var fullDatabasePath = Path.GetFullPath(databasePath);
        if (!File.Exists(fullDatabasePath))
        {
            console.WriteError($"Tuvima database was not found at '{fullDatabasePath}'. Use --config-dir or TUVIMA_DB_PATH to select the installed library.");
            return 4;
        }

        using var database = new DatabaseConnection(fullDatabasePath);
        var identities = new IdentityRepository(database);
        var profiles = new ProfileRepository(database);
        IHostAdministratorRecoveryService recovery = new FirstPartyIdentityService(
            identities,
            profiles,
            new PasswordHasher<ProfileCredential>(),
            TimeProvider.System);
        var command = new ResetAdministratorPasswordCommand(
            authorizer,
            recovery,
            console);
        return await command.ExecuteAsync(options.Username, ct).ConfigureAwait(false);
    }

    private static void WriteHelp(IAdminConsole console)
    {
        console.WriteLine("Tuvima Library host administration");
        console.WriteLine(string.Empty);
        console.WriteLine("Usage:");
        console.WriteLine("  tuvima-admin auth reset-password [--username <name>] [--config-dir <path>]");
        console.WriteLine(string.Empty);
        console.WriteLine("The command requires elevated host privileges and prompts securely for the new password.");
        console.WriteLine("It revokes every session and rotates all recovery codes for the administrator.");
    }

    private sealed record AdminCommandOptions(
        bool ShowHelp,
        string? Username,
        string? ConfigDirectory)
    {
        public static bool TryParse(
            IReadOnlyList<string> args,
            out AdminCommandOptions options,
            out string error)
        {
            options = new(false, null, null);
            error = string.Empty;
            if (args.Count == 1 && args[0] is "--help" or "-h")
            {
                options = options with { ShowHelp = true };
                return true;
            }

            if (args.Count < 2
                || !args[0].Equals("auth", StringComparison.OrdinalIgnoreCase)
                || !args[1].Equals("reset-password", StringComparison.OrdinalIgnoreCase))
            {
                error = "Unknown command.";
                return false;
            }

            string? username = null;
            string? configDirectory = null;
            for (var index = 2; index < args.Count; index++)
            {
                if (args[index] is "--help" or "-h")
                {
                    options = new(true, username, configDirectory);
                    return true;
                }

                if (index + 1 >= args.Count)
                {
                    error = $"Option '{args[index]}' requires a value.";
                    return false;
                }

                var value = args[++index];
                if (args[index - 1].Equals("--username", StringComparison.OrdinalIgnoreCase))
                {
                    username = value;
                }
                else if (args[index - 1].Equals("--config-dir", StringComparison.OrdinalIgnoreCase))
                {
                    configDirectory = value;
                }
                else
                {
                    error = $"Unknown option '{args[index - 1]}'.";
                    return false;
                }
            }

            options = new(false, username, configDirectory);
            return true;
        }
    }
}
