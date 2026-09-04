using MediaEngine.Contracts.Profiles;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.PersonalMedia;

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
        Issuer = login.Issuer,
        Subject = login.Subject,
        Email = login.Email,
        DisplayName = login.DisplayName,
        LinkedAt = login.LinkedAt,
        LastLoginAt = login.LastLoginAt,
    };

    internal static ViewProfilePolicyDto ToResponse(ViewProfilePolicy policy) => new()
    {
        ProfileId = policy.ProfileId,
        ViewEnabled = policy.ViewEnabled,
        AccessSharedView = policy.AccessSharedView,
        IncludeInSharedView = policy.IncludeInSharedView,
        AllowGallerySharing = policy.ShareGalleries,
        UpdatedAt = policy.UpdatedAt,
    };

    internal static ViewProfilePolicy ToDomain(
        Guid profileId,
        UpdateViewProfilePolicyRequest request) => new(
            profileId,
            request.ViewEnabled,
            request.AccessSharedView,
            request.IncludeInSharedView,
            request.AllowGallerySharing,
            null);

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
