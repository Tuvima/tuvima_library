using MediaEngine.Contracts.Profiles;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;

namespace MediaEngine.Api.Services.ReadServices;

internal static class ProfileContractMapper
{
    internal static ProfileResponseDto ToResponse(Profile profile) => new()
    {
        Id = profile.Id,
        DisplayName = profile.DisplayName,
        AvatarColor = profile.AvatarColor,
        Role = profile.Role.ToString(),
        CreatedAt = profile.CreatedAt,
        NavigationConfig = profile.NavigationConfig,
        AvatarImageUrl = string.IsNullOrWhiteSpace(profile.AvatarImagePath)
            ? null
            : $"/profiles/{profile.Id:D}/avatar",
    };

    internal static ProfileExternalLoginDto ToResponse(ProfileExternalLogin login) => new()
    {
        Id = login.Id,
        ProfileId = login.ProfileId,
        Provider = login.Provider,
        Subject = login.Subject,
        Email = login.Email,
        DisplayName = login.DisplayName,
        LinkedAt = login.LinkedAt,
        LastLoginAt = login.LastLoginAt,
    };

    internal static TasteProfileDto ToResponse(TasteProfile profile) => new()
    {
        UserId = profile.UserId,
        GenreDistribution = profile.GenreDistribution,
        EraPreferences = profile.EraPreferences,
        MediaTypeMix = profile.MediaTypeMix,
        MoodPreferences = profile.MoodPreferences,
        Summary = profile.Summary,
        LastUpdatedAt = profile.LastUpdatedAt,
    };

    internal static TasteProfileBuildResponse ToResponse(TasteProfileBuildResult result) => new(
        (TasteProfileBuildStatusDto)result.Status,
        result.UserId,
        result.Profile is null ? null : ToResponse(result.Profile),
        result.SignalCount,
        result.InputFingerprint,
        result.Reason);
}
