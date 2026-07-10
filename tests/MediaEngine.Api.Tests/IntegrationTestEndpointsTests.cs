using System.Reflection;
using System.Collections;
using MediaEngine.Api.DevSupport;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;

namespace MediaEngine.Api.Tests;

public sealed class IntegrationTestEndpointsTests : IDisposable
{
    private readonly string _tempRoot;

    public IntegrationTestEndpointsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"tuvima_integration_validator_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void Program_MapsIntegrationTestEndpointsOnlyInDevelopment()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\DependencyInjection\ApiEndpointRouteBuilderExtensions.cs"));
        var developmentGuard = "if (app.Environment.IsDevelopment())";
        var guardIndex = source.IndexOf(developmentGuard, StringComparison.Ordinal);
        var mapIndex = source.IndexOf("app.MapIntegrationTestEndpoints();", StringComparison.Ordinal);

        Assert.True(guardIndex >= 0);
        Assert.True(mapIndex > guardIndex);
        Assert.DoesNotContain(
            "app.MapIntegrationTestEndpoints();",
            source[..guardIndex],
            StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationTestEndpoints_RegisterOnlyUnderDevRouteGroup()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\DevSupport\IntegrationTestEndpoints.cs"));

        Assert.Contains("app.MapGroup(\"/dev\")", source, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/integration-test\", RunIntegrationTestAsync)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("app.MapPost(\"/integration-test\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DevHarnessReset_DefaultsToGeneratedStateScope()
    {
        Assert.Equal(DevHarnessWipeScope.GeneratedState, DevHarnessResetService.ParseScope(null));
        Assert.Equal(DevHarnessWipeScope.GeneratedState, DevHarnessResetService.ParseScope("generated-state"));
        Assert.Equal(DevHarnessWipeScope.Full, DevHarnessResetService.ParseScope("full"));
    }

    [Fact]
    public void RunFullIntegration_DefaultsToGeneratedStateAndSupportsExplicitFullWipe()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"tools\Run-FullIntegration.ps1"));

        Assert.Contains("[ValidateSet(\"generated-state\", \"full\")]", source, StringComparison.Ordinal);
        Assert.Contains("[string]$WipeScope = \"generated-state\"", source, StringComparison.Ordinal);
        Assert.Contains("[int]$TimeoutSec = 10800", source, StringComparison.Ordinal);
        Assert.Contains("[switch]$MusicOnly", source, StringComparison.Ordinal);
        Assert.Contains("$Types = @(\"music\")", source, StringComparison.Ordinal);
        Assert.Contains("wipeScope=$([uri]::EscapeDataString($WipeScope))", source, StringComparison.Ordinal);
        Assert.Contains("$WipeScope -eq \"full\"", source, StringComparison.Ordinal);
        Assert.Contains("stages=$Stages", source, StringComparison.Ordinal);
        Assert.Contains("-TimeoutSec $TimeoutSec", source, StringComparison.Ordinal);
        Assert.Contains("-TimeoutSec 30", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DevHarnessReset_RecreatesConfiguredSourceFoldersAfterWipe()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\DevSupport\DevHarnessResetService.cs"));

        Assert.Contains("EnsureConfiguredSourcePathsExist(details)", source, StringComparison.Ordinal);
        Assert.Contains("EnsureConfiguredSourcePathsExist(details ?? [])", source, StringComparison.Ordinal);
        Assert.Contains("Directory.CreateDirectory(srcPath)", source, StringComparison.Ordinal);
        Assert.Contains("EnumerateConfiguredSourcePaths().Distinct", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationHarness_TracksStageAwareWaitBudgetsAndOwnedCounts()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\DevSupport\IntegrationTestEndpoints.cs"));

        Assert.Contains("ResolveIngestionWaitPlan", source, StringComparison.Ordinal);
        Assert.Contains("IngestionWaitStages", source, StringComparison.Ordinal);
        Assert.Contains("CompletedAfterTimeout", source, StringComparison.Ordinal);
        Assert.Contains("Ingestion Wait Budgets", source, StringComparison.Ordinal);
        Assert.Contains("owned, {mt.CatalogOnlyCount} catalog-only", source, StringComparison.Ordinal);
        Assert.Contains("allItems.Items.Where(IsOwnedValidationItem)", source, StringComparison.Ordinal);
        Assert.Contains("ProviderHealthTimeoutSeconds", source, StringComparison.Ordinal);
        Assert.Contains("elapsed {sw.Elapsed.TotalSeconds:F1}s", source, StringComparison.Ordinal);
        Assert.Contains("ProviderHealthDetails", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationHarness_UsesMusicBrainzAsMusicIdentityAndAppleAsEnrichment()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\DevSupport\IntegrationTestEndpoints.cs"));
        var seedSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\DevSupport\DevSeedEndpoints.cs"));

        Assert.Contains("[\"music\"] = [\"musicbrainz\", \"apple_api\"]", source, StringComparison.Ordinal);
        Assert.Contains("(\"Bohemian Rhapsody Queen\", \"musicbrainz\", \"Music identity\"", source, StringComparison.Ordinal);
        Assert.Contains("(\"Bohemian Rhapsody Queen\", \"apple_api\", \"Music enrichment\"", source, StringComparison.Ordinal);
        Assert.Contains("\"music\" => \"musicbrainz\"", source, StringComparison.Ordinal);
        Assert.Contains("ExpectedQid: \"Q11986\"", seedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationHarness_ReportShowsUtf8IssueCategoriesAndProviderProvenance()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\DevSupport\IntegrationTestEndpoints.cs"));
        var script = File.ReadAllText(GetRepoFilePath(@"tools\Run-FullIntegration.ps1"));

        Assert.Contains("text/html; charset=utf-8", source, StringComparison.Ordinal);
        Assert.Contains("File.WriteAllTextAsync(filePath, html, Encoding.UTF8, ct)", source, StringComparison.Ordinal);
        Assert.Contains("Results.Content(html, \"text/html; charset=utf-8\", Encoding.UTF8)", source, StringComparison.Ordinal);
        Assert.Contains("CategorizeIssue", source, StringComparison.Ordinal);
        Assert.Contains("Product data failure", source, StringComparison.Ordinal);
        Assert.Contains("Harness expectation drift", source, StringComparison.Ordinal);
        Assert.Contains("Provider/runtime transient", source, StringComparison.Ordinal);
        Assert.Contains("Dashboard/API tracking", source, StringComparison.Ordinal);
        Assert.Contains("Identity Provider", source, StringComparison.Ordinal);
        Assert.Contains("Enrichment Providers", source, StringComparison.Ordinal);
        Assert.Contains("Bridge Provider", source, StringComparison.Ordinal);
        Assert.Contains("Bridge ID", source, StringComparison.Ordinal);
        Assert.Contains("ArtworkCompleteness", source, StringComparison.Ordinal);
        Assert.Contains("ResolveIdentityProvider", source, StringComparison.Ordinal);
        Assert.Contains("ResolveEnrichmentProviders", source, StringComparison.Ordinal);
        Assert.Contains("ProviderMatchesExpectation", source, StringComparison.Ordinal);
        Assert.Contains("ProviderForExpectationComparison", source, StringComparison.Ordinal);
        Assert.Contains("UsesIdentityProviderExpectation", source, StringComparison.Ordinal);
        Assert.Contains("identity provider", source, StringComparison.Ordinal);
        Assert.Contains("musicbrainz_recording_id", source, StringComparison.Ordinal);
        Assert.Contains("apple_music_id", source, StringComparison.Ordinal);
        Assert.Contains("BuildHarnessTitleMediaKeys", source, StringComparison.Ordinal);
        Assert.Contains("NormalizeHarnessTitleKey", source, StringComparison.Ordinal);
        Assert.Contains("TextEncodingRepair.RepairMojibake", source, StringComparison.Ordinal);
        Assert.Contains("CharUnicodeInfo.GetUnicodeCategory", source, StringComparison.Ordinal);
        Assert.Contains("-OutFile $OutputPath", script, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationHarness_ReadsCurrentSchemaGuidBlobs()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\DevSupport\IntegrationTestEndpoints.cs"));

        Assert.Contains("TryReadCurrentGuid", source, StringComparison.Ordinal);
        Assert.Contains("GuidSql.FromDb(value)", source, StringComparison.Ordinal);
        Assert.Contains("GuidSql.ToBlob(collectionId)", source, StringComparison.Ordinal);
        Assert.Contains("entityIds = ids.Select(GuidSql.ToBlob).ToArray()", source, StringComparison.Ordinal);
        Assert.Contains("QueryAsync<WorkAssetIdRow>", source, StringComparison.Ordinal);
        Assert.Contains("QueryAsync<AssetPathRow>", source, StringComparison.Ordinal);
        Assert.Contains("QueryAsync<OptionalArtworkRow>", source, StringComparison.Ordinal);
        Assert.Contains("QueryAsync<WorkHierarchyRow>", source, StringComparison.Ordinal);
        Assert.Contains("QueryAsync<CanonicalValueRow>", source, StringComparison.Ordinal);
        Assert.Contains("QueryAsync<PreferredArtworkRow>", source, StringComparison.Ordinal);
        Assert.Contains("provider_id AS ProviderIdBlob", source, StringComparison.Ordinal);
        Assert.Contains("winning_provider_id AS WinningProviderIdBlob", source, StringComparison.Ordinal);
        Assert.Contains("GuidSql.FromDb(ProviderIdBlob)", source, StringComparison.Ordinal);
        Assert.Contains("GuidSql.FromDb(WinningProviderIdBlob)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("QueryAsync<(string WorkId, string AssetId)>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("QueryAsync<(string EntityId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.Parse(row.EntityId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.TryParse(row.EntityId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationHarness_PreflightsManifestDrivenIdentityExpectations()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\DevSupport\IntegrationTestEndpoints.cs"));
        var seedSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\DevSupport\DevSeedEndpoints.cs"));

        Assert.Contains("BuildExpectationPreflight", source, StringComparison.Ordinal);
        Assert.Contains("ExpectedIdentityStatus", seedSource, StringComparison.Ordinal);
        Assert.Contains("ExpectedResolved", source, StringComparison.Ordinal);
        Assert.Contains("ExpectedExactQid", source, StringComparison.Ordinal);
        Assert.Contains("ExpectedIdentityOnly", source, StringComparison.Ordinal);
        Assert.Contains("Preflight Expected Outcomes", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationHarness_DoesNotAcceptQidNoMatchForExpectedFixtures()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Api\DevSupport\IntegrationTestEndpoints.cs"));

        Assert.Contains("expectsIdentification", source, StringComparison.Ordinal);
        Assert.Contains("? hasQid && exactQidMatches", source, StringComparison.Ordinal);
        Assert.Contains(": hasQid || hasIdentifiedState", source, StringComparison.Ordinal);
        Assert.Contains("expected a Wikidata QID", source, StringComparison.Ordinal);
        Assert.Contains("QID optional", source, StringComparison.Ordinal);
        Assert.DoesNotContain("accepted re-check state", source, StringComparison.Ordinal);
        Assert.DoesNotContain("hasQid || retainedRetailWithoutQid", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReconciliationReport_EmitsPerFixtureQidAndProviderAlignment()
    {
        var report = new ReconciliationReport
        {
            Total = 1,
            Matched = 1,
            Items =
            [
                new ReconciliationReportItem(
                    FileName: "Dune",
                    MediaType: "Books",
                    ExpectedStatus: "Identified as Q190192",
                    ActualStatus: "Identified",
                    ExpectedQid: "Q190192",
                    ActualQid: "Q190192",
                    ExpectedProvider: "apple_api",
                    ActualProvider: "apple_api",
                    ExpectedTrigger: null,
                    ActualTrigger: null,
                    Classification: "Match",
                    Matched: true,
                    Reason: null)
            ],
        };

        var json = report.ToJson();

        Assert.Contains("\"file_name\": \"Dune\"", json, StringComparison.Ordinal);
        Assert.Contains("\"media_type\": \"Books\"", json, StringComparison.Ordinal);
        Assert.Contains("\"expected_qid\": \"Q190192\"", json, StringComparison.Ordinal);
        Assert.Contains("\"actual_qid\": \"Q190192\"", json, StringComparison.Ordinal);
        Assert.Contains("\"expected_provider\": \"apple_api\"", json, StringComparison.Ordinal);
        Assert.Contains("\"actual_provider\": \"apple_api\"", json, StringComparison.Ordinal);
        Assert.Contains("\"classification\": \"Match\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void OverallPass_FailsWhenReconciliationHasMismatches()
    {
        var report = CreateTestReport();
        var reconciliation = CreateReconciliationSummary();
        AddToListProperty(reconciliation, "Mismatches", CreateReconciliationItemResult());
        SetProperty(report, "Reconciliation", reconciliation);

        Assert.False(GetOverallPass(report));
    }

    [Fact]
    public void OverallPass_FailsWhenExpectedReconciliationCountsDrift()
    {
        var report = CreateTestReport();
        AddToListProperty(report, "IssuesFound", "Reconciliation count mismatch: expected 97 exact QID, actual 96.");

        Assert.False(GetOverallPass(report));
    }

    [Fact]
    public void OverallPass_FailsWhenARequestedMediaTypeIsSkipped()
    {
        var report = CreateTestReport();
        AddToSetProperty(report, "RequestedTypes", "music");
        AddToDictionaryProperty(report, "SkippedTypes", "music", "Provider 'apple_api' unavailable");

        Assert.False(GetOverallPass(report));
    }

    [Fact]
    public void OverallPass_IgnoresMediaTypesSkippedOnlyBecauseTheyWereNotRequested()
    {
        var report = CreateTestReport();
        AddToSetProperty(report, "RequestedTypes", "books");
        AddToDictionaryProperty(report, "SkippedTypes", "music", "Not requested");

        Assert.True(GetOverallPass(report));
    }

    [Fact]
    public void OverallPass_FailsWhenSummarySectionHasFailures()
    {
        var report = CreateTestReport();
        AddToListProperty(report, "MediaTypeResults", CreateMediaTypeResult(count: 0, failed: 0));

        Assert.False(GetOverallPass(report));
    }

    [Fact]
    public void OverallPass_FailsWhenIngestionProgressSnapshotHasImpossibleCounts()
    {
        var report = CreateTestReport();
        AddToListProperty(report, "IngestionProgressSnapshots", CreateIngestionProgressSnapshot(progressPercent: 125));

        Assert.False(GetOverallPass(report));
    }

    [Fact]
    public void OverallPass_AllowsIngestionCompletedAfterSoftWaitBudgetTimeout()
    {
        var report = CreateTestReport();
        AddToListProperty(report, "IngestionWaitStages", CreateIngestionWaitStage(completed: true, timedOut: true));

        Assert.True(GetOverallPass(report));
    }

    [Fact]
    public void OverallPass_FailsWhenIngestionWaitStageHardTimesOut()
    {
        var report = CreateTestReport();
        AddToListProperty(report, "IngestionWaitStages", CreateIngestionWaitStage(completed: false, timedOut: true));

        Assert.False(GetOverallPass(report));
    }

    [Fact]
    public void OverallPass_FailsWhenCrossMediaSeriesValidationFails()
    {
        var report = CreateTestReport();
        AddToListProperty(report, "CrossMediaSeriesChecks", CreateCrossMediaSeriesCheckResult());

        Assert.False(GetOverallPass(report));
    }

    [Fact]
    public void GenerateHtmlReport_DoesNotShowAllPassWhenReconciliationMismatches()
    {
        var report = CreateTestReport();
        var reconciliation = CreateReconciliationSummary();
        AddToListProperty(reconciliation, "Mismatches", CreateReconciliationItemResult());
        SetProperty(reconciliation, "ExpectedTotal", 1);
        SetProperty(report, "Reconciliation", reconciliation);

        var html = InvokeGenerateHtmlReport(report);

        Assert.Contains("VALIDATION FAILED", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ALL PASS", html, StringComparison.Ordinal);
    }

    [Fact]
    public void HasCentralPreferredArtwork_AcceptsCentralAssetStorePaths()
    {
        var ownerEntityId = Guid.NewGuid();
        var assetPaths = new AssetPathService(_tempRoot);
        var coverPath = assetPaths.GetCentralAssetPath("Work", ownerEntityId, "CoverArt", Guid.NewGuid(), ".jpg");
        AssetPathService.EnsureDirectory(coverPath);
        File.WriteAllBytes(coverPath, [1, 2, 3]);

        var preferredRecord = CreatePreferredArtworkRecord(coverPath);

        var result = InvokeHasCentralPreferredArtwork(ownerEntityId, preferredRecord, assetPaths, "Work", "CoverArt");

        Assert.True(result);
        Assert.False(Directory.Exists(Path.Combine(_tempRoot, ".data", "images")));
    }

    [Fact]
    public void HasCentralPreferredArtwork_RejectsLegacyPathLocations()
    {
        var ownerEntityId = Guid.NewGuid();
        var assetPaths = new AssetPathService(_tempRoot);
        var legacyPath = Path.Combine(_tempRoot, ".data", "images", "works", "legacy", "cover.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllBytes(legacyPath, [4, 5, 6]);

        var preferredRecord = CreatePreferredArtworkRecord(legacyPath);

        var result = InvokeHasCentralPreferredArtwork(ownerEntityId, preferredRecord, assetPaths, "Work", "CoverArt");

        Assert.False(result);
    }

    [Fact]
    public void ShouldRequireSidecarArtwork_ReturnsFalseWhenArtworkExportIsDisabled()
    {
        var policy = new LibraryStoragePolicy
        {
            ArtworkExport = false,
            ExportProfile = new SidecarExportProfile { Artwork = false },
        };

        var result = InvokeShouldRequireSidecarArtwork(policy);

        Assert.False(result);
    }

    [Fact]
    public void ShouldRequireSidecarArtwork_ReturnsTrueWhenArtworkExportIsEnabled()
    {
        var policy = new LibraryStoragePolicy
        {
            ArtworkExport = true,
            ExportProfile = new SidecarExportProfile { Artwork = false },
        };

        var result = InvokeShouldRequireSidecarArtwork(policy);

        Assert.True(result);
    }

    [Fact]
    public void DescribeFileSystemCheck_ReportsCentralAssetStoreWhenStoredArtworkIsMissing()
    {
        var check = CreateFileSystemCheckResult();
        SetProperty(check, "FileExists", true);
        SetProperty(check, "LocationMatchesExpectation", true);
        SetProperty(check, "RequiresStoredArtwork", true);
        SetProperty(check, "HasStoredCover", false);
        SetProperty(check, "HasStoredCoverSmall", true);
        SetProperty(check, "HasStoredCoverMedium", true);
        SetProperty(check, "HasStoredCoverLarge", true);
        SetProperty(check, "HasStoredPalette", true);

        var result = InvokeDescribeFileSystemCheck(check);

        Assert.Equal("Stored artwork renditions or palette metadata are incomplete in the central asset store", result);
    }

    private static bool InvokeHasCentralPreferredArtwork(
        Guid ownerEntityId,
        object preferredRecord,
        AssetPathService assetPaths,
        string ownerKind,
        string assetType)
    {
        var method = typeof(IntegrationTestEndpoints).GetMethod(
            "HasCentralPreferredArtwork",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(Guid), preferredRecord.GetType());
        var dictionary = (IDictionary)Activator.CreateInstance(dictionaryType)!;
        dictionary.Add(ownerEntityId, preferredRecord);

        var result = method!.Invoke(null, [ownerEntityId, dictionary, assetPaths, ownerKind, assetType]);
        return Assert.IsType<bool>(result);
    }

    private static object CreatePreferredArtworkRecord(
        string localImagePath,
        string? localImagePathSmall = null,
        string? localImagePathMedium = null,
        string? localImagePathLarge = null,
        string? primaryHex = "#112233",
        string? secondaryHex = "#223344",
        string? accentHex = "#334455")
    {
        var type = typeof(IntegrationTestEndpoints).GetNestedType(
            "PreferredArtworkRecord",
            BindingFlags.NonPublic);

        Assert.NotNull(type);
        return Activator.CreateInstance(type!, [localImagePath, localImagePathSmall, localImagePathMedium, localImagePathLarge, primaryHex, secondaryHex, accentHex])!;
    }

    private static bool InvokeShouldRequireSidecarArtwork(LibraryStoragePolicy policy)
    {
        var method = typeof(IntegrationTestEndpoints).GetMethod(
            "ShouldRequireSidecarArtwork",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = method!.Invoke(null, [policy]);
        return Assert.IsType<bool>(result);
    }

    private static object CreateFileSystemCheckResult()
    {
        var type = typeof(IntegrationTestEndpoints).GetNestedType(
            "FileSystemCheckResult",
            BindingFlags.NonPublic);

        Assert.NotNull(type);
        return Activator.CreateInstance(type!, nonPublic: true)!;
    }

    private static object CreateTestReport()
    {
        var type = typeof(IntegrationTestEndpoints).GetNestedType(
            "TestReport",
            BindingFlags.NonPublic);

        Assert.NotNull(type);
        return Activator.CreateInstance(type!, nonPublic: true)!;
    }

    private static object CreateReconciliationSummary()
    {
        var type = typeof(IntegrationTestEndpoints).GetNestedType(
            "ReconciliationSummary",
            BindingFlags.NonPublic);

        Assert.NotNull(type);
        return Activator.CreateInstance(type!, nonPublic: true)!;
    }

    private static object CreateReconciliationItemResult()
    {
        var type = typeof(IntegrationTestEndpoints).GetNestedType(
            "ReconciliationItemResult",
            BindingFlags.NonPublic);

        Assert.NotNull(type);
        var item = Activator.CreateInstance(type!, nonPublic: true)!;
        SetProperty(item, "Title", "Fixture");
        SetProperty(item, "MediaType", "Books");
        SetProperty(item, "Expected", "Identified");
        SetProperty(item, "Actual", "InReview");
        SetProperty(item, "Classification", "UnexpectedReview");
        return item;
    }

    private static object CreateMediaTypeResult(int count, int failed)
    {
        var type = typeof(IntegrationTestEndpoints).GetNestedType(
            "MediaTypeResult",
            BindingFlags.NonPublic);

        Assert.NotNull(type);
        var result = Activator.CreateInstance(type!, nonPublic: true)!;
        SetProperty(result, "MediaType", "Books");
        SetProperty(result, "Count", count);
        SetProperty(result, "Failed", failed);
        return result;
    }

    private static object CreateIngestionProgressSnapshot(double progressPercent)
    {
        var type = typeof(IntegrationTestEndpoints).GetNestedType(
            "IngestionProgressSnapshot",
            BindingFlags.NonPublic);

        Assert.NotNull(type);
        var snapshot = Activator.CreateInstance(type!, nonPublic: true)!;
        SetProperty(snapshot, "AssetCount", 10);
        SetProperty(snapshot, "ExpectedCount", 10);
        SetProperty(snapshot, "ResolvedCount", 10);
        SetProperty(snapshot, "WorkCount", 10);
        SetProperty(snapshot, "PendingCount", 0);
        SetProperty(snapshot, "ClaimCount", 20);
        SetProperty(snapshot, "ActiveJobCount", 0);
        SetProperty(snapshot, "ProgressPercent", progressPercent);
        return snapshot;
    }

    private static object CreateIngestionWaitStage(bool completed, bool timedOut)
    {
        var type = typeof(IntegrationTestEndpoints).GetNestedType(
            "IngestionWaitStageResult",
            BindingFlags.NonPublic);

        Assert.NotNull(type);
        var stage = Activator.CreateInstance(type!, nonPublic: true)!;
        SetProperty(stage, "StageKey", "file_registration");
        SetProperty(stage, "Label", "File registration");
        SetProperty(stage, "BudgetSeconds", 1d);
        SetProperty(stage, "ElapsedSeconds", 2d);
        SetProperty(stage, "Completed", completed);
        SetProperty(stage, "TimedOut", timedOut);
        SetProperty(stage, "CompletedAfterTimeout", completed && timedOut);
        return stage;
    }

    private static object CreateCrossMediaSeriesCheckResult()
    {
        var type = typeof(IntegrationTestEndpoints).GetNestedType(
            "CrossMediaSeriesCheckResult",
            BindingFlags.NonPublic);

        Assert.NotNull(type);
        var result = Activator.CreateInstance(type!, nonPublic: true)!;
        SetProperty(result, "Name", "Fixture");
        SetProperty(result, "OwnedCount", 1);
        return result;
    }

    private static string InvokeDescribeFileSystemCheck(object check)
    {
        var method = typeof(IntegrationTestEndpoints).GetMethod(
            "DescribeFileSystemCheck",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = method!.Invoke(null, [check]);
        return Assert.IsType<string>(result);
    }

    private static string InvokeGenerateHtmlReport(object report)
    {
        var method = typeof(IntegrationTestEndpoints).GetMethod(
            "GenerateHtmlReport",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = method!.Invoke(null, [report]);
        return Assert.IsType<string>(result);
    }

    private static bool GetOverallPass(object instance)
    {
        var property = instance.GetType().GetProperty("OverallPass", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return Assert.IsType<bool>(property!.GetValue(instance));
    }

    private static void SetProperty(object instance, string name, object value)
    {
        var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }

    private static void AddToListProperty(object instance, string name, object value)
    {
        var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var list = Assert.IsAssignableFrom<IList>(property!.GetValue(instance));
        list.Add(value);
    }

    private static void AddToSetProperty(object instance, string name, string value)
    {
        var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var set = Assert.IsAssignableFrom<ISet<string>>(property!.GetValue(instance));
        set.Add(value);
    }

    private static void AddToDictionaryProperty(object instance, string name, string key, string value)
    {
        var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(property!.GetValue(instance));
        dictionary[key] = value;
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
