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
    public void ActiveRazorComponents_DoNotImportStorageImplementationModelsOrSql()
    {
        var repoRoot = FindRepoRoot();
        var componentRoot = Path.Combine(repoRoot, "src", "MediaEngine.Web", "Components");
        var offenders = Directory.EnumerateFiles(componentRoot, "*.razor", SearchOption.AllDirectories)
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .Where(file =>
                file.Text.Contains("@using MediaEngine.Storage.Models", StringComparison.Ordinal)
                || file.Text.Contains("CreateCommand(", StringComparison.Ordinal)
                || file.Text.Contains("CommandText", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(repoRoot, file.Path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ApiEndpointFiles_WithDirectDatabaseAccess_AreExplicitlyTracked()
    {
        var repoRoot = FindRepoRoot();
        var endpointRoot = Path.Combine(repoRoot, "src", "MediaEngine.Api", "Endpoints");
        var offenders = Directory.EnumerateFiles(endpointRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("IDatabaseConnection", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .Except(EndpointDatabaseAccessAllowlist, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ApiEndpointFiles_OutsideLegacyAllowlist_DoNotContainDirectSql()
    {
        var repoRoot = FindRepoRoot();
        var endpointRoot = Path.Combine(repoRoot, "src", "MediaEngine.Api", "Endpoints");
        var offenders = Directory.EnumerateFiles(endpointRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !EndpointDatabaseAccessAllowlist.Contains(
                Path.GetRelativePath(repoRoot, path).Replace('\\', '/'),
                StringComparer.OrdinalIgnoreCase))
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .Where(file => ContainsDirectSql(file.Text))
            .Select(file => Path.GetRelativePath(repoRoot, file.Path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void LegacyEndpointDatabaseAllowlist_DoesNotIncludeMigratedFiles()
    {
        Assert.DoesNotContain(
            "src/MediaEngine.Api/Endpoints/SystemEndpoints.cs",
            EndpointDatabaseAccessAllowlist,
            StringComparer.OrdinalIgnoreCase);
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
            .Where(file => ContainsDirectSql(file.Text))
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
        {
            dir = dir.Parent;
        }

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

    private static bool ContainsDirectSql(string text)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimStart();
            if (line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Contains(".WithSummary(", StringComparison.Ordinal)
                || line.Contains(".WithDescription(", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Contains("CreateCommand(", StringComparison.Ordinal)
                || line.Contains("CommandText", StringComparison.Ordinal)
                || DirectSqlStatementRegex.IsMatch(line))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly System.Text.RegularExpressions.Regex DirectSqlStatementRegex =
        new(@"\b(SELECT|INSERT|UPDATE|DELETE)\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly string[] EndpointDatabaseAccessAllowlist =
    [
        // TODO Wave 7+: legacy endpoint SQL debt. Keep this list shrinking; do not add new files.
        "src/MediaEngine.Api/Endpoints/CharacterEndpoints.cs",
        "src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs",
        "src/MediaEngine.Api/Endpoints/ItemCanonicalEndpoints.cs",
        "src/MediaEngine.Api/Endpoints/LibraryEndpoints.cs",
        "src/MediaEngine.Api/Endpoints/LibraryItemEndpoints.cs",
        "src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs",
        "src/MediaEngine.Api/Endpoints/MetadataEndpoints.MediaEditorNavigator.cs",
        "src/MediaEngine.Api/Endpoints/PersonCreditQueries.cs",
        "src/MediaEngine.Api/Endpoints/PersonEndpoints.cs",
        "src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs",
        "src/MediaEngine.Api/Endpoints/UniverseGraphEndpoints.cs",
        "src/MediaEngine.Api/Endpoints/WorkEndpoints.cs",
    ];
}
