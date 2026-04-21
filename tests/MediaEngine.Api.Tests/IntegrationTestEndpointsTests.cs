using System.Reflection;
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
    public void HasCentralPreferredArtwork_AcceptsCentralAssetStorePaths()
    {
        var ownerEntityId = Guid.NewGuid();
        var assetPaths = new AssetPathService(_tempRoot);
        var coverPath = assetPaths.GetCentralAssetPath("Work", ownerEntityId, "CoverArt", Guid.NewGuid(), ".jpg");
        AssetPathService.EnsureDirectory(coverPath);
        File.WriteAllBytes(coverPath, [1, 2, 3]);

        var preferredPaths = new Dictionary<Guid, string>
        {
            [ownerEntityId] = coverPath,
        };

        var result = InvokeHasCentralPreferredArtwork(ownerEntityId, preferredPaths, assetPaths, "Work", "CoverArt");

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

        var preferredPaths = new Dictionary<Guid, string>
        {
            [ownerEntityId] = legacyPath,
        };

        var result = InvokeHasCentralPreferredArtwork(ownerEntityId, preferredPaths, assetPaths, "Work", "CoverArt");

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
        SetProperty(check, "HasStoredCoverThumb", true);
        SetProperty(check, "HasStoredHero", true);

        var result = InvokeDescribeFileSystemCheck(check);

        Assert.Equal("Stored artwork set is incomplete in the central asset store", result);
    }

    private static bool InvokeHasCentralPreferredArtwork(
        Guid ownerEntityId,
        IReadOnlyDictionary<Guid, string> preferredPaths,
        AssetPathService assetPaths,
        string ownerKind,
        string assetType)
    {
        var method = typeof(IntegrationTestEndpoints).GetMethod(
            "HasCentralPreferredArtwork",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = method!.Invoke(null, [ownerEntityId, preferredPaths, assetPaths, ownerKind, assetType]);
        return Assert.IsType<bool>(result);
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

    private static string InvokeDescribeFileSystemCheck(object check)
    {
        var method = typeof(IntegrationTestEndpoints).GetMethod(
            "DescribeFileSystemCheck",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = method!.Invoke(null, [check]);
        return Assert.IsType<string>(result);
    }

    private static void SetProperty(object instance, string name, object value)
    {
        var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }
}
