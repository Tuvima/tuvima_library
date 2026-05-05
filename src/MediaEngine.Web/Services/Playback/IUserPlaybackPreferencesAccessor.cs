using MediaEngine.Contracts.Playback;

namespace MediaEngine.Web.Services.Playback;

public interface IUserPlaybackPreferencesAccessor
{
    Task<UserPlaybackSettingsDto?> GetAsync(CancellationToken ct = default);
    void UpdateCache(UserPlaybackSettingsDto settings);
    void Invalidate();
}
