using System.Xml.Linq;

namespace MediaEngine.Api.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void Domain_DoesNotReferenceInfrastructureOrUiProjects()
    {
        var refs = ProjectReferences("src", "MediaEngine.Domain", "MediaEngine.Domain.csproj");
        Assert.Empty(refs);
    }

    [Fact]
    public void Application_DoesNotReferenceInfrastructureOrUiProjects()
    {
        var refs = ProjectReferences("src", "MediaEngine.Application", "MediaEngine.Application.csproj");
        var forbidden = refs
            .Where(reference => ForbiddenApplicationReferences.Any(forbidden =>
                reference.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void Web_DoesNotReferenceStorage()
    {
        var refs = ProjectReferences("src", "MediaEngine.Web", "MediaEngine.Web.csproj");
        Assert.DoesNotContain(refs, reference =>
            reference.Contains("MediaEngine.Storage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PhaseOneEndpointFiles_DoNotContainDirectSql()
    {
        var repoRoot = FindRepoRoot();
        string[] endpointFiles =
        [
            Path.Combine(repoRoot, "src", "MediaEngine.Api", "Endpoints", "ProgressEndpoints.cs"),
            Path.Combine(repoRoot, "src", "MediaEngine.Api", "Endpoints", "PersonEndpoints.cs"),
            Path.Combine(repoRoot, "src", "MediaEngine.Api", "Endpoints", "IngestionEndpoints.cs"),
        ];

        var offenders = endpointFiles
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .Where(file => DirectSqlPatterns.Any(pattern =>
                file.Text.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            .Select(file => Path.GetRelativePath(repoRoot, file.Path))
            .ToList();

        Assert.Empty(offenders);
    }

    private static IReadOnlyList<string> ProjectReferences(params string[] projectPathParts)
    {
        var repoRoot = FindRepoRoot();
        var projectPath = Path.Combine([repoRoot, .. projectPathParts]);
        var document = XDocument.Load(projectPath);
        return document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MediaEngine.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private static readonly string[] ForbiddenApplicationReferences =
    [
        "MediaEngine.Storage",
        "MediaEngine.Api",
        "MediaEngine.Web",
        "MediaEngine.Providers",
        "MediaEngine.Ingestion",
        "MediaEngine.Processors",
        "MediaEngine.AI",
    ];

    private static readonly string[] DirectSqlPatterns =
    [
        "CreateCommand(",
        "CommandText",
        "SELECT ",
        "INSERT ",
        "UPDATE ",
        "DELETE ",
    ];
}
