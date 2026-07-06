namespace MediaEngine.Providers.Tests;

public sealed class WikidataBridgeWorkerProgressTests
{
    [Fact]
    public void BridgeOperationStaysRunningThroughPropertyFetchAndPostPipeline()
    {
        var source = File.ReadAllText(GetRepoFilePath(
            @"src\MediaEngine.Providers\Workers\WikidataBridgeWorker.cs"));
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        var successCall = "await MarkBridgeSucceededAsync(ctx.Operation, job, ctx.ResolvedQid, ct).ConfigureAwait(false);";
        var qidResolvedIndex = normalized.IndexOf(
            "await _jobRepo.UpdateStateAsync(job.Id, IdentityJobState.QidResolved, ct: ct);",
            StringComparison.Ordinal);
        var fetchStageIndex = normalized.IndexOf(
            "Fetching full Wikidata properties.",
            StringComparison.Ordinal);
        var postPipelineIndex = normalized.IndexOf(
            "await _postPipeline.EvaluateAndOrganizeAsync(\n                job.EntityId, job.Id, ctx.ResolvedQid, job.IngestionRunId, ct);",
            fetchStageIndex >= 0 ? fetchStageIndex : 0,
            StringComparison.Ordinal);
        var successIndex = normalized.IndexOf(
            successCall,
            postPipelineIndex >= 0 ? postPipelineIndex : 0,
            StringComparison.Ordinal);

        Assert.True(qidResolvedIndex >= 0);
        Assert.True(fetchStageIndex > qidResolvedIndex);
        Assert.True(postPipelineIndex > fetchStageIndex);
        Assert.True(successIndex > postPipelineIndex);
        Assert.Contains("Persisting Wikidata claims.", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeFinalisationDoesNotBlockOnInlinePersonEnrichment()
    {
        var source = File.ReadAllText(GetRepoFilePath(
            @"src\MediaEngine.Providers\Workers\WikidataBridgeWorker.cs"));
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        var fullClaimBlockStart = normalized.IndexOf(
            "if (fullClaims.Count > 0)",
            StringComparison.Ordinal);
        var manifestIndex = normalized.IndexOf(
            "await TryHydrateSeriesManifestAsync(job, ctx, lineage, ctx.ResolvedQid, fullClaims, ct);",
            StringComparison.Ordinal);

        Assert.True(fullClaimBlockStart >= 0);
        Assert.True(manifestIndex > fullClaimBlockStart);

        var bridgeCriticalPath = normalized[fullClaimBlockStart..manifestIndex];
        Assert.DoesNotContain("RunPostIdentityPersonPassAsync(", bridgeCriticalPath, StringComparison.Ordinal);
        Assert.Contains("Quick hydration runs the people pass after QID resolution.", bridgeCriticalPath, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MediaEngine.slnx")))
            dir = dir.Parent;

        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Repo root not found."), relativePath);
    }
}
