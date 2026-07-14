using MediaEngine.Domain.Contracts;
using MediaEngine.Providers.Services;
using Microsoft.Extensions.Hosting;

namespace MediaEngine.Api.Tests;

public sealed class DeferredEnrichmentHostingTests
{
    [Fact]
    public void DeferredEnrichmentService_UsesTheHostedServiceLifecycle()
    {
        Assert.True(typeof(IHostedService).IsAssignableFrom(typeof(DeferredEnrichmentService)));
        Assert.True(typeof(IDeferredEnrichmentService).IsAssignableFrom(typeof(DeferredEnrichmentService)));
    }

    [Fact]
    public void EngineRegistration_UsesTheSameSingletonForCommandsAndHostedExecution()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Program.cs"));

        Assert.Contains("AddSingleton<DeferredEnrichmentService>()", source, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<DeferredEnrichmentService>()", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AddSingleton<IDeferredEnrichmentService,    DeferredEnrichmentService>()",
            source,
            StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {AppContext.BaseDirectory}");
    }
}
