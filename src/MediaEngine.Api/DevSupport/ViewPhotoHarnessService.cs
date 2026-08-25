using System.Security.Cryptography;
using System.Text.Json;
using MediaEngine.Api.Services.LocalAssets;
using MediaEngine.Contracts.LocalAssets;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.DevSupport;

/// <summary>
/// Development-only, repeatable View fixture runner. Remote images are free-licensed
/// Wikimedia Commons files; the harness supplies deterministic metadata so upstream
/// EXIF changes cannot make the assertions flaky.
/// </summary>
public sealed class ViewPhotoHarnessService(
    IHttpClientFactory httpClientFactory,
    ViewLibraryService viewLibrary,
    ViewStorageService viewStorage,
    ILocalAssetRepository assets,
    IViewDiscoveryRepository discovery,
    ILogger<ViewPhotoHarnessService> logger)
{
    public static readonly Guid ProfileId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string ProvenanceSource = "tuvima-dev-harness:reviewed-fixture";

    private static readonly Fixture[] Fixtures =
    [
        new("portrait-khwairakpam-chaoba.jpg", "Khwairakpam Chaoba portrait", "https://commons.wikimedia.org/wiki/Special:Redirect/file/A_portrait_of_Khwairakpam_Chaoba.jpg?width=1280", "https://commons.wikimedia.org/wiki/File:A_portrait_of_Khwairakpam_Chaoba.jpg", "CC0 1.0", "Singh, Prafullo", new DateTimeOffset(2021, 1, 15, 10, 30, 0, TimeSpan.Zero), 24.8170, 93.9368, "Imphal, India", ["Khwairakpam Chaoba"]),
        new("family-outdoors.jpg", "Family posing outdoors", "https://commons.wikimedia.org/wiki/Special:Redirect/file/Elderly_family_posing_outdoors_(16350013894).jpg?width=1280", "https://commons.wikimedia.org/wiki/File:Elderly_family_posing_outdoors_(16350013894).jpg", "CC BY 2.0", "simpleinsomnia", new DateTimeOffset(2015, 4, 2, 16, 45, 0, TimeSpan.Zero), 47.6062, -122.3321, "Seattle, United States", ["Harness Person A", "Harness Person B"]),
        new("chicago-sunset.jpg", "Chicago skyline at sunset", "https://commons.wikimedia.org/wiki/Special:Redirect/file/Chicago_Skyline_At_Sunset_United_States_Cityscape_Photography_(112263307).jpeg?width=1280", "https://commons.wikimedia.org/wiki/File:Chicago_Skyline_At_Sunset_United_States_Cityscape_Photography_(112263307).jpeg", "CC BY 3.0", "Giuseppe Milo", new DateTimeOffset(2018, 9, 21, 18, 12, 0, TimeSpan.Zero), 41.8781, -87.6298, "Chicago, United States", []),
        new("paris-eiffel.jpg", "Eiffel Tower", "https://commons.wikimedia.org/wiki/Special:Redirect/file/Eiffelturm_Europameisterschaft_(166775095).jpeg?width=1280", "https://commons.wikimedia.org/wiki/File:Eiffelturm_Europameisterschaft_(166775095).jpeg", "CC BY 3.0", "Dronepicr", new DateTimeOffset(2016, 6, 10, 12, 0, 0, TimeSpan.Zero), 48.8584, 2.2945, "Paris, France", []),
        new("tokyo-shibuya.jpg", "Shibuya street", "https://commons.wikimedia.org/wiki/Special:Redirect/file/Shibuya_Tokyo_Color_Street_Photography_(240747567).jpeg?width=1280", "https://commons.wikimedia.org/wiki/File:Shibuya_Tokyo_Color_Street_Photography_(240747567).jpeg", "CC BY 3.0", "Giuseppe Milo", new DateTimeOffset(2019, 11, 3, 20, 5, 0, TimeSpan.Zero), 35.6595, 139.7005, "Tokyo, Japan", [])
    ];

    public async Task<ViewPhotoHarnessReport> RunAsync(CancellationToken ct)
    {
        var space = await viewStorage.EnsurePersonalSpaceAsync(ProfileId, ct);
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "TuvimaLibraryPhotoHarness/1.0 (development fixture; https://github.com/Tuvima/tuvima_library)");
        var results = new List<ViewPhotoFixtureResult>();

        foreach (var fixture in Fixtures)
        {
            try
            {
                var bytes = await client.GetByteArrayAsync(fixture.DownloadUrl, ct);
                await using var stream = new MemoryStream(bytes, writable: false);
                var upload = await viewLibrary.UploadAsync(ProfileId, $"harness-{fixture.FileName}", stream, ct);
                var location = assets.ResolveContent(upload.ItemId)
                    ?? throw new InvalidOperationException("Uploaded fixture has no resolvable content source.");
                var existing = assets.Find(upload.ItemId)
                    ?? throw new InvalidOperationException("Uploaded fixture was not readable after indexing.");
                await assets.UpsertAsync(new LocalAssetRegistration(
                    space.LibraryId, space.Id, ProfileId, LocalAssetMediaKinds.Image, fixture.Title,
                    fixture.CapturedAt,
                    [new LocalAssetFileRegistration(location.FilePath, location.ContentHash,
                        Path.GetFileName(location.FilePath), location.MimeType, location.ByteSize,
                        File.GetLastWriteTimeUtc(location.FilePath), LocalAssetFileRoles.Primary,
                        SourceId: location.SourceId, DeviceId: location.DeviceId)],
                    existing.Width, existing.Height, DeviceMake: "Tuvima Harness",
                    DeviceModel: "Deterministic Fixture", Latitude: fixture.Latitude,
                    Longitude: fixture.Longitude, LocationName: fixture.LocationName,
                    MetadataJson: JsonSerializer.Serialize(new
                    {
                        fixture.SourcePage, fixture.License, fixture.Author,
                        sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                        metadata_mode = "deterministic-harness"
                    }), ExistingItemId: upload.ItemId), ct);
                foreach (var person in fixture.People)
                    await assets.AddAnnotationAsync(upload.ItemId,
                        new LocalAssetAnnotation("person_name", person, ProvenanceSource,
                            Confidence: 1, ProvenanceJson: JsonSerializer.Serialize(new
                            { fixture.SourcePage, assertion = "manual test fixture; not face recognition" }),
                            ReviewedAt: DateTimeOffset.UtcNow), ct);
                await assets.ReplaceTagsAsync(upload.ItemId,
                    ["view-photo-harness", "free-stock", fixture.LocationName], ct);
                results.Add(new ViewPhotoFixtureResult(fixture.FileName, upload.ItemId, "passed", null,
                    fixture.SourcePage, fixture.License, fixture.Author));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "View photo harness fixture {Fixture} failed", fixture.FileName);
                results.Add(new ViewPhotoFixtureResult(fixture.FileName, null, "failed", ex.Message,
                    fixture.SourcePage, fixture.License, fixture.Author));
            }
        }

        // Adding/removing is tested through reversible product lifecycle state.
        var lifecyclePassed = false;
        if (results.FirstOrDefault(result => result.ItemId.HasValue)?.ItemId is { } lifecycleId)
        {
            await assets.SetLifecycleStateAsync(lifecycleId, LocalAssetLifecycleState.Trashed, ct);
            var trashed = assets.Find(lifecycleId)?.TrashedAt is not null;
            await assets.SetLifecycleStateAsync(lifecycleId, LocalAssetLifecycleState.Active, ct);
            lifecyclePassed = trashed && assets.Find(lifecycleId)?.TrashedAt is null;
        }

        var places = discovery.QueryPlaces(new ViewPlaceDiscoveryQuery([space.LibraryId], 100), ct);
        var people = discovery.QueryPeople(new ViewPeopleDiscoveryQuery([space.LibraryId], 100), ct);
        var passed = results.All(result => result.Status == "passed")
                     && lifecyclePassed && places.Items.Count >= Fixtures.Length
                     && people.Items.Count >= Fixtures.Sum(fixture => fixture.People.Length);
        return new ViewPhotoHarnessReport(passed, ProfileId, space.LibraryId, results,
            places.Items.Count, people.Items.Count, lifecyclePassed,
            "People are reviewed fixture annotations; automatic face recognition is not asserted.");
    }

    private sealed record Fixture(string FileName, string Title, string DownloadUrl,
        string SourcePage, string License, string Author, DateTimeOffset CapturedAt,
        double Latitude, double Longitude, string LocationName, string[] People);
}

public sealed record ViewPhotoFixtureResult(string FileName, Guid? ItemId, string Status,
    string? Error, string SourcePage, string License, string Author);

public sealed record ViewPhotoHarnessReport(bool Passed, Guid ProfileId, Guid LibraryId,
    IReadOnlyList<ViewPhotoFixtureResult> Fixtures, int PlaceGroups, int PeopleGroups,
    bool TrashRestorePassed, string PeopleEvidenceNote);
