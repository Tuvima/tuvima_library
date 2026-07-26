using System.Text.Json;
using MediaEngine.Contracts.Collections;

namespace MediaEngine.Contracts.Tests;

public sealed class CollectionGroupContractTests
{
    [Fact]
    public void WorkRoundTrip_PreservesCompleteApiEmittedFields()
    {
        var work = new CollectionGroupWorkDto
        {
            WorkId = Guid.NewGuid(),
            Title = "Track",
            DiscNumber = 2,
            AppleMusicId = "apple-123",
            Stage1 = new LibraryPipelineStageDto { State = "complete", Label = "Retail" },
            Stage2 = new LibraryPipelineStageDto { State = "complete", Label = "Identity" },
            Stage3 = new LibraryPipelineStageDto { State = "pending", Label = "Universe" },
        };

        var json = JsonSerializer.Serialize(work);
        var roundTrip = JsonSerializer.Deserialize<CollectionGroupWorkDto>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal(2, roundTrip.DiscNumber);
        Assert.Equal("apple-123", roundTrip.AppleMusicId);
        Assert.Equal("complete", roundTrip.Stage1?.State);
        Assert.Equal("complete", roundTrip.Stage2?.State);
        Assert.Equal("pending", roundTrip.Stage3?.State);
        Assert.Contains("\"disc_number\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"apple_music_id\":\"apple-123\"", json, StringComparison.Ordinal);
        Assert.Contains("\"stage1\":", json, StringComparison.Ordinal);
        Assert.Contains("\"stage2\":", json, StringComparison.Ordinal);
        Assert.Contains("\"stage3\":", json, StringComparison.Ordinal);
    }
}
