using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

public static class PersonEndpoints
{
    public static IEndpointRouteBuilder MapPersonEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/persons")
                       .WithTags("Persons");

        // GET /persons/{id} — person detail including local headshot availability.
        group.MapGet("/{id:guid}", async (
            Guid id,
            IPersonRepository personRepo,
            CancellationToken ct) =>
        {
            var person = await personRepo.FindByIdAsync(id, ct);
            if (person is null)
                return Results.NotFound($"Person '{id}' not found.");

            return Results.Ok(new
            {
                id              = person.Id,
                name            = person.Name,
                role            = person.Role,
                wikidata_qid    = person.WikidataQid,
                headshot_url    = person.HeadshotUrl,
                biography       = person.Biography,
                occupation      = person.Occupation,
                instagram       = person.Instagram,
                twitter         = person.Twitter,
                tiktok          = person.TikTok,
                mastodon        = person.Mastodon,
                website         = person.Website,
                has_local_headshot = !string.IsNullOrEmpty(person.LocalHeadshotPath)
                                    && File.Exists(person.LocalHeadshotPath),
                created_at      = person.CreatedAt,
                enriched_at     = person.EnrichedAt,
            });
        });

        // GET /persons/{id}/headshot — serves headshot.jpg from .people/ folder.
        group.MapGet("/{id:guid}/headshot", async (
            Guid id,
            IPersonRepository personRepo,
            IConfigurationLoader configLoader,
            CancellationToken ct) =>
        {
            var person = await personRepo.FindByIdAsync(id, ct);
            if (person is null)
                return Results.NotFound($"Person '{id}' not found.");

            // Try local headshot path first.
            if (!string.IsNullOrEmpty(person.LocalHeadshotPath)
                && File.Exists(person.LocalHeadshotPath))
            {
                return Results.File(person.LocalHeadshotPath, "image/jpeg");
            }

            // Fallback: check the .people/ directory convention.
            var core = configLoader.LoadCore();
            if (!string.IsNullOrWhiteSpace(core.LibraryRoot))
            {
                var headshotPath = Path.Combine(
                    core.LibraryRoot, ".people", id.ToString(), "headshot.jpg");
                if (File.Exists(headshotPath))
                    return Results.File(headshotPath, "image/jpeg");
            }

            return Results.NotFound("Headshot not available.");
        });

        // GET /persons/by-hub/{hubId} — all persons linked to works in a hub.
        group.MapGet("/by-hub/{hubId:guid}", async (
            Guid hubId,
            IPersonRepository personRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            // Find all media asset IDs for works in this hub.
            var conn = db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT ma.id
                FROM media_assets ma
                JOIN editions e ON e.id = ma.edition_id
                JOIN works w    ON w.id = e.work_id
                WHERE w.hub_id = @hubId;
                """;
            cmd.Parameters.AddWithValue("@hubId", hubId.ToString());

            var assetIds = new List<Guid>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                assetIds.Add(Guid.Parse(reader.GetString(0)));

            // Gather persons linked to those assets (deduplicated).
            var seen = new HashSet<Guid>();
            var persons = new List<object>();
            foreach (var assetId in assetIds)
            {
                var linked = await personRepo.GetByMediaAssetAsync(assetId, ct);
                foreach (var p in linked)
                {
                    if (seen.Add(p.Id))
                    {
                        persons.Add(new
                        {
                            id                 = p.Id,
                            name               = p.Name,
                            role               = p.Role,
                            headshot_url       = p.HeadshotUrl,
                            has_local_headshot = !string.IsNullOrEmpty(p.LocalHeadshotPath)
                                                 && File.Exists(p.LocalHeadshotPath),
                            biography          = p.Biography,
                            occupation         = p.Occupation,
                        });
                    }
                }
            }

            return Results.Ok(persons);
        });

        return app;
    }
}
