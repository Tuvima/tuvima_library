using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Services;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// Library character endpoints — character portraits, person character roles,
/// universe character lists, entity assets, and manual Stage 3 trigger.
///
/// <list type="bullet">
/// <item><c>GET  /library/characters/{fictionalEntityId}/portraits</c> — portraits for a character with actor info</item>
/// <item><c>PUT  /library/characters/{fictionalEntityId}/portraits/{portraitId}/default</c> — set default portrait</item>
/// <item><c>GET  /library/persons/{personId}/character-roles</c> — character roles for a person</item>
/// <item><c>GET  /library/universes/{universeQid}/characters</c> — characters with default actor/portrait</item>
/// <item><c>GET  /library/assets/{entityId}</c> — entity assets grouped by type</item>
/// <item><c>POST /library/enrichment/universe/trigger</c> — manual Stage 3 trigger</item>
/// </list>
/// </summary>
public static class CharacterEndpoints
{
    public static IEndpointRouteBuilder MapCharacterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/library")
                       .WithTags("Library Characters");

        // GET /library/portraits/{portraitId}
        // Serves a character portrait from local storage, downloading and caching if needed.
        group.MapGet("/portraits/{portraitId:guid}", async (
            Guid portraitId,
            ICharacterPortraitRepository portraitRepo,
            IHttpClientFactory httpFactory,
            AssetPathService assetPaths,
            CancellationToken ct) =>
        {
            var portrait = await portraitRepo.FindByIdAsync(portraitId, ct);
            if (portrait is null)
                return Results.NotFound($"Portrait '{portraitId}' not found.");

            if (!string.IsNullOrWhiteSpace(portrait.LocalImagePath) && File.Exists(portrait.LocalImagePath))
            {
                var bytes = await File.ReadAllBytesAsync(portrait.LocalImagePath, ct);
                return Results.File(bytes, GetImageMimeType(portrait.LocalImagePath), Path.GetFileName(portrait.LocalImagePath));
            }

            if (string.IsNullOrWhiteSpace(portrait.ImageUrl)
                || !Uri.TryCreate(portrait.ImageUrl, UriKind.Absolute, out var imageUri)
                || (imageUri.Scheme != Uri.UriSchemeHttp && imageUri.Scheme != Uri.UriSchemeHttps))
            {
                return Results.NotFound("Portrait image source not found.");
            }

            using var client = httpFactory.CreateClient("cover_download");
            using var response = await client.GetAsync(imageUri, ct);
            if (!response.IsSuccessStatusCode)
                return Results.NotFound("Portrait image source could not be retrieved.");

            var bytesFromSource = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytesFromSource.Length == 0)
                return Results.NotFound("Portrait image source was empty.");

            var localPath = string.IsNullOrWhiteSpace(portrait.LocalImagePath)
                ? assetPaths.GetCharacterPortraitPath(
                    portrait.PersonId,
                    portrait.FictionalEntityId,
                    InferImageExtension(portrait.ImageUrl))
                : portrait.LocalImagePath;

            AssetPathService.EnsureDirectory(localPath);
            await File.WriteAllBytesAsync(localPath, bytesFromSource, ct);

            portrait.LocalImagePath = localPath;
            portrait.UpdatedAt = DateTimeOffset.UtcNow;
            await portraitRepo.UpsertAsync(portrait, ct);

            var contentType = response.Content.Headers.ContentType?.MediaType ?? GetImageMimeType(localPath);
            return Results.File(bytesFromSource, contentType, Path.GetFileName(localPath));
        });

        // GET /library/characters/{fictionalEntityId}/portraits
        // Returns all portraits for a character, enriched with actor name.
        group.MapGet("/characters/{fictionalEntityId:guid}/portraits", async (
            Guid fictionalEntityId,
            ICharacterPortraitRepository portraitRepo,
            IFictionalEntityRepository entityRepo,
            IPersonRepository personRepo,
            CancellationToken ct) =>
        {
            var portraits = await portraitRepo.GetByCharacterAsync(fictionalEntityId, ct);
            var entity    = await entityRepo.FindByIdAsync(fictionalEntityId, ct);

            var result = new List<object>(portraits.Count);
            foreach (var p in portraits)
            {
                var person = await personRepo.FindByIdAsync(p.PersonId, ct);
                result.Add(new
                {
                    id                  = p.Id,
                    person_id           = p.PersonId,
                    person_name         = person?.Name,
                    fictional_entity_id = p.FictionalEntityId,
                    character_name      = entity?.Label,
                    image_url           = ApiImageUrls.BuildCharacterPortraitUrl(p.Id, p.LocalImagePath, p.ImageUrl),
                    is_default          = p.IsDefault,
                });
            }

            return Results.Ok(result);
        });

        // PUT /library/characters/{fictionalEntityId}/portraits/{portraitId}/default
        // Sets a portrait as the default for its character.
        group.MapPut("/characters/{fictionalEntityId:guid}/portraits/{portraitId:guid}/default", async (
            Guid fictionalEntityId,
            Guid portraitId,
            ICharacterPortraitRepository portraitRepo,
            CancellationToken ct) =>
        {
            // Verify the portrait belongs to this character before setting default.
            var portraits = await portraitRepo.GetByCharacterAsync(fictionalEntityId, ct);
            var match = portraits.FirstOrDefault(p => p.Id == portraitId);
            if (match is null)
                return Results.NotFound($"Portrait '{portraitId}' not found for character '{fictionalEntityId}'.");

            await portraitRepo.SetDefaultAsync(portraitId, ct);
            return Results.Ok(new { portrait_id = portraitId, is_default = true });
        });

        // GET /library/persons/{personId}/character-roles
        // Returns all character roles for a person, with portraits and universe info.
        group.MapGet("/persons/{personId:guid}/character-roles", async (
            Guid personId,
            IPersonRepository personRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var person = await personRepo.FindByIdAsync(personId, ct);
            if (person is null)
                return Results.NotFound($"Person '{personId}' not found.");

            var result = await PersonCreditQueries.GetCharacterRolesAsync(personId, db, ct);
            return Results.Ok(result);
        });

        // GET /library/universes/{universeQid}/characters
        // Returns characters in a universe with default actor/portrait.
        group.MapGet("/universes/{universeQid}/characters", async (
            string universeQid,
            IFictionalEntityRepository entityRepo,
            ICharacterPortraitRepository portraitRepo,
            IPersonRepository personRepo,
            CancellationToken ct) =>
        {
            var characters = await entityRepo.GetByUniverseAndTypeAsync(universeQid, "Character", ct);
            var entityIds  = characters.Select(e => e.Id).ToList();

            // Batch-fetch all portraits for these characters.
            var allPortraits = await portraitRepo.GetByCharacterBatchAsync(entityIds, ct);
            var portraitsByChar = allPortraits.GroupBy(p => p.FictionalEntityId)
                                              .ToDictionary(g => g.Key, g => g.ToList());

            var result = new List<object>(characters.Count);
            foreach (var character in characters)
            {
                portraitsByChar.TryGetValue(character.Id, out var charPortraits);
                var defaultPortrait = charPortraits?.FirstOrDefault(p => p.IsDefault)
                                   ?? charPortraits?.FirstOrDefault();

                string? actorName = null;
                if (defaultPortrait is not null)
                {
                    var actor = await personRepo.FindByIdAsync(defaultPortrait.PersonId, ct);
                    actorName = actor?.Name;
                }

                result.Add(new
                {
                    fictional_entity_id = character.Id,
                    character_name      = character.Label,
                    default_actor_name  = actorName,
                    default_actor_id    = defaultPortrait?.PersonId,
                    portrait_url        = defaultPortrait is null
                        ? null
                        : ApiImageUrls.BuildCharacterPortraitUrl(
                            defaultPortrait.Id,
                            defaultPortrait.LocalImagePath,
                            defaultPortrait.ImageUrl),
                    actor_count         = charPortraits?.Count ?? 0,
                });
            }

            return Results.Ok(result);
        });

        // GET /library/assets/{entityId}
        // Returns all entity assets, grouped by type.
        group.MapGet("/assets/{entityId}", async (
            string entityId,
            IEntityAssetRepository assetRepo,
            CancellationToken ct) =>
        {
            var assets = await assetRepo.GetByEntityAsync(entityId, null, ct);
            var result = assets.Select(a => new
            {
                id              = a.Id,
                entity_id       = a.EntityId,
                asset_type      = a.AssetTypeValue,
                image_url       = a.ImageUrl,
                is_preferred    = a.IsPreferred,
                source_provider = a.SourceProvider,
            });
            return Results.Ok(result);
        });

        // POST /library/enrichment/universe/trigger
        // Manually trigger Stage 3 universe enrichment on the next cycle.
        group.MapPost("/enrichment/universe/trigger", async (
            IServiceProvider sp,
            CancellationToken ct) =>
        {
            // Enqueue a single representative work for immediate processing by setting
            // a short-circuit flag via the harvest queue.  Since UniverseEnrichmentService
            // runs as a BackgroundService we can't call it directly; instead we use
            // IMetadataHarvestingService to enqueue a "universe_sweep" hint.
            var harvesting = sp.GetService<MediaEngine.Domain.Contracts.IMetadataHarvestingService>();
            if (harvesting is null)
                return Results.Ok(new { triggered = false, message = "Harvesting service unavailable." });

            await harvesting.EnqueueAsync(new MediaEngine.Domain.Models.HarvestRequest
            {
                EntityId   = Guid.Empty,
                EntityType = MediaEngine.Domain.Enums.EntityType.Character,
                MediaType  = MediaEngine.Domain.Enums.MediaType.Unknown,
                Hints      = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["trigger_type"]  = "universe_sweep",
                    ["requested_by"]  = "library_manual",
                },
            }, ct);

            return Results.Ok(new { triggered = true, message = "Universe enrichment sweep queued." });
        });

        return app;
    }

    private static string InferImageExtension(string imageUrl)
    {
        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri))
        {
            var uriExtension = Path.GetExtension(imageUri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(uriExtension))
                return uriExtension;
        }

        var directExtension = Path.GetExtension(imageUrl);
        return string.IsNullOrWhiteSpace(directExtension) ? ".jpg" : directExtension;
    }

    private static string GetImageMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/jpeg",
    };
}

