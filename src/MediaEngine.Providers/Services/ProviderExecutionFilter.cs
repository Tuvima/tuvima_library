using MediaEngine.Providers.Contracts;
using MediaEngine.Storage.Models;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Separates provider catalogue visibility from runtime execution.
/// </summary>
public static class ProviderExecutionFilter
{
    public static bool IsEnabled(
        string providerName,
        IReadOnlyList<ProviderConfiguration> providerConfigs)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return false;

        return providerConfigs
            .FirstOrDefault(config => string.Equals(
                config.Name,
                providerName,
                StringComparison.OrdinalIgnoreCase))
            ?.Enabled == true;
    }

    public static bool IsEnabled(
        IExternalMetadataProvider provider,
        IReadOnlyList<ProviderConfiguration> providerConfigs) =>
        IsEnabled(provider.Name, providerConfigs);

    public static IReadOnlyList<IExternalMetadataProvider> EnabledProviders(
        IEnumerable<IExternalMetadataProvider> providers,
        IReadOnlyList<ProviderConfiguration> providerConfigs) =>
        providers
            .Where(provider => IsEnabled(provider, providerConfigs))
            .ToList();

    public static IExternalMetadataProvider? FindEnabledProvider(
        IEnumerable<IExternalMetadataProvider> providers,
        IReadOnlyList<ProviderConfiguration> providerConfigs,
        string providerName) =>
        providers.FirstOrDefault(provider =>
            string.Equals(provider.Name, providerName, StringComparison.OrdinalIgnoreCase)
            && IsEnabled(provider, providerConfigs));

    public static IReadOnlyList<string> EnabledProviderNames(
        IEnumerable<string> providerNames,
        IEnumerable<IExternalMetadataProvider> providers,
        IReadOnlyList<ProviderConfiguration> providerConfigs)
    {
        var registered = providers
            .Select(provider => provider.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return providerNames
            .Where(name => registered.Contains(name))
            .Where(name => IsEnabled(name, providerConfigs))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
