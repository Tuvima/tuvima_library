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
        var credential = Assert.Single(entry.Onboarding.Credentials);
        Assert.Equal("api_key", credential.Key);
        Assert.Equal("TMDB API Key (v3 auth)", credential.Label);
        Assert.False(credential.Configured);

        var wireJson = JsonSerializer.Serialize(entry);
        Assert.DoesNotContain("validation_pattern", wireJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("^[A-Fa-f0-9]{32}$", wireJson, StringComparison.Ordinal);
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
