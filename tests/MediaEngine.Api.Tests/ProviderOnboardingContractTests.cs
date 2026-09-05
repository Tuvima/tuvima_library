using System.Text.Json;
using MediaEngine.Api.Endpoints;
using MediaEngine.Domain.Configuration;

namespace MediaEngine.Api.Tests;

public sealed class ProviderOnboardingContractTests
{
    [Fact]
    public void EveryShippedProvider_DeclaresAnOnboardingClassification()
    {
        var providerDirectory = Path.Combine(FindRepoRoot(), "config", "providers");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        foreach (var path in Directory.EnumerateFiles(providerDirectory, "*.json"))
        {
            var provider = JsonSerializer.Deserialize<ProviderConfiguration>(File.ReadAllText(path), options)!;
            Assert.NotNull(provider.Onboarding);
            Assert.Contains(
                provider.Onboarding.Classification,
                new[] { "built_in", "recommended", "optional" });
        }
    }

    [Fact]
    public void TmdbCatalogueEntry_IsACompleteProviderNeutralOnboardingContract()
    {
        var providerPath = Path.Combine(FindRepoRoot(), "config", "providers", "tmdb.json");
        var provider = JsonSerializer.Deserialize<ProviderConfiguration>(
            File.ReadAllText(providerPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var entry = ProviderCatalogueEndpoints.MapToEntry(provider);

        Assert.NotNull(entry.Onboarding);
        Assert.Equal("recommended", entry.Onboarding.Classification);
        Assert.Equal(["watch"], entry.Onboarding.SupportedLanes);
        Assert.NotEmpty(entry.Onboarding.SkipConsequences);
        Assert.Equal("Connect TMDB", entry.Onboarding.Intro?.Title);
        Assert.Equal(["account", "credential", "connect"], entry.Onboarding.Steps.Select(step => step.Id));
        Assert.Equal("external_link", entry.Onboarding.Steps[0].Action?.Kind);
        Assert.Contains(entry.Onboarding.Troubleshooting, item => item.Status == "invalid_credential");
        var credential = Assert.Single(entry.Onboarding.Credentials);
        Assert.Equal("api_key", credential.Key);
        Assert.Equal("TMDB API Key (v3 auth)", credential.Label);
        Assert.False(credential.Configured);
        Assert.Equal("user_supplied", credential.Ownership);
        Assert.Equal("api_key", credential.Purpose);

        var wireJson = JsonSerializer.Serialize(entry);
        Assert.DoesNotContain("validation_pattern", wireJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("^[A-Fa-f0-9]{32}$", wireJson, StringComparison.Ordinal);
    }

    [Fact]
    public void FanartCatalogue_SeparatesApplicationProjectKeyFromPersonalClientKey()
    {
        var providerPath = Path.Combine(FindRepoRoot(), "config", "providers", "fanart_tv.json");
        var provider = JsonSerializer.Deserialize<ProviderConfiguration>(
            File.ReadAllText(providerPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var entry = ProviderCatalogueEndpoints.MapToEntry(provider);

        Assert.Equal("https://webservice.fanart.tv/v3.2", provider.Endpoints["api"]);
        Assert.Equal("application_managed", entry.Onboarding!.Credentials.Single(field => field.Key == "api_key").Ownership);
        Assert.Equal("api_key", entry.Onboarding.Credentials.Single(field => field.Key == "api_key").Purpose);
        Assert.Equal("user_supplied", entry.Onboarding.Credentials.Single(field => field.Key == "client_key").Ownership);
        Assert.Equal("client_key", entry.Onboarding.Credentials.Single(field => field.Key == "client_key").Purpose);
        Assert.Equal(["client_key"], entry.Onboarding.Steps.Single(step => step.Id == "credential").CredentialKeys);

        var wireJson = JsonSerializer.Serialize(entry);
        Assert.DoesNotContain("validation_pattern", wireJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http_client", wireJson, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
