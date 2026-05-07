using System.Text.Json;
using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.Playback;
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
            IDatabaseConnection db,
            ISystemActivityRepository activity,
            ITasteProfiler tasteProfiler,
            CancellationToken ct) =>
        {
            var profile = await svc.GetProfileAsync(id, ct);
            if (profile is null)
                return Results.NotFound($"Profile '{id}' not found.");

            var items = ReadProfileOverviewItems(db, id, limit: 80);
            var recentlyAdded = ReadRecentlyAddedItems(db, limit: 12);
            var libraryCounts = ReadLibraryCounts(db);
            var profileActivity = await activity.GetRecentByProfileAsync(id, 20, ct);
            var taste = await tasteProfiler.GetProfileAsync(id, ct);
            var completedThreshold = 95d;

            var stats = new ProfileOverviewStatsDto
            {
                TotalItems = items.Count,
                InProgress = items.Count(item => item.ProgressPct > 0 && item.ProgressPct < completedThreshold),
                Completed = items.Count(item => item.ProgressPct >= completedThreshold),
                RecentActivity = profileActivity.Count,
                MediaTypeMix = items
                    .GroupBy(item => item.MediaType, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
                LibraryCounts = libraryCounts,
                ActivityBuckets = profileActivity
                    .GroupBy(entry => entry.ActionType, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
                TopGenres = items
                    .Where(item => !string.IsNullOrWhiteSpace(item.Genre))
                    .GroupBy(item => item.Genre!, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(group => group.Count())
                    .Take(8)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
                ConsumedSeconds = items.Sum(EstimateConsumedSeconds),
                ConsumedSecondsByMediaType = items
                    .GroupBy(item => item.MediaType, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Sum(EstimateConsumedSeconds), StringComparer.OrdinalIgnoreCase),
            };

            var response = new ProfileOverviewResponseDto
            {
                Profile = ProfileResponseDto.FromDomain(profile),
                Stats = stats,
                RecentItems = items.Take(12).ToList(),
                ContinueItems = items.Where(item => item.ProgressPct > 0 && item.ProgressPct < completedThreshold).Take(12).ToList(),
                CompletedItems = items.Where(item => item.ProgressPct >= completedThreshold).Take(12).ToList(),
                RecentlyAddedItems = recentlyAdded,
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
                return Results.NotFound($"Profile '{id}' not found.");
            if (string.IsNullOrWhiteSpace(profile.AvatarImagePath) || !File.Exists(profile.AvatarImagePath))
                return Results.NotFound("No avatar image has been uploaded.");

            var bytes = await File.ReadAllBytesAsync(profile.AvatarImagePath, ct);
            return Results.File(bytes, GetAvatarMimeType(profile.AvatarImagePath), Path.GetFileName(profile.AvatarImagePath));
        })
        .WithName("GetProfileAvatar")
        .WithSummary("Serves a profile avatar image.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapPost("/{id:guid}/avatar", async (
            Guid id,
            HttpRequest request,
            IProfileService svc,
            TuvimaDataPaths dataPaths,
            CancellationToken ct) =>
        {
            var profile = await svc.GetProfileAsync(id, ct);
            if (profile is null)
                return Results.NotFound($"Profile '{id}' not found.");
            if (!request.HasFormContentType)
                return Results.BadRequest("Expected multipart form data.");

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest("No file uploaded.");
            if (file.Length > 5 * 1024 * 1024)
                return Results.BadRequest("Avatar image must be 5 MB or smaller.");

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var mimeType = NormalizeAvatarMimeType(file.ContentType, extension);
            if (mimeType is null)
                return Results.BadRequest("Avatar image must be a JPEG, PNG, or WebP image.");

            var zoom = ParseAvatarZoom(form.TryGetValue("zoom", out var zoomValue) ? zoomValue.ToString() : null);

            dataPaths.EnsureRootExists();
            var directory = Path.Combine(dataPaths.Root, "profiles", id.ToString("D"));
            Directory.CreateDirectory(directory);
            var targetPath = Path.Combine(directory, $"avatar{extension}");

            if (!string.IsNullOrWhiteSpace(profile.AvatarImagePath)
                && !string.Equals(profile.AvatarImagePath, targetPath, StringComparison.OrdinalIgnoreCase)
                && File.Exists(profile.AvatarImagePath))
            {
                File.Delete(profile.AvatarImagePath);
            }

            try
            {
                await using var upload = file.OpenReadStream();
                await SaveAvatarImageAsync(upload, targetPath, extension, zoom, ct);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }

            profile.AvatarImagePath = targetPath;
            var updated = await svc.UpdateProfileAsync(profile, ct);
            return updated
                ? Results.Ok(ProfileResponseDto.FromDomain(profile))
                : Results.Problem("Could not update profile avatar.");
        })
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
                return Results.NotFound($"Profile '{id}' not found.");

            var existingPath = profile.AvatarImagePath;
            profile.AvatarImagePath = null;
            var updated = await svc.UpdateProfileAsync(profile, ct);
            if (!updated)
                return Results.Problem("Could not remove profile avatar.");

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

    private static List<ProfileOverviewItemDto> ReadProfileOverviewItems(
        IDatabaseConnection db,
        Guid profileId,
        int limit)
    {
        using var conn = db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                us.asset_id,
                w.id AS work_id,
                w.media_type,
                us.progress_pct,
                us.last_accessed,
                us.extended_properties,
                h.display_name AS collection_name,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key = 'title' LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = COALESCE(gpw.id, pw.id, w.id) AND key = 'title' LIMIT 1),
                    NULLIF(ma.file_path_root, ''),
                    'Untitled') AS title,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = COALESCE(gpw.id, pw.id, w.id) AND key IN ('author', 'artist', 'narrator', 'show_name', 'series') LIMIT 1),
                    h.display_name) AS subtitle,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key = 'media_type' LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = COALESCE(gpw.id, pw.id, w.id) AND key = 'media_type' LIMIT 1),
                    w.media_type,
                    'Media') AS media_type,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key IN ('cover_url', 'cover', 'image_url') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = COALESCE(gpw.id, pw.id, w.id) AND key IN ('cover_url', 'cover', 'image_url') LIMIT 1)) AS cover_url,
                (SELECT value FROM canonical_values WHERE entity_id = COALESCE(gpw.id, pw.id, w.id) AND key = 'genre' LIMIT 1) AS genre
            FROM user_states us
            JOIN media_assets ma ON ma.id = us.asset_id
            JOIN editions e ON e.id = ma.edition_id
            JOIN works w ON w.id = e.work_id
            LEFT JOIN works pw ON pw.id = w.parent_work_id
            LEFT JOIN works gpw ON gpw.id = pw.parent_work_id
            LEFT JOIN collections h ON h.id = w.collection_id
            WHERE us.user_id = @profileId
            ORDER BY us.last_accessed DESC
            LIMIT @limit;
            """;
        cmd.Parameters.AddWithValue("@profileId", profileId.ToString());
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();
        var items = new List<ProfileOverviewItemDto>();
        while (reader.Read())
        {
            var assetId = Guid.Parse(reader.GetString(0));
            var workId = Guid.Parse(reader.GetString(1));
            var mediaType = ReadString(reader, 9) ?? ReadString(reader, 2) ?? "Media";
            var ext = ReadExtendedProperties(ReadString(reader, 5));
            var positionSeconds = ReadDouble(ext, "position_seconds");
            var durationSeconds = ReadDouble(ext, "duration_seconds");

            items.Add(new ProfileOverviewItemDto
            {
                AssetId = assetId,
                WorkId = workId,
                MediaType = mediaType,
                ProgressPct = Math.Clamp(ReadDouble(reader, 3) ?? 0d, 0d, 100d),
                LastAccessed = ReadDateTimeOffset(reader, 4) ?? DateTimeOffset.UtcNow,
                CollectionName = ReadString(reader, 6),
                Title = NormalizeTitle(ReadString(reader, 7)),
                Subtitle = ReadString(reader, 8),
                CoverUrl = ReadString(reader, 10),
                Genre = ReadString(reader, 11),
                PositionSeconds = positionSeconds,
                DurationSeconds = durationSeconds,
                Route = BuildItemRoute(mediaType, assetId, workId),
            });
        }

        return items;
    }

    private static List<ProfileOverviewItemDto> ReadRecentlyAddedItems(
        IDatabaseConnection db,
        int limit)
    {
        using var conn = db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                ma.id AS asset_id,
                w.id AS work_id,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key = 'media_type' LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = COALESCE(gpw.id, pw.id, w.id) AND key = 'media_type' LIMIT 1),
                    w.media_type,
                    'Media') AS media_type,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key = 'title' LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = COALESCE(gpw.id, pw.id, w.id) AND key = 'title' LIMIT 1),
                    NULLIF(ma.file_path_root, ''),
                    'Untitled') AS title,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = COALESCE(gpw.id, pw.id, w.id) AND key IN ('author', 'artist', 'narrator', 'show_name', 'series') LIMIT 1),
                    h.display_name) AS subtitle,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key IN ('cover_url', 'cover', 'image_url') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = COALESCE(gpw.id, pw.id, w.id) AND key IN ('cover_url', 'cover', 'image_url') LIMIT 1)) AS cover_url,
                h.display_name AS collection_name,
                (SELECT value FROM canonical_values WHERE entity_id = COALESCE(gpw.id, pw.id, w.id) AND key = 'genre' LIMIT 1) AS genre,
                COALESCE(MAX(mc.claimed_at), datetime('now')) AS added_at
            FROM media_assets ma
            JOIN editions e ON e.id = ma.edition_id
            JOIN works w ON w.id = e.work_id
            LEFT JOIN works pw ON pw.id = w.parent_work_id
            LEFT JOIN works gpw ON gpw.id = pw.parent_work_id
            LEFT JOIN collections h ON h.id = w.collection_id
            LEFT JOIN metadata_claims mc ON mc.entity_id IN (ma.id, e.id, w.id, COALESCE(gpw.id, pw.id, w.id))
            WHERE COALESCE(w.ownership, 'Owned') = 'Owned'
              AND COALESCE(w.is_catalog_only, 0) = 0
            GROUP BY ma.id, w.id, w.media_type, ma.file_path_root, h.display_name
            ORDER BY added_at DESC
            LIMIT @limit;
            """;
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();
        var items = new List<ProfileOverviewItemDto>();
        while (reader.Read())
        {
            var assetId = Guid.Parse(reader.GetString(0));
            var workId = Guid.Parse(reader.GetString(1));
            var mediaType = ReadString(reader, 2) ?? "Media";
            items.Add(new ProfileOverviewItemDto
            {
                AssetId = assetId,
                WorkId = workId,
                MediaType = mediaType,
                Title = NormalizeTitle(ReadString(reader, 3)),
                Subtitle = ReadString(reader, 4),
                CoverUrl = ReadString(reader, 5),
                CollectionName = ReadString(reader, 6),
                Genre = ReadString(reader, 7),
                LastAccessed = ReadDateTimeOffset(reader, 8) ?? DateTimeOffset.UtcNow,
                AddedAt = ReadDateTimeOffset(reader, 8),
                Route = BuildItemRoute(mediaType, assetId, workId),
            });
        }

        return items;
    }

    private static Dictionary<string, int> ReadLibraryCounts(IDatabaseConnection db)
    {
        using var conn = db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(
                       (SELECT value FROM canonical_values WHERE entity_id = ma.id AND key = 'media_type' LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = w.id AND key = 'media_type' LIMIT 1),
                       w.media_type,
                       'Media') AS media_type,
                   COUNT(DISTINCT ma.id) AS total
            FROM media_assets ma
            JOIN editions e ON e.id = ma.edition_id
            JOIN works w ON w.id = e.work_id
            WHERE COALESCE(w.ownership, 'Owned') = 'Owned'
              AND COALESCE(w.is_catalog_only, 0) = 0
            GROUP BY media_type
            ORDER BY total DESC;
            """;

        using var reader = cmd.ExecuteReader();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            var key = ReadString(reader, 0) ?? "Media";
            counts[key] = reader.GetInt32(1);
        }

        return counts;
    }

    private static double EstimateConsumedSeconds(ProfileOverviewItemDto item)
    {
        if (item.PositionSeconds is > 0)
            return item.PositionSeconds.Value;

        if (item.DurationSeconds is > 0)
            return item.DurationSeconds.Value * Math.Clamp(item.ProgressPct, 0d, 100d) / 100d;

        return 0d;
    }

    private static string? BuildItemRoute(string mediaType, Guid assetId, Guid workId)
    {
        var normalized = mediaType.Trim().ToLowerInvariant();
        if (normalized.Contains("book") || normalized.Contains("epub") || normalized.Contains("comic"))
            return $"/read/{assetId}";
        if (normalized.Contains("audio"))
            return $"/listen/audiobook/{workId}";
        if (normalized.Contains("music"))
            return "/listen/music";
        if (normalized.Contains("movie") || normalized.Contains("show") || normalized.Contains("tv") || normalized.Contains("episode") || normalized.Contains("video"))
            return $"/watch/player/{assetId}";

        return null;
    }

    private static string? NormalizeAvatarMimeType(string? contentType, string extension)
    {
        var normalized = contentType?.Trim().ToLowerInvariant();
        if (normalized is "image/jpeg" or "image/jpg")
            return "image/jpeg";
        if (normalized is "image/png")
            return "image/png";
        if (normalized is "image/webp")
            return "image/webp";

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
            throw new ArgumentException("Avatar image could not be decoded.");

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

    private static Dictionary<string, string> ReadExtendedProperties(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Untitled";

        var fileName = Path.GetFileNameWithoutExtension(value);
        return string.IsNullOrWhiteSpace(fileName) ? value.Trim() : fileName.Trim();
    }

    private static string? ReadString(System.Data.IDataRecord reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static double? ReadDouble(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var raw) && double.TryParse(raw, out var parsed)
            ? parsed
            : null;
    }

    private static double? ReadDouble(System.Data.IDataRecord reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;

        var value = reader.GetValue(ordinal);
        return value switch
        {
            double number => number,
            float number => number,
            int number => number,
            long number => number,
            string raw when double.TryParse(raw, out var parsed) => parsed,
            _ => null,
        };
    }

    private static DateTimeOffset? ReadDateTimeOffset(System.Data.IDataRecord reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;

        var raw = reader.GetValue(ordinal)?.ToString();
        return DateTimeOffset.TryParse(raw, out var parsed) ? parsed : null;
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
