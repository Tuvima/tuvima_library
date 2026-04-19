using Dapper;
using MediaEngine.Api.Services;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Api.Tests;

public sealed class AssetExportServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _libraryRoot;
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public AssetExportServiceTests()
    {
        DapperConfiguration.Configure();
        _tempRoot = Path.Combine(Path.GetTempPath(), $"tuvima_asset_export_{Guid.NewGuid():N}");
        _libraryRoot = Path.Combine(_tempRoot, "library");
        _dbPath = Path.Combine(_tempRoot, "library.db");
        Directory.CreateDirectory(_libraryRoot);

        _db = new DatabaseConnection(_dbPath);
        _db.InitializeSchema();
        _db.RunStartupChecks();
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { }
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public async Task ReconcileArtworkAsync_CleanupEnabled_RemovesLegacyDuplicateSidecar()
    {
        var context = await SeedMovieAsync();
        var policy = new LibraryStoragePolicy
        {
            Mode = StorageMode.Hybrid,
            ArtworkExport = false,
            CleanupManagedLocalArtwork = true,
            ExportProfile = new SidecarExportProfile
            {
                Name = "plex-jellyfin-common",
                Artwork = false,
                PreferredSubtitles = true,
                MetadataSidecars = false,
            }
        };

        var assetPaths = new AssetPathService(_libraryRoot, policy);
        var variantId = Guid.NewGuid();
        var centralPath = assetPaths.GetCentralAssetPath("Work", context.WorkId, "CoverArt", variantId, ".jpg");
        AssetPathService.EnsureDirectory(centralPath);
        await File.WriteAllBytesAsync(centralPath, [1, 2, 3, 4]);

        var exportPath = Path.Combine(context.MovieFolder, "poster.jpg");
        await File.WriteAllBytesAsync(exportPath, [1, 2, 3, 4]);

        var entityAssetRepo = new EntityAssetRepository(_db);
        await entityAssetRepo.UpsertAsync(new EntityAsset
        {
            Id = variantId,
            EntityId = context.WorkId.ToString(),
            EntityType = "Work",
            AssetTypeValue = "CoverArt",
            LocalImagePath = centralPath,
            SourceProvider = "fanart_tv",
            AssetClassValue = "Artwork",
            StorageLocationValue = "Central",
            OwnerScope = "Work",
            IsPreferred = true,
        });

        var service = new AssetExportService(_db, entityAssetRepo, assetPaths, NullLogger<AssetExportService>.Instance);
        await service.ReconcileArtworkAsync(context.WorkId.ToString(), "Work", "CoverArt");

        Assert.False(File.Exists(exportPath));

        var persisted = await entityAssetRepo.FindByIdAsync(variantId);
        Assert.NotNull(persisted);
        Assert.False(persisted!.IsLocallyExported);
        Assert.False(persisted.IsPreferredExported);
    }

    [Fact]
    public async Task ReconcileArtworkAsync_CleanupDisabled_PreservesLegacyDuplicateSidecar()
    {
        var context = await SeedMovieAsync();
        var policy = new LibraryStoragePolicy
        {
            Mode = StorageMode.Hybrid,
            ArtworkExport = false,
            CleanupManagedLocalArtwork = false,
            ExportProfile = new SidecarExportProfile
            {
                Name = "plex-jellyfin-common",
                Artwork = false,
                PreferredSubtitles = true,
                MetadataSidecars = false,
            }
        };

        var assetPaths = new AssetPathService(_libraryRoot, policy);
        var variantId = Guid.NewGuid();
        var centralPath = assetPaths.GetCentralAssetPath("Work", context.WorkId, "CoverArt", variantId, ".jpg");
        AssetPathService.EnsureDirectory(centralPath);
        await File.WriteAllBytesAsync(centralPath, [9, 8, 7, 6]);

        var exportPath = Path.Combine(context.MovieFolder, "poster.jpg");
        await File.WriteAllBytesAsync(exportPath, [9, 8, 7, 6]);

        var entityAssetRepo = new EntityAssetRepository(_db);
        await entityAssetRepo.UpsertAsync(new EntityAsset
        {
            Id = variantId,
            EntityId = context.WorkId.ToString(),
            EntityType = "Work",
            AssetTypeValue = "CoverArt",
            LocalImagePath = centralPath,
            SourceProvider = "fanart_tv",
            AssetClassValue = "Artwork",
            StorageLocationValue = "Central",
            OwnerScope = "Work",
            IsPreferred = true,
        });

        var service = new AssetExportService(_db, entityAssetRepo, assetPaths, NullLogger<AssetExportService>.Instance);
        await service.ReconcileArtworkAsync(context.WorkId.ToString(), "Work", "CoverArt");

        Assert.True(File.Exists(exportPath));
    }

    private async Task<MovieContext> SeedMovieAsync()
    {
        var collectionId = Guid.NewGuid();
        var workId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var movieFolder = Path.Combine(_libraryRoot, "Movies", "Arrival (2016)");
        Directory.CreateDirectory(movieFolder);
        var movieFile = Path.Combine(movieFolder, "Arrival (2016).mkv");
        await File.WriteAllBytesAsync(movieFile, [0]);

        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            """
            INSERT INTO collections (id, created_at) VALUES (@CollectionId, datetime('now'));
            INSERT INTO works (id, collection_id, media_type) VALUES (@WorkId, @CollectionId, 'Movies');
            INSERT INTO editions (id, work_id) VALUES (@EditionId, @WorkId);
            """,
            new
            {
                CollectionId = collectionId.ToString(),
                WorkId = workId.ToString(),
                EditionId = editionId.ToString(),
            });

        var assetRepo = new MediaAssetRepository(_db);
        await assetRepo.InsertAsync(new MediaAsset
        {
            Id = assetId,
            EditionId = editionId,
            ContentHash = $"hash_{assetId:N}",
            FilePathRoot = movieFile,
            Status = AssetStatus.Normal,
        });

        return new MovieContext(workId, movieFolder);
    }

    private sealed record MovieContext(Guid WorkId, string MovieFolder);
}
