namespace MediaEngine.Api.Tests;

public sealed class DockerfileGuardrailTests
{
    [Fact]
    public void Dockerfile_CopiesReferencedProjectsBeforeRestore()
    {
        var repoRoot = FindRepoRoot();
        var dockerfile = File.ReadAllText(Path.Combine(repoRoot, "Dockerfile"));

        var restoreIndex = dockerfile.IndexOf("RUN dotnet restore", StringComparison.Ordinal);
        Assert.True(restoreIndex > 0, "Dockerfile should restore after copying project files.");

        string[] requiredProjects =
        [
            "src/MediaEngine.Contracts/MediaEngine.Contracts.csproj",
            "src/MediaEngine.Domain/MediaEngine.Domain.csproj",
            "src/MediaEngine.Storage/MediaEngine.Storage.csproj",
            "src/MediaEngine.Intelligence/MediaEngine.Intelligence.csproj",
            "src/MediaEngine.Processors/MediaEngine.Processors.csproj",
            "src/MediaEngine.Providers/MediaEngine.Providers.csproj",
            "src/MediaEngine.Ingestion/MediaEngine.Ingestion.csproj",
            "src/MediaEngine.Identity/MediaEngine.Identity.csproj",
            "src/MediaEngine.AI/MediaEngine.AI.csproj",
            "src/MediaEngine.Plugins/MediaEngine.Plugins.csproj",
            "src/MediaEngine.Plugin.CommercialSkip/MediaEngine.Plugin.CommercialSkip.csproj",
            "src/MediaEngine.Plugin.FandomLore/MediaEngine.Plugin.FandomLore.csproj",
            "src/MediaEngine.Plugin.MediaSegments/MediaEngine.Plugin.MediaSegments.csproj",
            "src/MediaEngine.Api/MediaEngine.Api.csproj",
            "src/MediaEngine.Web/MediaEngine.Web.csproj",
        ];

        foreach (var project in requiredProjects)
        {
            var copyIndex = dockerfile.IndexOf(project, StringComparison.Ordinal);
            Assert.True(copyIndex >= 0, $"Dockerfile does not copy {project}.");
            Assert.True(copyIndex < restoreIndex, $"{project} must be copied before restore.");
        }

        Assert.Contains("global.json", dockerfile);
        Assert.Contains("nuget.config", dockerfile);
    }

    [Fact]
    public void Entrypoint_UsesReadinessLoopInsteadOfFixedSleep()
    {
        var repoRoot = FindRepoRoot();
        var entrypoint = File.ReadAllText(Path.Combine(repoRoot, "docker-entrypoint.sh"));

        Assert.DoesNotContain("sleep 3", entrypoint);
        Assert.Contains("Waiting for Engine readiness", entrypoint);
        Assert.Contains("/dev/tcp/127.0.0.1/61495", entrypoint);
        Assert.Contains("Engine exited before becoming ready", entrypoint);
    }

    [Fact]
    public void ContainerFirstRun_SeedsPlatformSpecificLibraryPaths()
    {
        var repoRoot = FindRepoRoot();
        var dockerfile = File.ReadAllText(Path.Combine(repoRoot, "Dockerfile"));
        var entrypoint = File.ReadAllText(Path.Combine(repoRoot, "docker-entrypoint.sh"));
        var core = File.ReadAllText(Path.Combine(repoRoot, "docker", "config", "core.json"));
        var libraries = File.ReadAllText(Path.Combine(repoRoot, "docker", "config", "libraries.json"));

        Assert.Contains("COPY docker/config/ ./docker-config/", dockerfile);
        Assert.Contains("cp /app/docker-config/core.json /config/core.json", entrypoint);
        Assert.Contains("cp /app/docker-config/libraries.json /config/libraries.json", entrypoint);
        Assert.DoesNotContain("TUVIMA_WATCH_FOLDER", entrypoint);
        Assert.Contains("\"library_root\": \"/library\"", core);
        Assert.Contains("\"source_paths\": [\"/watch/books\"]", libraries);
        Assert.DoesNotContain(@"C:\temp", core);
        Assert.DoesNotContain(@"C:\temp", libraries);
    }

    [Fact]
    public void NuGetConfig_UsesPortablePublicPackageSource()
    {
        var repoRoot = FindRepoRoot();
        var nugetConfig = File.ReadAllText(Path.Combine(repoRoot, "nuget.config"));

        Assert.Contains("https://api.nuget.org/v3/index.json", nugetConfig);
        Assert.DoesNotContain("tuvima-wikidata-local", nugetConfig);
        Assert.DoesNotContain(@"C:\Users\", nugetConfig);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MediaEngine.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
