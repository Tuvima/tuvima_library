using MediaEngine.Api.Services;
using MediaEngine.Domain.Contracts;
using MediaEngine.Providers.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MediaEngine.Api.Tests;

public sealed class ServiceLifecycleRegistrationTests
{
    [Fact]
    public void MetadataHarvestQueue_AdmissionResolvesToTheHostedWorkersQueueInstance()
    {
        var services = new ServiceCollection();
        services.AddSingleton<MetadataHarvestQueue>();
        services.AddSingleton<IMetadataHarvestQueueAdmission>(provider =>
            provider.GetRequiredService<MetadataHarvestQueue>());
        using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<MetadataHarvestQueue>(),
            provider.GetRequiredService<IMetadataHarvestQueueAdmission>());

        var program = ReadSource("src/MediaEngine.Api/Program.cs");
        Assert.Contains("AddSingleton<MetadataHarvestQueue>()", program, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<MetadataHarvestQueue>()", program, StringComparison.Ordinal);
        Assert.Contains("AddHostedService(sp =>", program, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<MetadataHarvestingService>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void RelationshipPopulation_UsesExplicitAdmissionAndGraphDependencies()
    {
        var constructor = Assert.Single(typeof(RelationshipPopulationService).GetConstructors());
        var parameterTypes = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        Assert.Contains(typeof(IMetadataHarvestQueueAdmission), parameterTypes);
        Assert.Contains(typeof(IUniverseGraphQueryService), parameterTypes);
        Assert.DoesNotContain(typeof(IServiceProvider), parameterTypes);

        var source = ReadSource("src/MediaEngine.Providers/Services/RelationshipPopulationService.cs");
        Assert.DoesNotContain("IServiceProvider", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderHealthRecovery_UsesScopeFactoryInsteadOfRootServiceProvider()
    {
        var constructor = Assert.Single(typeof(ProviderHealthMonitorService).GetConstructors());
        var parameterTypes = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        Assert.Contains(typeof(IServiceScopeFactory), parameterTypes);
        Assert.DoesNotContain(typeof(IServiceProvider), parameterTypes);

        var source = ReadSource("src/MediaEngine.Api/Services/ProviderHealthMonitorService.cs");
        Assert.Contains("_scopeFactory.CreateScope()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_serviceProvider.CreateScope()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupConfiguration_DoesNotSilentlyReplaceInvalidCoreOrScoringConfiguration()
    {
        var program = ReadSource("src/MediaEngine.Api/Program.cs");

        Assert.DoesNotContain("catch { /* non-fatal", program, StringComparison.Ordinal);
        Assert.DoesNotContain("catch { s = new MediaEngine.Storage.Models.ScoringSettings(); }", program, StringComparison.Ordinal);
        Assert.Contains("var s = loader.LoadScoring();", program, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
