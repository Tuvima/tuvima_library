using MediaEngine.Api.Services.Playback;
using MediaEngine.Contracts.Playback;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Api.Tests;

public sealed class UserPlaybackSettingsServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima-playback-settings-{Guid.NewGuid():N}.db");
    private readonly DatabaseConnection _db;
    private readonly ProfileRepository _profiles;
    private readonly UserPlaybackSettingsService _service;
    private readonly Guid _profileId = Guid.NewGuid();
    private readonly Guid _otherProfileId = Guid.NewGuid();

    public UserPlaybackSettingsServiceTests()
    {
        _db = new DatabaseConnection(_dbPath);
        _db.Open();
        _db.InitializeSchema();
        _db.RunStartupChecks();
        _profiles = new ProfileRepository(_db);
        _profiles.InsertAsync(new Profile
        {
            Id = _profileId,
            DisplayName = "Playback Test",
            AvatarColor = "#7C4DFF",
            Role = ProfileRole.Consumer,
            CreatedAt = DateTimeOffset.UtcNow,
        }).GetAwaiter().GetResult();
        _profiles.InsertAsync(new Profile
        {
            Id = _otherProfileId,
            DisplayName = "Other Profile",
            AvatarColor = "#C9922E",
            Role = ProfileRole.Consumer,
            CreatedAt = DateTimeOffset.UtcNow,
        }).GetAwaiter().GetResult();
        _service = new UserPlaybackSettingsService(_db, _profiles);
    }

    [Fact]
    public async Task GetAsync_ReturnsDefaults_WhenSettingsDoNotExist()
    {
        var settings = await _service.GetAsync(_profileId);

        Assert.Equal(_profileId, settings.ProfileId);
        Assert.True(settings.General.ResumePlayback);
        Assert.Equal(1.0m, settings.Watching.DefaultPlaybackSpeed);
        Assert.Equal(1.25m, settings.Listening.AudiobookDefaultSpeed);
        Assert.Equal("Sepia", settings.Reading.Theme);
    }

    [Fact]
    public async Task UpdateAsync_PersistsProfileScopedSettings()
    {
        var settings = await _service.GetAsync(_profileId);
        settings.Watching.DefaultPlaybackSpeed = 1.5m;
        settings.Reading.Theme = PlaybackPreferenceValues.Dark;

        await _service.UpdateAsync(_profileId, settings);

        var saved = await _service.GetAsync(_profileId);
        var other = await _service.GetAsync(_otherProfileId);
        Assert.Equal(1.5m, saved.Watching.DefaultPlaybackSpeed);
        Assert.Equal(PlaybackPreferenceValues.Dark, saved.Reading.Theme);
        Assert.Equal(1.0m, other.Watching.DefaultPlaybackSpeed);
    }

    [Fact]
    public async Task UpdateAsync_RejectsInvalidSpeedThresholdAndEnumValues()
    {
        var settings = await _service.GetAsync(_profileId);

        settings.Watching.DefaultPlaybackSpeed = 2.5m;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.UpdateAsync(_profileId, settings));

        settings = await _service.GetAsync(_profileId);
        settings.General.MarkCompleteThresholdPercent = 25;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.UpdateAsync(_profileId, settings));

        settings = await _service.GetAsync(_profileId);
        settings.Reading.Theme = "Neon";
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(_profileId, settings));
    }

    [Fact]
    public async Task UpdateAsync_RejectsMismatchedProfileId()
    {
        var settings = await _service.GetAsync(_profileId);
        settings.ProfileId = _otherProfileId;

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(_profileId, settings));
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
