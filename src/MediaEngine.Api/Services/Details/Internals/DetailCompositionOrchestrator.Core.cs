using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using MediaEngine.Api.Endpoints;
using MediaEngine.Api.Models;
using MediaEngine.Api.Services.Display;
using MediaEngine.Api.Services.Playback;
using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Contracts.Collections;
using SeriesManifestViewDto = MediaEngine.Domain.Models.SeriesManifestViewDto;
using SeriesManifestItemDto = MediaEngine.Domain.Models.SeriesManifestItemDto;
using MediaEngine.Contracts.Details;
using MediaEngine.Contracts.Persons;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;
using static MediaEngine.Api.Services.Details.Internals.DetailPresentationPolicy;

namespace MediaEngine.Api.Services.Details.Internals;

internal sealed partial class DetailCompositionOrchestrator
{
    private static readonly Guid DefaultOwnerUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly IDatabaseConnection _db;
    private readonly ILibraryItemRepository _libraryItems;
    private readonly IPersonRepository _persons;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly ICanonicalValueArrayRepository _canonicalArrays;
    private readonly ISeriesManifestRepository _seriesManifests;
    private readonly IPersonCreditReadService _personCredits;
    private readonly DetailRecommendationService _recommendations;
    private readonly PlaybackCapabilitiesService? _playback;
    private readonly ILogger<DetailComposerService>? _logger;
    private readonly ICollectionBrowseReadService? _collectionBrowse;
    private readonly CollectionCatalogReadService? _collectionCatalog;
    private readonly IProfileRepository? _profiles;
    private readonly DetailProjectionReader _reader;

    public DetailCompositionOrchestrator(
        IDatabaseConnection db,
        ILibraryItemRepository libraryItems,
        IPersonRepository persons,
        IEntityAssetRepository entityAssets,
        ICanonicalValueArrayRepository canonicalArrays,
        ISeriesManifestRepository seriesManifests,
        IPersonCreditReadService personCredits,
        DetailRecommendationService recommendations,
        PlaybackCapabilitiesService? playback = null,
        ILogger<DetailComposerService>? logger = null,
        ICollectionBrowseReadService? collectionBrowse = null,
        CollectionCatalogReadService? collectionCatalog = null,
        IProfileRepository? profiles = null)
    {
        _db = db;
        _libraryItems = libraryItems;
        _persons = persons;
        _entityAssets = entityAssets;
        _canonicalArrays = canonicalArrays;
        _seriesManifests = seriesManifests;
        _personCredits = personCredits;
        _recommendations = recommendations;
        _playback = playback;
        _logger = logger;
        _collectionBrowse = collectionBrowse;
        _collectionCatalog = collectionCatalog;
        _profiles = profiles;
        _reader = new DetailProjectionReader(db, entityAssets);
    }

    public async Task<DetailPageViewModel?> BuildAsync(
        DetailEntityType entityType,
        Guid id,
        DetailPresentationContext context,
        CancellationToken ct = default,
        string? selectedContainerId = null,
        Guid? profileId = null)
    {
        var isAdminView = context is DetailPresentationContext.Admin;
        var favoriteWorkIds = await _reader.LoadFavoriteWorkIdsAsync(profileId, ct);

        return entityType switch
        {
            DetailEntityType.Person => await BuildPersonAsync(id, entityType, context, isAdminView, ct),
            DetailEntityType.BookSeries => await BuildBookSeriesAsync(id, context, isAdminView, favoriteWorkIds, profileId, ct),
            DetailEntityType.Collection or DetailEntityType.TvShow or DetailEntityType.MovieSeries
                or DetailEntityType.ComicSeries or DetailEntityType.MusicAlbum => await BuildCollectionAsync(
                    id,
                    entityType,
                    context,
                    isAdminView,
                    favoriteWorkIds,
                    ct,
                    profileId: profileId),
            DetailEntityType.Character => await BuildCharacterAsync(id, context, isAdminView, ct),
            DetailEntityType.Universe => await BuildUniverseAsync(id, context, isAdminView, ct),
            _ => await BuildWorkAsync(id, entityType, context, isAdminView, selectedContainerId, favoriteWorkIds, profileId, ct),
        };
    }
}
