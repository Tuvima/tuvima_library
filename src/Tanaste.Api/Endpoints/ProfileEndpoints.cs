using Tanaste.Api.Models;
using Tanaste.Api.Security;
using Tanaste.Domain.Aggregates;
using Tanaste.Domain.Enums;
using Tanaste.Identity.Contracts;

namespace Tanaste.Api.Endpoints;

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

        group.MapPost("/", async (
            CreateProfileRequest request,
            IProfileService svc,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName))
                return Results.BadRequest("display_name must not be empty.");

            if (!Enum.TryParse<ProfileRole>(request.Role, ignoreCase: true, out var role))
                return Results.BadRequest(
                    $"Invalid role '{request.Role}'. Must be one of: Administrator, Curator, Consumer.");

            var profile = await svc.CreateProfileAsync(
                request.DisplayName, role, request.AvatarColor, ct);

            return Results.Ok(ProfileResponseDto.FromDomain(profile));
        })
        .WithName("CreateProfile")
        .WithSummary("Create a new user profile.")
        .Produces<ProfileResponseDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
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
                    $"Invalid role '{request.Role}'. Must be one of: Administrator, Curator, Consumer.");

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

        return app;
    }
}
