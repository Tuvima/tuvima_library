using MediaEngine.Api.Endpoints;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Api.Tests;

public sealed class UniverseGraphEndpointTests
{
    [Fact]
    public void EgoNetwork_PreservesRequestedDepthAndBothEdgeDirections()
    {
        var qids = new HashSet<string>(["Q1", "Q2", "Q3", "Q4"], StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<EntityRelationship> edges =
        [
            Edge("Q1", "Q2"),
            Edge("Q3", "Q2"),
            Edge("Q4", "Q3"),
            Edge("Q1", "Q-outside"),
        ];

        Assert.Equal(["Q1"], UniverseGraphEndpoints.BuildEgoNetwork("Q1", 0, qids, edges).Order().ToArray());
        Assert.Equal(["Q1", "Q2"], UniverseGraphEndpoints.BuildEgoNetwork("Q1", 1, qids, edges).Order().ToArray());
        Assert.Equal(["Q1", "Q2", "Q3"], UniverseGraphEndpoints.BuildEgoNetwork("Q1", 2, qids, edges).Order().ToArray());
        Assert.Equal(["Q1", "Q2", "Q3", "Q4"], UniverseGraphEndpoints.BuildEgoNetwork("Q1", 3, qids, edges).Order().ToArray());
    }

    [Fact]
    public void GraphRoute_UsesBatchedReadsInsteadOfPerNodeRepositoryCalls()
    {
        var source = File.ReadAllText(GetRepoFilePath(
            @"src\MediaEngine.Api\Endpoints\UniverseGraphEndpoints.cs"));
        var start = source.IndexOf("group.MapGet(\"/universe/{qid}/graph\"", StringComparison.Ordinal);
        var end = source.IndexOf("group.MapPost(\"/universe/entity/{qid}/deep-enrich\"", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);

        var graphRoute = source[start..end];
        Assert.Contains("LoadVisibleUniverseScopeAsync(", graphRoute, StringComparison.Ordinal);
        Assert.Contains("entityRepo.GetWorkLinksAsync(", source, StringComparison.Ordinal);
        Assert.Contains("allEntities.Select(entity => entity.Id)", source, StringComparison.Ordinal);
        Assert.Contains("visibleWorkQids.Contains(link.WorkQid)", source, StringComparison.Ordinal);
        Assert.Contains("eraActorResolver.ResolveActorsForEraAsync(", graphRoute, StringComparison.Ordinal);
        Assert.Contains("BuildEgoNetwork(center, maxDepth, entityQids, relationships)", graphRoute, StringComparison.Ordinal);
        Assert.Contains("ApiImageUrls.BuildPersonHeadshotUrl(", graphRoute, StringComparison.Ordinal);
        Assert.DoesNotContain("GetWorkLinksAsync(entity.Id", graphRoute, StringComparison.Ordinal);
        Assert.DoesNotContain("relRepo.GetByEntityAsync", graphRoute, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveActorForEraAsync", graphRoute, StringComparison.Ordinal);
    }

    [Fact]
    public void VisibleUniverseEntitiesRequireProvenanceFromAnAuthorizedWork()
    {
        var visible = new FictionalEntity { Id = Guid.NewGuid(), WikidataQid = "Q1" };
        var withheld = new FictionalEntity { Id = Guid.NewGuid(), WikidataQid = "Q2" };
        IReadOnlyList<MediaEngine.Domain.Contracts.FictionalEntityWorkLink> visibleLinks =
        [
            new(visible.Id, "Q100", "Visible work", "appears_in"),
        ];

        var filtered = UniverseGraphEndpoints.FilterVisibleEntities(
            [visible, withheld], visibleLinks);

        Assert.Equal(visible.Id, Assert.Single(filtered).Id);
    }

    private static EntityRelationship Edge(string subject, string target) => new()
    {
        Id = Guid.NewGuid(),
        SubjectQid = subject,
        ObjectQid = target,
        RelationshipTypeValue = "member_of",
        DiscoveredAt = DateTimeOffset.UtcNow,
    };

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
