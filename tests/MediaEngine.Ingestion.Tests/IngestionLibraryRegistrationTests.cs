using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.DependencyInjection;
using MediaEngine.Ingestion.Tests.Helpers;
using MediaEngine.Processors;
using MediaEngine.Processors.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MediaEngine.Ingestion.Tests;

public sealed class IngestionLibraryRegistrationTests
{
    [Fact]
    public void IngestionProject_IsInternalLibraryWithoutStandaloneHostFiles()
    {
        string projectPath = GetRepoFilePath(@"src\MediaEngine.Ingestion\MediaEngine.Ingestion.csproj");
        string project = File.ReadAllText(projectPath);

        Assert.Contains("Sdk=\"Microsoft.NET.Sdk\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.NET.Sdk.Worker", project, StringComparison.Ordinal);
        Assert.False(File.Exists(GetRepoFilePath(@"src\MediaEngine.Ingestion\Program.cs")));
        Assert.False(File.Exists(GetRepoFilePath(@"src\MediaEngine.Ingestion\appsettings.json")));
    }

    [Fact]
    public async Task AddMediaEngineIngestion_RegistersCorePipelineServicesForEngineHost()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IVideoMetadataExtractor, StubVideoMetadataExtractor>();

        services.AddMediaEngineIngestion(
            new ConfigurationBuilder().Build(),
            new StubConfigurationLoader(),
            options =>
            {
                options.ConfigureOptions = false;
                options.CreateConfiguredDirectories = false;
            });

        Assert.Contains(services, d => d.ServiceType == typeof(IIngestionEngine));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
        Assert.Contains(services, d => d.ServiceType == typeof(IFileWatcher));
        Assert.Contains(services, d => d.ServiceType == typeof(IBackgroundWorker));
        Assert.Contains(services, d => d.ServiceType == typeof(IProcessorRouter));

        await using ServiceProvider provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IFileWatcher>());
        Assert.NotNull(provider.GetRequiredService<IBackgroundWorker>());
        Assert.NotNull(provider.GetRequiredService<IProcessorRouter>());
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
