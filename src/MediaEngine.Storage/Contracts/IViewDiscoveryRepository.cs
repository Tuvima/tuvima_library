namespace MediaEngine.Storage.Contracts;

/// <summary>
/// Read-only personal-media discovery boundary. Library IDs must come from the
/// trusted View scope resolver; callers cannot discover by supplying arbitrary IDs.
/// </summary>
public interface IViewDiscoveryRepository
{
    ViewPlaceDiscoveryPage QueryPlaces(ViewPlaceDiscoveryQuery query, CancellationToken ct = default);
    ViewPeopleDiscoveryPage QueryPeople(ViewPeopleDiscoveryQuery query, CancellationToken ct = default);
}

public sealed record ViewPlaceDiscoveryQuery(
    IReadOnlyCollection<Guid> AuthorizedLibraryIds,
    int Limit = 50,
    string? Search = null,
    ViewDiscoveryCursor? Cursor = null);

public sealed record ViewPeopleDiscoveryQuery(
    IReadOnlyCollection<Guid> AuthorizedLibraryIds,
    int Limit = 100,
    string? Search = null,
    ViewDiscoveryCursor? Cursor = null);

public sealed record ViewDiscoveryCursor(int AssetCount, string Key);

public sealed record ViewPlaceDiscoveryRow(
    string Key,
    string Name,
    double Latitude,
    double Longitude,
    int AssetCount,
    Guid RepresentativeLibraryId,
    Guid RepresentativeAssetId);

public sealed record ViewPersonDiscoveryRow(
    string Key,
    string DisplayName,
    int AssetCount,
    Guid RepresentativeLibraryId,
    Guid RepresentativeAssetId,
    IReadOnlyList<string> AnnotationKinds,
    IReadOnlyList<string> ProvenanceSources,
    bool HasReviewedEvidence);

public sealed record ViewPlaceDiscoveryPage(
    IReadOnlyList<ViewPlaceDiscoveryRow> Items,
    ViewDiscoveryCursor? NextCursor,
    bool HasMore,
    bool HasEligibleData);

public sealed record ViewPeopleDiscoveryPage(
    IReadOnlyList<ViewPersonDiscoveryRow> Items,
    ViewDiscoveryCursor? NextCursor,
    bool HasMore,
    bool HasEligibleData);
