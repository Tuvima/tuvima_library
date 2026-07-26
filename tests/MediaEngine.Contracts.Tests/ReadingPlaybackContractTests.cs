using System.Text.Json;
using MediaEngine.Contracts.Progress;
using MediaEngine.Contracts.Reading;
using MediaEngine.Domain.Services;

namespace MediaEngine.Contracts.Tests;

public sealed class ReadingPlaybackContractTests
{
    [Fact]
    public void ReaderBookmarkContract_PreservesTheApiCompleteWireShape()
    {
        var bookmark = new ReaderBookmarkDto
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            UserId = "local",
            AssetId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ChapterIndex = 4,
            CfiPosition = "epubcfi(/6/8)",
            Label = "Return here",
            CreatedAt = new DateTime(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc),
        };

        var json = JsonSerializer.Serialize(bookmark, MediaEngineJson.Web);
        var roundTrip = JsonSerializer.Deserialize<ReaderBookmarkDto>(json, MediaEngineJson.Web);

        Assert.Contains("\"userId\":\"local\"", json, StringComparison.Ordinal);
        Assert.Equal(bookmark.Id, roundTrip?.Id);
        Assert.Equal(bookmark.UserId, roundTrip?.UserId);
        Assert.Equal(bookmark.CfiPosition, roundTrip?.CfiPosition);
    }

    [Fact]
    public void ReaderStatisticsContract_DoesNotDropPersistenceIdentity()
    {
        var statistics = new ReaderStatisticsDto
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            UserId = "local",
            AssetId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            ChaptersRead = 7,
            TotalReadingTimeSecs = 900,
            WordsRead = 4200,
            SessionsCount = 3,
            AvgWordsPerMinute = 280,
        };

        var json = JsonSerializer.Serialize(statistics, MediaEngineJson.Web);

        Assert.Contains("\"id\":\"33333333-3333-3333-3333-333333333333\"", json, StringComparison.Ordinal);
        Assert.Contains("\"userId\":\"local\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgressContract_PreservesContentHashAndSnakeCaseNames()
    {
        var state = new UserStateResponse(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            "sha256",
            42.5,
            new DateTime(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc),
            new Dictionary<string, string> { ["chapter_index"] = "2" });

        var json = JsonSerializer.Serialize(state, MediaEngineJson.Web);

        Assert.Contains("\"user_id\":\"55555555-5555-5555-5555-555555555555\"", json, StringComparison.Ordinal);
        Assert.Contains("\"content_hash\":\"sha256\"", json, StringComparison.Ordinal);
        Assert.Contains("\"progress_pct\":42.5", json, StringComparison.Ordinal);
        Assert.Contains("\"extended_properties\":{\"chapter_index\":\"2\"}", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardReaderModelFile_ContainsOnlyDeviceLocalPresentationSettings()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "MediaEngine.Web",
            "Models",
            "ViewDTOs",
            "EpubReaderDtos.cs"));

        Assert.Contains("ReaderSettingsDto", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReaderBookmarkDto", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReaderStatisticsDto", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProgressStateDto", source, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            root,
            "src",
            "MediaEngine.Web",
            "Models",
            "ViewDTOs",
            "TextTrackViewModel.cs")));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
