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
                            wikidata_qid       = p.WikidataQid,
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


        // GET /persons/{id}/works — all hubs containing works by this person.
        group.MapGet("/{id:guid}/works", async (
            Guid id,
            IPersonRepository personRepo,
            IHubRepository hubRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var person = await personRepo.FindByIdAsync(id, ct);
            if (person is null)
                return Results.NotFound($"Person '{id}' not found.");

            // Find all hub IDs linked to this person via person_media_links.
            using var conn = db.CreateConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = """
                SELECT DISTINCT w.hub_id
                FROM person_media_links pml
                JOIN media_assets ma ON ma.id = pml.media_asset_id
                JOIN editions e      ON e.id  = ma.edition_id
                JOIN works w         ON w.id  = e.work_id
                WHERE pml.person_id = @personId
                  AND w.hub_id IS NOT NULL;
                """;
            cmd.Parameters.AddWithValue("@personId", id.ToString());

            var hubIds = new HashSet<Guid>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                hubIds.Add(Guid.Parse(reader.GetString(0)));

            if (hubIds.Count == 0)
                return Results.Ok(Array.Empty<MediaEngine.Api.Models.HubDto>());

            // Load full hub data and filter to matching IDs.
            var allHubs = await hubRepo.GetAllAsync(ct);
            var dtos    = allHubs
                .Where(h => hubIds.Contains(h.Id))
                .Select(MediaEngine.Api.Models.HubDto.FromDomain)
                .ToList();

            return Results.Ok(dtos);
        })
        .WithName("GetWorksByPerson")
        .WithSummary("All hubs containing works linked to this person (author/narrator/director).")
        .Produces<List<MediaEngine.Api.Models.HubDto>>(StatusCodes.Status200OK);
        // GET /persons?role=Author&limit=50 -- list persons filtered by role.
        group.MapGet("/", async (
            string? role,
            int? limit,
            IPersonRepository personRepo,
            CancellationToken ct) =>
        {
            var all = await personRepo.ListAllAsync(ct);
            IEnumerable<MediaEngine.Domain.Entities.Person> filtered = all;

            if (!string.IsNullOrEmpty(role))
                filtered = filtered.Where(p => p.Role.Equals(role, StringComparison.OrdinalIgnoreCase));

            var results = filtered
                .Take(limit ?? 50)
                .Select(p => new
                {
                    id                 = p.Id,
                    name               = p.Name,
                    role               = p.Role,
                    wikidata_qid       = p.WikidataQid,
                    headshot_url       = p.HeadshotUrl,
                    has_local_headshot = !string.IsNullOrEmpty(p.LocalHeadshotPath)
                                         && File.Exists(p.LocalHeadshotPath),
                    biography          = p.Biography,
                    occupation         = p.Occupation,
                })
                .ToList();

            return Results.Ok(results);
        })
        .WithName("ListPersons")
        .WithSummary("List persons, optionally filtered by role.");

        return app;
    }
}
