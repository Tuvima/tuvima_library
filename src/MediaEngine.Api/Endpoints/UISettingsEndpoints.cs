using System.Text.Json;
using MediaEngine.Api.Security;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// UI settings endpoints for the three-tier cascade (Global → Device → Profile).
/// All routes are grouped under <c>/settings/ui</c>.
///
/// <para>
/// Access:
///   Global (read/write) — Administrator only.
///   Device (read) — Administrator or Curator.
///   Device (write) — Administrator only.
///   Profile (read/write) — Administrator or Curator.
///   Resolved — Any authenticated role.
/// </para>
///
/// <list type="bullet">
///   <item><c>GET    /settings/ui/global</c>                — current global UI settings</item>
///   <item><c>PUT    /settings/ui/global</c>                — save global UI settings</item>
///   <item><c>GET    /settings/ui/device/{deviceClass}</c>  — device profile for class</item>
///   <item><c>PUT    /settings/ui/device/{deviceClass}</c>  — save device profile</item>
///   <item><c>GET    /settings/ui/profile/{profileId}</c>   — profile UI preferences</item>
///   <item><c>PUT    /settings/ui/profile/{profileId}</c>   — save profile UI preferences</item>
///   <item><c>GET    /settings/ui/resolved</c>              — fully cascaded output</item>
/// </list>
/// </summary>
public static class UISettingsEndpoints
{
    /// <summary>
    /// Known device classes. Requests for unrecognised classes are rejected with 400.
    /// </summary>
    private static readonly HashSet<string> ValidDeviceClasses =
        new(StringComparer.OrdinalIgnoreCase) { "web", "mobile", "television", "automotive" };

    public static IEndpointRouteBuilder MapUISettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/settings/ui").WithTags("UI Settings");

        // ── GET /settings/ui/global ──────────────────────────────────────────
        grp.MapGet("/global", (IConfigurationLoader configLoader) =>
        {
            var global = configLoader.LoadConfig<UIGlobalSettings>("ui", "global")
                         ?? new UIGlobalSettings();

            return Results.Ok(global);
        })
        .WithName("GetUIGlobalSettings")
        .WithSummary("Returns the current global UI settings (theme, features, layout defaults).")
        .Produces<UIGlobalSettings>(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── PUT /settings/ui/global ──────────────────────────────────────────
        grp.MapPut("/global", (
            UIGlobalSettings         settings,
            IConfigurationLoader     configLoader,
            UISettingsCacheRepository cache) =>
        {
            configLoader.SaveConfig("ui", "global", settings);
            cache.Upsert("global", JsonSerializer.Serialize(settings));

            return Results.Ok(settings);
        })
        .WithName("UpdateUIGlobalSettings")
        .WithSummary("Saves global UI settings to the configuration file and updates the cache.")
        .Produces<UIGlobalSettings>(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── GET /settings/ui/device/{deviceClass} ────────────────────────────
        grp.MapGet("/device/{deviceClass}", (
            string               deviceClass,
            IConfigurationLoader configLoader) =>
        {
            if (!ValidDeviceClasses.Contains(deviceClass))
                return Results.BadRequest(new { error = $"Unknown device class '{deviceClass}'. Valid: web, mobile, television, automotive." });

            var device = configLoader.LoadConfig<UIDeviceProfile>("ui/devices", deviceClass);

            if (device is null)
                return Results.NotFound(new { error = $"No device profile found for '{deviceClass}'." });

            return Results.Ok(device);
        })
        .WithName("GetUIDeviceProfile")
        .WithSummary("Returns the device profile and constraints for a specific device class.")
        .Produces<UIDeviceProfile>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // ── PUT /settings/ui/device/{deviceClass} ────────────────────────────
        grp.MapPut("/device/{deviceClass}", (
            string                   deviceClass,
            UIDeviceProfile          profile,
            IConfigurationLoader     configLoader,
            UISettingsCacheRepository cache) =>
        {
            if (!ValidDeviceClasses.Contains(deviceClass))
                return Results.BadRequest(new { error = $"Unknown device class '{deviceClass}'. Valid: web, mobile, television, automotive." });

            // Ensure the device_class field matches the route parameter.
            profile.DeviceClass = deviceClass;

            configLoader.SaveConfig("ui/devices", deviceClass, profile);
            cache.Upsert($"device:{deviceClass}", JsonSerializer.Serialize(profile));

            return Results.Ok(profile);
        })
        .WithName("UpdateUIDeviceProfile")
        .WithSummary("Saves a device profile to the configuration file and updates the cache.")
        .Produces<UIDeviceProfile>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        // ── GET /settings/ui/profile/{profileId} ─────────────────────────────
        grp.MapGet("/profile/{profileId}", (
            string               profileId,
            IConfigurationLoader configLoader) =>
        {
            var profile = configLoader.LoadConfig<UIProfileSettings>("ui/profiles", profileId);

            if (profile is null)
                return Results.NotFound(new { error = $"No UI profile found for '{profileId}'." });

            return Results.Ok(profile);
        })
        .WithName("GetUIProfileSettings")
        .WithSummary("Returns the UI preferences for a specific user profile.")
        .Produces<UIProfileSettings>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // ── PUT /settings/ui/profile/{profileId} ─────────────────────────────
        grp.MapPut("/profile/{profileId}", (
            string                   profileId,
            UIProfileSettings        settings,
            IConfigurationLoader     configLoader,
            UISettingsCacheRepository cache) =>
        {
            // Ensure the profile_id matches the route parameter.
            settings.ProfileId = profileId;

            configLoader.SaveConfig("ui/profiles", profileId, settings);
            cache.Upsert($"profile:{profileId}", JsonSerializer.Serialize(settings));

            return Results.Ok(settings);
        })
        .WithName("UpdateUIProfileSettings")
        .WithSummary("Saves UI preferences for a user profile to the configuration file and updates the cache.")
        .Produces<UIProfileSettings>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── GET /settings/ui/resolved ────────────────────────────────────────
        grp.MapGet("/resolved", (
            HttpContext              httpContext,
            UISettingsCascadeResolver resolver) =>
        {
            var query       = httpContext.Request.Query;
            var deviceClass = query["device"].FirstOrDefault() ?? "web";
            var profileId   = query["profile"].FirstOrDefault();

            if (!ValidDeviceClasses.Contains(deviceClass))
                return Results.BadRequest(new { error = $"Unknown device class '{deviceClass}'. Valid: web, mobile, television, automotive." });

            var resolved = resolver.Resolve(deviceClass, profileId);

            return Results.Ok(resolved);
        })
        .WithName("GetResolvedUISettings")
        .WithSummary("Returns the fully cascaded UI settings for a device class and optional profile.")
        .Produces<ResolvedUISettings>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAnyRole();

        return app;
    }
}
