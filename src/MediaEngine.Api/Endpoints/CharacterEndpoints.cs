using MediaEngine.Domain.Contracts;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// Vault character endpoints — character portraits, person character roles,
/// universe character lists, entity assets, and manual Stage 3 trigger.
///
/// <list type="bullet">
/// <item><c>GET  /vault/characters/{fictionalEntityId}/portraits</c> — portraits for a character with actor info</item>
/// <item><c>PUT  /vault/characters/{fictionalEntityId}/portraits/{portraitId}/default</c> — set default portrait</item>
/// <item><c>GET  /vault/persons/{personId}/character-roles</c> — character roles for a person</item>
/// <item><c>GET  /vault/universes/{universeQid}/characters</c> — characters with default actor/portrait</item>
/// <item><c>GET  /vault/assets/{entityId}</c> — entity assets grouped by type</item>
/// <item><c>POST /vault/enrichment/universe/trigger</c> — manual Stage 3 trigger</item>
/// </list>
/// </summary>
public static class CharacterEndpoints
{
    public static IEndpointRouteBuilder MapCharacterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/vault")
                       .WithTags("Vault Characters");

        // GET /vault/characters/{fictionalEntityId}/portraits
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
                    image_url           = p.ImageUrl,
                    is_default          = p.IsDefault,
                });
            }

            return Results.Ok(result);
        });

        // PUT /vault/characters/{fictionalEntityId}/portraits/{portraitId}/default
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

        // GET /vault/persons/{personId}/character-roles
        // Returns all character roles for a person, with portraits and universe info.
        group.MapGet("/persons/{personId:guid}/character-roles", async (
            Guid personId,
            ICharacterPortraitRepository portraitRepo,
            IFictionalEntityRepository entityRepo,
            CancellationToken ct) =>
        {
            var portraits = await portraitRepo.GetByPersonAsync(personId, ct);

            var result = new List<object>(portraits.Count);
            foreach (var p in portraits)
            {
                var entity = await entityRepo.FindByIdAsync(p.FictionalEntityId, ct);
                if (entity is null) continue;

                // Get first work link for display context.
                var workLinks = await entityRepo.GetWorkLinksAsync(p.FictionalEntityId, ct);
                var firstWork = workLinks.FirstOrDefault();

                result.Add(new
                {
                    fictional_entity_id = p.FictionalEntityId,
                    character_name      = entity.Label,
                    portrait_url        = p.ImageUrl,
                    work_title          = firstWork.WorkLabel,
                    is_default          = p.IsDefault,
                    universe_qid        = entity.FictionalUniverseQid,
                    universe_label      = entity.FictionalUniverseLabel,
                });
            }

            return Results.Ok(result);
        });

        // GET /vault/universes/{universeQid}/characters
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
                    portrait_url        = defaultPortrait?.ImageUrl,
                    actor_count         = charPortraits?.Count ?? 0,
                });
            }

            return Results.Ok(result);
        });

        // GET /vault/assets/{entityId}
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

        // POST /vault/enrichment/universe/trigger
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
                    ["requested_by"]  = "vault_manual",
                },
            }, ct);

            return Results.Ok(new { triggered = true, message = "Universe enrichment sweep queued." });
        });

        return app;
    }
}
