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
}
