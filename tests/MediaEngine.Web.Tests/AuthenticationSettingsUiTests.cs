namespace MediaEngine.Web.Tests;

public sealed class AuthenticationSettingsUiTests
{
    [Fact]
    public void AdministratorAuthenticationSurface_EditsEnforcedPolicyAndShowsReadiness()
    {
        var component = Read("src/MediaEngine.Web/Components/Settings/SecurityTab.razor");
        var client = Read("src/MediaEngine.Web/Services/Integration/EngineApiClient.Details.cs");

        Assert.Contains("UpdateAuthSettingsAsync", component, StringComparison.Ordinal);
        Assert.Contains("Password sign-in", component, StringComparison.Ordinal);
        Assert.Contains("Passkey sign-in", component, StringComparison.Ordinal);
        Assert.Contains("Allow remote sign-in", component, StringComparison.Ordinal);
        Assert.Contains("Session lifetime (hours)", component, StringComparison.Ordinal);
        Assert.Contains("Maximum active sessions per account", component, StringComparison.Ordinal);
        Assert.Contains("Trusted local networks", component, StringComparison.Ordinal);
        Assert.Contains("SaveProviderAsync", component, StringComparison.Ordinal);
        Assert.Contains("DeleteExternalAuthProviderAsync", component, StringComparison.Ordinal);
        Assert.Contains("Client secret", component, StringComparison.Ordinal);
        Assert.Contains("Disabled=\"@(!ExternalCapableMode)\"", component, StringComparison.Ordinal);
        Assert.DoesNotContain("session-policy", component, StringComparison.Ordinal);
        Assert.Contains("Configured, not ready", component, StringComparison.Ordinal);
        Assert.Contains("Send test email to my account", component, StringComparison.Ordinal);
        Assert.Contains("DashboardAuthenticationEndpoints.SendCurrentAccountTestEmailAsync", component, StringComparison.Ordinal);
        Assert.DoesNotContain("@bind-Value=\"_testEmail", component, StringComparison.Ordinal);
        Assert.Contains("if (_saving || _disposed) return;", component, StringComparison.Ordinal);
        Assert.Contains("OperationCanceledException", component, StringComparison.Ordinal);
        Assert.Contains("Use an absolute HTTPS URL, or a loopback URL for local use.", component, StringComparison.Ordinal);
        Assert.DoesNotContain("Session duration is not separately configurable", component, StringComparison.Ordinal);
        Assert.Contains("PutAsync<UpdateAuthSettingsRequest, AuthSettingsDto>", client, StringComparison.Ordinal);
        Assert.Contains("/settings/security/auth/providers/", client, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalCallback_UsesPurposeBoundTicketAndDedicatedAuthenticatedLinkRoute()
    {
        var callback = Read("src/MediaEngine.Web/Services/Integration/ExternalAuthenticationRegistration.cs");
        var dashboard = Read("src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs");
        var contracts = Read("src/MediaEngine.Contracts/Authentication/AuthContracts.cs");

        Assert.Contains("BeginExternalIdentityTransactionAsync", callback, StringComparison.Ordinal);
        Assert.Matches(@"TransactionTicket\s*=\s*transaction\.Ticket\b", callback);
        Assert.Contains("ExternalIdentityTransactionPurposes.Link", callback, StringComparison.Ordinal);
        Assert.Contains("/account/security/external/", dashboard, StringComparison.Ordinal);
        Assert.Contains("transaction_ticket", contracts, StringComparison.Ordinal);
        var requestStart = contracts.IndexOf("public sealed class ExternalSessionRequest", StringComparison.Ordinal);
        var requestEnd = contracts.IndexOf("public static class ExternalIdentityTransactionPurposes", StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\[JsonPropertyName\(""subject""\)\]\s*public\s+string\s+Subject\b",
            contracts[requestStart..requestEnd]);
    }

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(
        FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
