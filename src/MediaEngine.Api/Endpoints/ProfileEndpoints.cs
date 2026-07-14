using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.Playback;
using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Contracts.Playback;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Identity.Contracts;
using MediaEngine.Storage.Contracts;
using SkiaSharp;

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
            {
                return Results.NotFound($"Profile '{id}' not found.");
            }

            var taste = await tasteProfiler.GetProfileAsync(id, ct);
            return Results.Ok(taste);
        })
        .WithName("GetProfileTaste")
        .WithSummary("Get the computed taste profile for a user profile.")
        .Produces<TasteProfileBuildResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{id:guid}/overview", async (
            Guid id,
            IProfileOverviewReadService overviewReadService,
            CancellationToken ct) =>
        {
            var response = await overviewReadService.GetOverviewAsync(id, ct);
            if (response is null)
            {
                return Results.NotFound($"Profile '{id}' not found.");
            }

            return Results.Ok(response);
        })
        .WithName("GetProfileOverview")
        .WithSummary("Get user-facing profile details, history, statistics, and taste signals.")
        .Produces<ProfileOverviewResponseDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{id:guid}/settings/playback", async (
            Guid id,
            IUserPlaybackSettingsService settingsService,
            CancellationToken ct) =>
        {
            try
            {
                var settings = await settingsService.GetOrCreateDefaultsAsync(id, ct);
                return Results.Ok(settings);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        })
        .WithName("GetProfilePlaybackSettings")
        .WithSummary("Get user playback and reading settings for a profile.")
        .Produces<UserPlaybackSettingsDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapPut("/{id:guid}/settings/playback", async (
            Guid id,
            UserPlaybackSettingsDto request,
            IUserPlaybackSettingsService settingsService,
            CancellationToken ct) =>
        {
            try
            {
                var saved = await settingsService.UpdateAsync(id, request, ct);
                return Results.Ok(saved);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("UpdateProfilePlaybackSettings")
        .WithSummary("Save user playback and reading settings for a profile.")
        .Produces<UserPlaybackSettingsDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{id:guid}/avatar", async (
            Guid id,
            IProfileService svc,
            CancellationToken ct) =>
        {
            var profile = await svc.GetProfileAsync(id, ct);
            if (profile is null)
            {
                return Results.NotFound($"Profile '{id}' not found.");
            }

            if (string.IsNullOrWhiteSpace(profile.AvatarImagePath) || !File.Exists(profile.AvatarImagePath))
            {
                return Results.NotFound("No avatar image has been uploaded.");
            }

            var bytes = await File.ReadAllBytesAsync(profile.AvatarImagePath, ct);
            return Results.File(bytes, GetAvatarMimeType(profile.AvatarImagePath), Path.GetFileName(profile.AvatarImagePath));
        })
        .WithName("GetProfileAvatar")
        .WithSummary("Serves a profile avatar image.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapPost("/{id:guid}/avatar", UploadProfileAvatarAsync)
        .WithName("UploadProfileAvatar")
        .WithSummary("Uploads and stores a profile avatar image.")
        .DisableAntiforgery()
        .Produces<ProfileResponseDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapDelete("/{id:guid}/avatar", async (
            Guid id,
            IProfileService svc,
            CancellationToken ct) =>
        {
            var profile = await svc.GetProfileAsync(id, ct);
            if (profile is null)
            {
                return Results.NotFound($"Profile '{id}' not found.");
            }

            var existingPath = profile.AvatarImagePath;
            profile.AvatarImagePath = null;
            var updated = await svc.UpdateProfileAsync(profile, ct);
            if (!updated)
            {
                return Results.Problem("Could not remove profile avatar.");
            }

            if (!string.IsNullOrWhiteSpace(existingPath) && File.Exists(existingPath))
            {
                File.Delete(existingPath);
            }

            return Results.Ok(ProfileResponseDto.FromDomain(profile));
        })
        .WithName("RemoveProfileAvatar")
        .WithSummary("Removes the uploaded avatar image for a profile.")
        .Produces<ProfileResponseDto>(StatusCodes.Status200OK)
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
            {
                return Results.NotFound($"Profile '{id}' not found.");
            }

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
            {
                return Results.BadRequest("display_name must not be empty.");
            }

            if (!Enum.TryParse<ProfileRole>(request.Role, ignoreCase: true, out var role))
            {
                return Results.BadRequest(
                    $"Invalid role '{request.Role}'. Must be one of: {string.Join(", ", AppRoles.All)}.");
            }

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
            {
                return Results.BadRequest("display_name must not be empty.");
            }

            var existing = await svc.GetProfileAsync(id, ct);
            if (existing is null)
            {
                return Results.NotFound($"Profile '{id}' not found.");
            }

            if (!Enum.TryParse<ProfileRole>(request.Role, ignoreCase: true, out var role))
            {
                return Results.BadRequest(
                    $"Invalid role '{request.Role}'. Must be one of: {string.Join(", ", AppRoles.All)}.");
            }

            existing.DisplayName = request.DisplayName.Trim();
            existing.AvatarColor = string.IsNullOrWhiteSpace(request.AvatarColor)
                ? existing.AvatarColor
                : request.AvatarColor.Trim();
            existing.Role = role;
            existing.NavigationConfig = request.NavigationConfig;

            var updated = await svc.UpdateProfileAsync(existing, ct);
            return updated
                ? Results.Ok(ProfileResponseDto.FromDomain(existing))
                : Results.BadRequest(
                    "Cannot demote the seed Owner or the last Administrator profile.");
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

    internal static async Task<IResult> UploadProfileAvatarAsync(
        Guid id,
        HttpRequest request,
        IProfileService svc,
        TuvimaDataPaths dataPaths,
        CancellationToken ct)
    {
        var profile = await svc.GetProfileAsync(id, ct);
        if (profile is null)
        {
            return Results.NotFound($"Profile '{id}' not found.");
        }

        if (!request.HasFormContentType)
        {
            return Results.BadRequest("Expected multipart form data.");
        }

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest("No file uploaded.");
        }

        if (file.Length > 5 * 1024 * 1024)
        {
            return Results.BadRequest("Avatar image must be 5 MB or smaller.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var mimeType = NormalizeAvatarMimeType(file.ContentType, extension);
        if (mimeType is null)
        {
            return Results.BadRequest("Avatar image must be a JPEG, PNG, or WebP image.");
        }

        var zoom = ParseAvatarZoom(form.TryGetValue("zoom", out var zoomValue) ? zoomValue.ToString() : null);

        dataPaths.EnsureRootExists();
        var directory = Path.Combine(dataPaths.Root, "profiles", id.ToString("D"));
        Directory.CreateDirectory(directory);
        var replacementPath = Path.Combine(directory, $"avatar-{Guid.NewGuid():N}{extension}");
        var existingPath = profile.AvatarImagePath;
        var committed = false;

        try
        {
            await using var upload = file.OpenReadStream();
            await SaveAvatarImageAsync(upload, replacementPath, extension, zoom, ct);

            profile.AvatarImagePath = replacementPath;
            if (!await svc.UpdateProfileAsync(profile, ct))
            {
                return Results.Problem("Could not update profile avatar.");
            }

            committed = true;
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
        finally
        {
            if (!committed)
            {
                profile.AvatarImagePath = existingPath;
                if (File.Exists(replacementPath))
                {
                    File.Delete(replacementPath);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(existingPath)
            && !string.Equals(existingPath, replacementPath, StringComparison.OrdinalIgnoreCase)
            && File.Exists(existingPath))
        {
            File.Delete(existingPath);
        }

        return Results.Ok(ProfileResponseDto.FromDomain(profile));
    }

    private static string? NormalizeAvatarMimeType(string? contentType, string extension)
    {
        var normalized = contentType?.Trim().ToLowerInvariant();
        if (normalized is "image/jpeg" or "image/jpg")
        {
            return "image/jpeg";
        }

        if (normalized is "image/png")
        {
            return "image/png";
        }

        if (normalized is "image/webp")
        {
            return "image/webp";
        }

        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => null,
        };
    }

    private static string GetAvatarMimeType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg",
        };

    private static float ParseAvatarZoom(string? value)
    {
        if (!float.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var zoom))
        {
            return 1f;
        }

        return Math.Clamp(zoom, 1f, 3f);
    }

    private static async Task SaveAvatarImageAsync(
        Stream upload,
        string targetPath,
        string extension,
        float zoom,
        CancellationToken ct)
    {
        using var input = new MemoryStream();
        await upload.CopyToAsync(input, ct);
        input.Position = 0;

        using var bitmap = SKBitmap.Decode(input);
        if (bitmap is null)
        {
            throw new ArgumentException("Avatar image could not be decoded.");
        }

        var cropSize = Math.Max(1, (int)MathF.Round(Math.Min(bitmap.Width, bitmap.Height) / zoom));
        var cropLeft = Math.Max(0, (bitmap.Width - cropSize) / 2);
        var cropTop = Math.Max(0, (bitmap.Height - cropSize) / 2);
        var source = new SKRectI(cropLeft, cropTop, cropLeft + cropSize, cropTop + cropSize);
        var destination = new SKRect(0, 0, 512, 512);

        using var surface = SKSurface.Create(new SKImageInfo(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true };
        using var imageSource = SKImage.FromBitmap(bitmap);
        surface.Canvas.DrawImage(
            imageSource,
            new SKRect(source.Left, source.Top, source.Right, source.Bottom),
            destination,
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            paint);

        using var image = surface.Snapshot();
        using var data = image.Encode(GetAvatarEncodeFormat(extension), 92);
        await using var output = File.Create(targetPath);
        data.SaveTo(output);
    }

    private static SKEncodedImageFormat GetAvatarEncodeFormat(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".png" => SKEncodedImageFormat.Png,
            ".webp" => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Jpeg,
        };

}
