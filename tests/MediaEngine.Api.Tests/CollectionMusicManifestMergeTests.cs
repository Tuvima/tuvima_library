using MediaEngine.Api.Models;
using MediaEngine.Api.Services.Collections;

namespace MediaEngine.Api.Tests;

public sealed class CollectionMusicManifestMergeTests
{

    [Fact]
    public void MergeUnownedMusicTracks_DedupesOwnedTrackWithParentheticalManifestTitle()
    {
        var ownedId = Guid.NewGuid();
        var owned = new List<CollectionGroupWorkDto>
        {
            new()
            {
                WorkId = ownedId,
                Title = "Death on Two Legs",
                TrackNumber = "1",
                DurationSeconds = 223,
                IsOwned = true,
            },
        };

        var merged = Merge(owned, """
            {
              "tracks": [
                {
                  "title": "Death on Two Legs (Dedicated To...)",
                  "track_number": 1,
                  "duration_seconds": 223
                }
              ]
            }
            """);

        var item = Assert.Single(merged);
        Assert.Equal(ownedId, item.WorkId);
        Assert.True(item.IsOwned);
    }

    [Fact]
    public void MergeUnownedMusicTracks_DoesNotCollideAcrossDiscsOnTrackNumber()
    {
        var owned = new List<CollectionGroupWorkDto>
        {
            new()
            {
                WorkId = Guid.NewGuid(),
                Title = "Intro",
                DiscNumber = 1,
                TrackNumber = "1",
                IsOwned = true,
            },
        };

        var merged = Merge(owned, """
            {
              "tracks": [
                {
                  "title": "Intro",
                  "disc_number": 2,
                  "track_number": 1
                }
              ]
            }
            """);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, item => item.IsOwned && item.DiscNumber == 1 && item.TrackNumber == "1");
        Assert.Contains(merged, item => !item.IsOwned && item.DiscNumber == 2 && item.TrackNumber == "1");
    }

    // Called directly rather than by reflection: the merge is a pure static function on
    // AlbumTrackManifestService, extracted out of CollectionEndpoints in stage 5A.
    private static List<CollectionGroupWorkDto> Merge(
        List<CollectionGroupWorkDto> owned,
        string childEntitiesJson) =>
        AlbumTrackManifestService.MergeUnownedMusicTracks(owned, childEntitiesJson, null);
}
