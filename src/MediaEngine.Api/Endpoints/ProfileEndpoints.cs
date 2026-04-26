using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Identity.Contracts;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// Profile management endpoints.
///
/// Routes under <c>/profiles</c>:
///   GET    /profiles         — list all profiles
///   GET    /profiles/{id}    — get a single profile
///   POST   /profiles         — create a new profile
///   PUT    /profiles/{id}    — update an existing profile
///   DELETE /profiles/{id}    — delete a profile (cannot delete seed or last admin)
///
/// Spec: Settings & Management Layer — Identity & Multi-User.
/// </summary>
public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/profiles").WithTags("Profiles");

        group.MapGet("/", async (
            IProfileService svc,
            CancellationToken ct) =>
        {
            var profiles = await svc.GetAllProfilesAsync(ct);
            var dtos = profiles.Select(ProfileResponseDto.FromDomain).ToList();
            return Results.Ok(dtos);
        })
        .WithName("ListProfiles")
        .WithSummary("List all user profiles.")
        .Produces<List<ProfileResponseDto>>(StatusCodes.Status200OK)
        .RequireAdmin();

        group.MapGet("/{id:guid}", async (
            Guid id,
            IProfileService svc,
            CancellationToken ct) =>
        {
            var profile = await svc.GetProfileAsync(id, ct);
            return profile is null
                ? Results.NotFound($"Profile '{id}' not found.")
                : Results.Ok(ProfileResponseDto.FromDomain(profile));
        })
        .WithName("GetProfile")
        .WithSummary("Get a single profile by ID.")
        .Produces<ProfileResponseDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdmin();

        group.MapGet("/{id:guid}/taste", async (
            Guid id,
            IProfileService svc,
            ITasteProfiler tasteProfiler,
            CancellationToken ct) =>
        {
            var profile = await svc.GetProfileAsync(id, ct);
            if (profile is null)
                return Results.NotFound($"Profile '{id}' not found.");

            var taste = await tasteProfiler.GetProfileAsync(id, ct);
            return Results.Ok(taste);
        })
        .WithName("GetProfileTaste")
        .WithSummary("Get the computed taste profile for a user profile.")
        .Produces<TasteProfile>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{id:guid}/overview", async (
            Guid id,
            IProfileService svc,
            IUserStateStore userStates,
            IMediaAssetRepository mediaAssets,
            IWorkRepository works,
            ICanonicalValueRepository canonicalValues,
            ISystemActivityRepository activity,
            ITasteProfiler tasteProfiler,
            CancellationToken ct) =>
        {
            var profile = await svc.GetProfileAsync(id, ct);
            if (profile is null)
                return Results.NotFound($"Profile '{id}' not found.");

            var states = await userStates.GetRecentAsync(id, 50, ct);
            var items = new List<ProfileOverviewItemDto>();
            foreach (var state in states)
            {
                var asset = await mediaAssets.FindByIdAsync(state.AssetId, ct);
                var lineage = await works.GetLineageByAssetAsync(state.AssetId, ct);

                var canonicalIds = new List<Guid> { state.AssetId };
                if (lineage is not null)
                {
                    canonicalIds.Add(lineage.WorkId);
                    canonicalIds.Add(lineage.RootParentWorkId);
                }

                var canonical = await canonicalValues.GetByEntitiesAsync(canonicalIds.Distinct().ToList(), ct);
                var ownValues = canonical.TryGetValue(state.AssetId, out var assetValues) ? assetValues : [];
                var workValues = lineage is not null && canonical.TryGetValue(lineage.WorkId, out var directWorkValues) ? directWorkValues : [];
                var rootValues = lineage is not null && canonical.TryGetValue(lineage.RootParentWorkId, out var rootWorkValues) ? rootWorkValues : [];

                var title = FirstCanonical("title", ownValues, workValues, rootValues)
                    ?? Path.GetFileNameWithoutExtension(asset?.FilePathRoot)
                    ?? "Untitled";
                var subtitle = FirstCanonical("author", ownValues, workValues, rootValues)
                    ?? FirstCanonical("artist", ownValues, workValues, rootValues)
                    ?? FirstCanonical("show_name", rootValues, workValues, ownValues)
                    ?? FirstCanonical("collection", rootValues, workValues, ownValues);
                var mediaType = FirstCanonical("media_type", ownValues, workValues, rootValues)
                    ?? lineage?.MediaType.ToString()
                    ?? "Media";
                var cover = FirstCanonical("cover_url", ownValues, workValues, rootValues)
                    ?? FirstCanonical("cover", ownValues, workValues, rootValues)
                    ?? FirstCanonical("image_url", ownValues, workValues, rootValues);

                items.Add(new ProfileOverviewItemDto
                {
                    AssetId = state.AssetId,
                    WorkId = lineage?.WorkId,
                    Title = title,
                    Subtitle = subtitle,
                    MediaType = mediaType,
                    CoverUrl = cover,
                    ProgressPct = Math.Clamp(state.ProgressPct, 0, 100),
                    LastAccessed = state.LastAccessed,
                });
            }

            var profileActivity = await activity.GetRecentByProfileAsync(id, 20, ct);
            var taste = await tasteProfiler.GetProfileAsync(id, ct);

            var stats = new ProfileOverviewStatsDto
            {
                TotalItems = items.Count,
                InProgress = items.Count(item => item.ProgressPct > 0 && item.ProgressPct < 95),
                Completed = items.Count(item => item.ProgressPct >= 95),
                RecentActivity = profileActivity.Count,
                MediaTypeMix = items
                    .GroupBy(item => item.MediaType, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            };

            var response = new ProfileOverviewResponseDto
            {
                Profile = ProfileResponseDto.FromDomain(profile),
                Stats = stats,
                RecentItems = items.Take(12).ToList(),
                ContinueItems = items.Where(item => item.ProgressPct > 0 && item.ProgressPct < 95).Take(12).ToList(),
                CompletedItems = items.Where(item => item.ProgressPct >= 95).Take(12).ToList(),
                Activity = profileActivity.Select(entry => new ProfileOverviewActivityDto
                {
                    Id = entry.Id,
                    OccurredAt = entry.OccurredAt,
                    ActionType = entry.ActionType,
                    Detail = entry.Detail,
                    EntityId = entry.EntityId,
                }).ToList(),
                Taste = taste,
            };

            return Results.Ok(response);
        })
        .WithName("GetProfileOverview")
        .WithSummary("Get user-facing profile details, history, statistics, and taste signals.")
        .Produces<ProfileOverviewResponseDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{id:guid}/external-logins", async (
            Guid id,
            IProfileService profileService,
            IProfileExternalLoginService loginService,
            CancellationToken ct) =>
        {
            var profile = await profileService.GetProfileAsync(id, ct);
            if (profile is null)
                return Results.NotFound($"Profile '{id}' not found.");

            var logins = await loginService.GetByProfileAsync(id, ct);
            return Results.Ok(logins.Select(ProfileExternalLoginDto.FromDomain).ToList());
        })
        .WithName("ListProfileExternalLogins")
        .WithSummary("List SSO/OAuth sign-in accounts linked to a profile.")
        .Produces<List<ProfileExternalLoginDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdmin();

        group.MapPost("/", async (
            CreateProfileRequest request,
            IProfileService svc,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName))
                return Results.BadRequest("display_name must not be empty.");

            if (!Enum.TryParse<ProfileRole>(request.Role, ignoreCase: true, out var role))
                return Results.BadRequest(
                    $"Invalid role '{request.Role}'. Must be one of: {string.Join(", ", AppRoles.All)}.");

            var profile = await svc.CreateProfileAsync(
                request.DisplayName, role, request.AvatarColor, ct);

            return Results.Ok(ProfileResponseDto.FromDomain(profile));
        })
        .WithName("CreateProfile")
        .WithSummary("Create a new user profile.")
        .Produces<ProfileResponseDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        group.MapPost("/{id:guid}/external-logins", async (
            Guid id,
            LinkProfileExternalLoginRequest request,
            IProfileExternalLoginService loginService,
            CancellationToken ct) =>
        {
            try
            {
                var login = await loginService.LinkAsync(
                    id,
                    request.Provider,
                    request.Subject,
                    request.Email,
                    request.DisplayName,
                    ct);

                return Results.Ok(ProfileExternalLoginDto.FromDomain(login));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? Results.NotFound(ex.Message)
                    : Results.Conflict(ex.Message);
            }
        })
        .WithName("LinkProfileExternalLogin")
        .WithSummary("Link an external SSO/OAuth account to a local profile.")
        .Produces<ProfileExternalLoginDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAdmin();

        group.MapMethods("/{id:guid}", ["PUT"], async (
            Guid id,
            UpdateProfileRequest request,
            IProfileService svc,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName))
                return Results.BadRequest("display_name must not be empty.");

            var existing = await svc.GetProfileAsync(id, ct);
            if (existing is null)
                return Results.NotFound($"Profile '{id}' not found.");

            if (!Enum.TryParse<ProfileRole>(request.Role, ignoreCase: true, out var role))
                return Results.BadRequest(
                    $"Invalid role '{request.Role}'. Must be one of: {string.Join(", ", AppRoles.All)}.");

            existing.DisplayName = request.DisplayName.Trim();
            existing.AvatarColor = string.IsNullOrWhiteSpace(request.AvatarColor)
                ? existing.AvatarColor
                : request.AvatarColor.Trim();
            existing.Role = role;
            existing.NavigationConfig = request.NavigationConfig;

            var updated = await svc.UpdateProfileAsync(existing, ct);
            return updated
                ? Results.Ok(ProfileResponseDto.FromDomain(existing))
                : Results.Problem("Could not update profile.");
        })
        .WithName("UpdateProfile")
        .WithSummary("Update an existing profile's display name, avatar color, and role.")
        .Produces<ProfileResponseDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdmin();

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IProfileService svc,
            CancellationToken ct) =>
        {
            var deleted = await svc.DeleteProfileAsync(id, ct);
            return deleted
                ? Results.NoContent()
                : Results.BadRequest(
                    "Cannot delete this profile. It may be the seed profile or the last Administrator.");
        })
        .WithName("DeleteProfile")
        .WithSummary("Delete a profile. Cannot delete the seed Owner profile or the last Administrator.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        group.MapDelete("/external-logins/{loginId:guid}", async (
            Guid loginId,
            IProfileExternalLoginService loginService,
            CancellationToken ct) =>
        {
            var deleted = await loginService.UnlinkAsync(loginId, ct);
            return deleted
                ? Results.NoContent()
                : Results.NotFound($"External login '{loginId}' not found.");
        })
        .WithName("UnlinkProfileExternalLogin")
        .WithSummary("Unlink an external SSO/OAuth account from its local profile.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdmin();

        return app;
    }

    private static string? FirstCanonical(
        string key,
        params IReadOnlyList<Domain.Entities.CanonicalValue>[] groups)
    {
        foreach (var group in groups)
        {
            var value = group.FirstOrDefault(item =>
                string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }
}
