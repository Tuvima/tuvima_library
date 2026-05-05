using MediaEngine.Contracts.Playback;

namespace MediaEngine.Api.Services.Playback;

public interface IUserPlaybackSettingsService
{
    Task<UserPlaybackSettingsDto> GetAsync(Guid profileId, CancellationToken ct = default);
    Task<UserPlaybackSettingsDto> GetOrCreateDefaultsAsync(Guid profileId, CancellationToken ct = default);
    Task<UserPlaybackSettingsDto> UpdateAsync(Guid profileId, UserPlaybackSettingsDto settings, CancellationToken ct = default);
}
