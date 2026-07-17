using MediaEngine.Api.Models;
using MediaEngine.Contracts.Display;
using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Display;

public sealed class DisplayHomeCollectionProjectionReader
{
    private const string HomeLocation = "home";

    private readonly ICollectionPlacementRepository _placements;
    private readonly CollectionCatalogReadService _catalog;
    private readonly IProfileRepository _profiles;

    public DisplayHomeCollectionProjectionReader(
        ICollectionPlacementRepository placements,
        CollectionCatalogReadService catalog,
        IProfileRepository profiles)
    {
        _placements = placements;
        _catalog = catalog;
        _profiles = profiles;
    }

    public async Task<IReadOnlyList<DisplayHomeCollectionRow>> LoadAsync(Guid? profileId, CancellationToken ct)
    {
        var placements = await _placements.GetByLocationAsync(HomeLocation, ct);
        if (placements.Count == 0)
        {
            return [];
        }

        var activeProfile = await ResolveActiveProfileAsync(profileId, ct);
        var catalog = await _catalog.GetCatalogAsync(activeProfile, ct);
        var catalogById = catalog.ToDictionary(item => item.Id);

        var rows = new List<DisplayHomeCollectionRow>();
        foreach (var placement in placements.OrderBy(item => item.Position))
        {
            if (!catalogById.TryGetValue(placement.CollectionId, out var collection)
                || collection.ItemCount <= 0)
            {
                continue;
            }

            rows.Add(ToRow(collection));
        }

        return rows;
    }

    private async Task<Profile?> ResolveActiveProfileAsync(Guid? profileId, CancellationToken ct)
    {
        if (!profileId.HasValue)
        {
            return null;
        }

        return await _profiles.GetByIdAsync(profileId.Value, ct);
    }

    private static DisplayHomeCollectionRow ToRow(CollectionManagementCatalogDto collection)
    {
        var artwork = collection.ArtworkItems.FirstOrDefault();
        var primaryArtwork = FirstNonBlank(collection.SquareArtworkUrl, artwork?.CoverUrl);
        return new DisplayHomeCollectionRow
        {
            CollectionId = collection.Id,
            Title = collection.Name,
            Subtitle = collection.Description,
            CollectionType = collection.CollectionType,
            PrimaryLane = collection.PrimaryLane,
            ItemCount = collection.ItemCount,
            WatchCount = collection.WatchCount,
            ReadCount = collection.ReadCount,
            ListenCount = collection.ListenCount,
            PreviewItems = collection.ArtworkItems
                .Where(item => !string.IsNullOrWhiteSpace(item.CoverUrl))
                .Select(item => new DisplayCardPreviewItemDto(
                    item.WorkId,
                    null,
                    item.Title,
                    item.CoverUrl!,
                    item.ArtworkShape,
                    null,
                    item.MediaType,
                    DisplayCardBuilder.PreviewWebUrlFor(item.WorkId, item.MediaType),
                    item.Description,
                    item.Facts))
                .ToList(),
            CreatedAt = collection.ModifiedAt ?? collection.CreatedAt,
            BackgroundUrl = primaryArtwork,
            BackgroundSmallUrl = primaryArtwork,
            BackgroundMediumUrl = primaryArtwork,
            BackgroundLargeUrl = primaryArtwork,
            AccentColor = collection.ArtworkPalette.AccentColor,
        };
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
