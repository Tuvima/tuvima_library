using MediaEngine.Domain;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Workers;

namespace MediaEngine.Providers.Tests;

public sealed class TextTrackIdentityTests
{
    [Fact]
    public void ProviderCandidateIdentity_IsStableAcrossEquivalentRefreshes()
    {
        var assetId = Guid.NewGuid();

        var first = TextTrackEnrichmentWorker.BuildTrackIdentity(
            assetId,
            TextTrackKind.Subtitles,
            "OpenSubtitles",
            "subtitle-42",
            "EN");
        var second = TextTrackEnrichmentWorker.BuildTrackIdentity(
            assetId,
            TextTrackKind.Subtitles,
            " opensubtitles ",
            " SUBTITLE-42 ",
            "en");

        Assert.Equal(first, second);
        Assert.Equal(Hashing.DeterministicGuid(first), Hashing.DeterministicGuid(second));
    }

    [Fact]
    public void DifferentProviderCandidates_GetDifferentManagedTrackIds()
    {
        var assetId = Guid.NewGuid();
        var first = TextTrackEnrichmentWorker.BuildTrackIdentity(
            assetId,
            TextTrackKind.Lyrics,
            "lrclib",
            "track-a",
            "en");
        var second = TextTrackEnrichmentWorker.BuildTrackIdentity(
            assetId,
            TextTrackKind.Lyrics,
            "lrclib",
            "track-b",
            "en");

        Assert.NotEqual(Hashing.DeterministicGuid(first), Hashing.DeterministicGuid(second));
    }
}
