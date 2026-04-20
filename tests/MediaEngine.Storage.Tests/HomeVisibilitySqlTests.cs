using MediaEngine.Storage;

namespace MediaEngine.Storage.Tests;

public sealed class HomeVisibilitySqlTests
{
    [Fact]
    public void VisibleAssetPathPredicate_ExcludesStagingAndQuarantineRoots()
    {
        var predicate = HomeVisibilitySql.VisibleAssetPathPredicate("ma.file_path_root");

        Assert.Contains("NOT LIKE '%/.data/staging/%'", predicate, StringComparison.Ordinal);
        Assert.Contains("NOT LIKE '%\\.data\\staging\\%'", predicate, StringComparison.Ordinal);
        Assert.Contains("NOT LIKE '%/quarantine/%'", predicate, StringComparison.Ordinal);
        Assert.Contains("NOT LIKE '%\\quarantine\\%'", predicate, StringComparison.Ordinal);
    }

    [Fact]
    public void VisibleWorkPredicate_ExcludesReviewRejectedAndCatalogOnlyStates()
    {
        var predicate = HomeVisibilitySql.VisibleWorkPredicate("w.id", "w.curator_state", "w.is_catalog_only");

        Assert.Contains("NOT IN ('rejected', 'provisional')", predicate, StringComparison.Ordinal);
        Assert.Contains("FROM review_queue rq", predicate, StringComparison.Ordinal);
        Assert.Contains("rq.status = 'Pending'", predicate, StringComparison.Ordinal);
        Assert.Contains("rq.trigger != 'WritebackFailed'", predicate, StringComparison.Ordinal);
        Assert.Contains("FROM identity_jobs ij", predicate, StringComparison.Ordinal);
        Assert.Contains("'QidNeedsReview', 'RetailMatchedNeedsReview'", predicate, StringComparison.Ordinal);
        Assert.Contains("COALESCE(w.is_catalog_only, 0) = 0", predicate, StringComparison.Ordinal);
        Assert.Contains("ma_v.file_path_root", predicate, StringComparison.Ordinal);
        Assert.Contains("NOT LIKE '%/quarantine/%'", predicate, StringComparison.Ordinal);
    }
}
