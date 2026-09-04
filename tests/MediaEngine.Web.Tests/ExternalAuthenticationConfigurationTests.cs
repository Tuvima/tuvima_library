using MediaEngine.Domain.Configuration;
using MediaEngine.Web.Services.Configuration;
using MediaEngine.Web.Services.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace MediaEngine.Web.Tests;

public sealed class ExternalAuthenticationConfigurationTests
{
    [Fact]
    public void DashboardConfiguration_LoadsProviderSecretOnlyFromPrivateOverlay()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tuvima-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, ".secrets"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "core.json"), """
                {
                  "auth": {
                    "mode": "Hybrid",
                    "external_providers": [
                      {
                        "id": "google",
                        "kind": "oidc",
                        "enabled": true,
                        "display_name": "Google",
                        "authority": "https://accounts.google.com",
                        "client_id": "client-id",
                        "client_secret": "must-not-load-from-core",
                        "scopes": ["openid", "profile", "email"]
                      }
                    ]
                  }
                }
                """);
            File.WriteAllText(Path.Combine(directory, ".secrets", "auth-providers.json"), """
                {
                  "providers": {
                    "google": { "client_secret": "private-secret" }
                  }
                }
                """);

            var core = new DashboardConfigurationReader(directory).LoadCore();

            Assert.Equal("private-secret", core.Auth.ExternalProviders.Single().ClientSecret);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Registration_CreatesIndependentOidcAndOAuthSchemes()
    {
        var services = new ServiceCollection();
        var authentication = services.AddAuthentication();
        var providers = new ExternalAuthProviderSettings[]
        {
            new()
            {
                Id = "google",
                Kind = ExternalAuthProviderKinds.OpenIdConnect,
                Enabled = true,
                DisplayName = "Google",
                Authority = "https://accounts.google.com",
                ClientId = "google-client",
                Scopes = ["openid", "profile", "email"],
            },
            new()
            {
                Id = "github",
                Kind = ExternalAuthProviderKinds.OAuth,
                Enabled = true,
                DisplayName = "GitHub",
                Issuer = "https://github.com",
                ClientId = "github-client",
                ClientSecret = "secret",
                Scopes = ["read:user"],
                AuthorizationEndpoint = "https://github.com/login/oauth/authorize",
                TokenEndpoint = "https://github.com/login/oauth/access_token",
                UserInformationEndpoint = "https://api.github.com/user",
            },
        };

        var registrations = authentication.AddTuvimaExternalProviders(providers);

        Assert.Collection(
            registrations,
            google =>
            {
                Assert.Equal("Tuvima.External.google", google.AuthenticationScheme);
                Assert.Equal("/signin-tuvima-google", google.CallbackPath);
            },
            github =>
            {
                Assert.Equal("Tuvima.External.github", github.AuthenticationScheme);
                Assert.Equal("/signin-tuvima-github", github.CallbackPath);
            });

        using var provider = services.BuildServiceProvider();
        var oidc = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get("Tuvima.External.google");
        var oauth = provider.GetRequiredService<IOptionsMonitor<OAuthOptions>>()
            .Get("Tuvima.External.github");
        Assert.True(oidc.UsePkce);
        Assert.True(oidc.ProtocolValidator.RequireNonce);
        Assert.True(oauth.UsePkce);
        Assert.Equal("/signin-tuvima-google", oidc.CallbackPath);
        Assert.Equal("/signin-tuvima-github", oauth.CallbackPath);
    }
}
