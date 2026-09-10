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
        var applicationCredential = entry.Onboarding.Credentials.Single(field => field.Key == "api_key");
        Assert.Equal("TMDB API Key (v3 auth)", applicationCredential.Label);
        Assert.False(applicationCredential.Configured);
        Assert.Equal("application_managed", applicationCredential.Ownership);
        Assert.Equal("api_key", applicationCredential.Purpose);
        var overrideCredential = entry.Onboarding.Credentials.Single(field => field.Key == "api_key_override");
        Assert.False(overrideCredential.Required);
        Assert.Equal("user_supplied", overrideCredential.Ownership);
        Assert.Contains("api_key_override", entry.Onboarding.Steps.Single(step => step.Id == "credential").CredentialKeys);

        var wireJson = JsonSerializer.Serialize(entry);
        Assert.DoesNotContain("validation_pattern", wireJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("^[A-Fa-f0-9]{32}$", wireJson, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenSubtitlesCatalogue_ExposesOptionalAccountLoginWithoutEmbeddedCredentials()
    {
        var providerPath = Path.Combine(FindRepoRoot(), "config", "providers", "opensubtitles.json");
        var provider = JsonSerializer.Deserialize<ProviderConfiguration>(
            File.ReadAllText(providerPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var entry = ProviderCatalogueEndpoints.MapToEntry(provider);

        Assert.Equal("optional", entry.Onboarding!.Classification);
        Assert.Equal("user_supplied", entry.Onboarding.Credentials.Single(field => field.Key == "api_key").Ownership);
        Assert.False(entry.Onboarding.Credentials.Single(field => field.Key == "username").Required);
        Assert.False(entry.Onboarding.Credentials.Single(field => field.Key == "password").Required);
        Assert.Equal(["username", "password"], entry.Onboarding.Steps.Single(step => step.Id == "account_login").CredentialKeys);
        Assert.DoesNotContain("application_managed", entry.Onboarding.Credentials.Select(field => field.Ownership));
    }
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git"))
                || File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
