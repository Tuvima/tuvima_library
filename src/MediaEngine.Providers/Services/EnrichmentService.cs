using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Providers.Workers;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Dispatches enrichment work to specialized workers based on pass type.
/// Thin orchestrator — no enrichment logic lives here.
/// </summary>
public sealed class EnrichmentService : IEnrichmentService
{
    private readonly CoverArtWorker _coverArt;
    private readonly PersonEnrichmentWorker _persons;
    private readonly ChildEntityWorker _children;
    private readonly FictionalEntityWorker _fictional;
    private readonly DescriptionEnrichmentWorker _descriptions;
    private readonly IImageEnrichmentService _images;
    private readonly IWriteBackService _writeBack;
    private readonly ICollectionRepository _collectionRepo;
    private readonly IParentCollectionResolver _parentCollectionResolver;
    private readonly ILogger<EnrichmentService> _logger;

    public EnrichmentService(
        CoverArtWorker coverArt,
        PersonEnrichmentWorker persons,
        ChildEntityWorker children,
        FictionalEntityWorker fictional,
        DescriptionEnrichmentWorker descriptions,
        IImageEnrichmentService images,
        IWriteBackService writeBack,
        ICollectionRepository collectionRepo,
        IParentCollectionResolver parentCollectionResolver,
        ILogger<EnrichmentService> logger)
    {
        _coverArt = coverArt;
        _persons = persons;
        _children = children;
        _fictional = fictional;
        _descriptions = descriptions;
        _images = images;
        _writeBack = writeBack;
        _collectionRepo = collectionRepo;
        _parentCollectionResolver = parentCollectionResolver;
        _logger = logger;
    }

    public async Task RunQuickPassAsync(Guid entityId, string qid, CancellationToken ct = default)
    {
        _logger.LogInformation("Quick pass starting for entity {Id} (QID {Qid})", entityId, qid);
        await _coverArt.DownloadAndPersistAsync(entityId, qid, ct);
        await _persons.EnrichFromClaimsAsync(entityId, ct);
        await _writeBack.WriteMetadataAsync(entityId, "quick_hydration", ct);
        _logger.LogInformation("Quick pass completed for entity {Id}", entityId);
    }

    public async Task RunUniversePassAsync(Guid entityId, string qid, CancellationToken ct = default)
    {
        _logger.LogInformation("Universe pass starting for entity {Id} (QID {Qid})", entityId, qid);
        await RunUniverseCorePassAsync(entityId, qid, ct);
        await RunUniverseEnhancerPassAsync(entityId, qid, ct);
        _logger.LogInformation("Universe pass completed for entity {Id}", entityId);
    }

    public async Task RunUniverseCorePassAsync(Guid entityId, string qid, CancellationToken ct = default)
    {
        await _children.DiscoverAsync(entityId, qid, ct);
        await _fictional.EnrichAsync(entityId, qid, ct);
        await _persons.EnrichActorCharacterMappingsAsync(entityId, qid, ct);
        await ResolveParentCollectionAsync(entityId, ct);
    }

    public async Task RunUniverseEnhancerPassAsync(Guid entityId, string qid, CancellationToken ct = default)
    {
        await _images.EnrichWorkImagesAsync(entityId, qid, ct);
        await _descriptions.EnrichAsync(entityId, qid, ct);
        await _writeBack.WriteMetadataAsync(entityId, "universe_enrichment", ct);
    }

    public async Task RunSingleEnrichmentAsync(Guid entityId, string qid, EnrichmentType type, CancellationToken ct = default)
    {
        _logger.LogInformation("Single enrichment {Type} starting for entity {Id}", type, entityId);
        switch (type)
        {
            case EnrichmentType.CoverArt:
                await _coverArt.DownloadAndPersistAsync(entityId, qid, ct);
                break;
            case EnrichmentType.Persons:
                await _persons.EnrichFromClaimsAsync(entityId, ct);
                break;
            case EnrichmentType.Children:
                await _children.DiscoverAsync(entityId, qid, ct);
                break;
            case EnrichmentType.Images:
                await _images.EnrichWorkImagesAsync(entityId, qid, ct);
                break;
            case EnrichmentType.Fictional:
                await _fictional.EnrichAsync(entityId, qid, ct);
                break;
            case EnrichmentType.Descriptions:
                await _descriptions.EnrichAsync(entityId, qid, ct);
                break;
            case EnrichmentType.WriteBack:
                await _writeBack.WriteMetadataAsync(entityId, "manual_enrichment", ct);
                break;
        }
    }

    private async Task ResolveParentCollectionAsync(Guid entityId, CancellationToken ct)
    {
        var workId = await _collectionRepo.GetWorkIdByMediaAssetAsync(entityId, ct);
        if (!workId.HasValue)
            return;

        var collectionId = await _collectionRepo.GetCollectionIdByWorkIdAsync(workId.Value, ct);
        if (!collectionId.HasValue)
            return;

        await _parentCollectionResolver.ResolveParentCollectionAsync(collectionId.Value, ct);
    }
}
