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
    public void RunFullIntegration_RequestsGeneratedStateWipe()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"tools\Run-FullIntegration.ps1"));

        Assert.Contains("wipeScope=generated-state", source, StringComparison.Ordinal);
        Assert.Contains("stages=$Stages", source, StringComparison.Ordinal);
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
