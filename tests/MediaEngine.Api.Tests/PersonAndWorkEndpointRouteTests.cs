using System.IO;

namespace MediaEngine.Api.Tests;

public sealed class PersonAndWorkEndpointRouteTests
{
    [Fact]
    public void PersonEndpoints_ExposeRichPersonShellAndLibraryCredits()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\PersonEndpoints.cs"));

        Assert.Contains("group.MapGet(\"/{id:guid}/library-credits\"", source, StringComparison.Ordinal);
        Assert.Contains("PersonCreditQueries.GetLibraryCreditsAsync", source, StringComparison.Ordinal);
        Assert.Contains("group_members", source, StringComparison.Ordinal);
        Assert.Contains("member_of_groups", source, StringComparison.Ordinal);
        Assert.Contains("banner_url", source, StringComparison.Ordinal);
        Assert.Contains("background_url", source, StringComparison.Ordinal);
        Assert.Contains("logo_url", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CharacterRoleQuery_UsesStoredWorkQidInsteadOfInferringFirstWork()
    {
        var endpointSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\CharacterEndpoints.cs"));
        var querySource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\PersonCreditQueries.cs"));

        Assert.Contains("group.MapGet(\"/persons/{personId:guid}/character-roles\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/portraits/{portraitId:guid}\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("ApiImageUrls.BuildCharacterPortraitUrl", endpointSource, StringComparison.Ordinal);
        Assert.Contains("PersonCreditQueries.GetCharacterRolesAsync", endpointSource, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN works w", querySource, StringComparison.Ordinal);
        Assert.Contains("ON cpl.work_qid = COALESCE(", querySource, StringComparison.Ordinal);
        Assert.Contains("NULLIF(TRIM(w.wikidata_qid), '')", querySource, StringComparison.Ordinal);
        Assert.Contains("cpl.work_qid IS NOT NULL", querySource, StringComparison.Ordinal);
        Assert.Contains("ApiImageUrls.BuildCharacterPortraitUrl", querySource, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkAndCollectionCastEndpoints_ShareTheSameCastCreditShape()
    {
        var workEndpointSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\WorkEndpoints.cs"));
        var collectionEndpointSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\CollectionEndpoints.cs"));
        var dtoSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Models\CollectionGroupDetailDto.cs"));
        var programSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Program.cs"));

        Assert.Contains("group.MapGet(\"/{workId:guid}/cast\"", workEndpointSource, StringComparison.Ordinal);
        Assert.Contains("CastCreditQueries.BuildForWorkAsync", workEndpointSource, StringComparison.Ordinal);
        Assert.Contains("CastCreditQueries.BuildForCollectionRootAsync", collectionEndpointSource, StringComparison.Ordinal);
        Assert.Contains("public List<CastCreditDto> TopCast { get; init; } = [];", dtoSource, StringComparison.Ordinal);
        Assert.Contains("app.MapWorkEndpoints();", programSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CastCreditQuery_QualifiesWorkIdentityColumns()
    {
        var querySource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\PersonCreditQueries.cs"));

        Assert.Contains("SELECT w.id AS WorkId", querySource, StringComparison.Ordinal);
        Assert.Contains(")                                      AS WorkQid", querySource, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT id AS WorkId", querySource, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkEndpoints_ExposeReadOnlyWorkAndEditionIdentitySurfaces()
    {
        var workEndpointSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\WorkEndpoints.cs"));
        var dtoSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Models\Dtos.cs"));

        Assert.Contains("group.MapGet(\"/{workId:guid}\", async (", workEndpointSource, StringComparison.Ordinal);
        Assert.Contains(".WithName(\"GetWorkDetail\")", workEndpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/{workId:guid}/editions\", async (", workEndpointSource, StringComparison.Ordinal);
        Assert.Contains(".WithName(\"GetWorkEditions\")", workEndpointSource, StringComparison.Ordinal);
        Assert.Contains("public sealed class WorkDetailDto", dtoSource, StringComparison.Ordinal);
        Assert.Contains("public sealed class EditionDto", dtoSource, StringComparison.Ordinal);
        Assert.Contains("public sealed class EditionAssetDto", dtoSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PersonAndUniverseEndpoints_PreferLocalHeadshotRoutes()
    {
        var personSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\PersonEndpoints.cs"));
        var graphSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\Endpoints\UniverseGraphEndpoints.cs"));

        Assert.Contains("ApiImageUrls.BuildPersonHeadshotUrl", personSource, StringComparison.Ordinal);
        Assert.Contains("ApiImageUrls.BuildPersonHeadshotUrl", graphSource, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
