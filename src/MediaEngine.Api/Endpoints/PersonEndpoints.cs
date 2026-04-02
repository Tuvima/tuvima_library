using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Services;
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
                roles           = person.Roles,
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
                is_pseudonym    = person.IsPseudonym,
                created_at      = person.CreatedAt,
                enriched_at     = person.EnrichedAt,
            });
        });

        // GET /persons/{id}/aliases — linked pseudonym and real-person entries.
        group.MapGet("/{id:guid}/aliases", async (
            Guid id,
            IPersonRepository personRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var person = await personRepo.FindByIdAsync(id, ct);
            if (person is null)
                return Results.NotFound($"Person '{id}' not found.");

            var conn = db.Open();
            var aliases = new List<object>();

            if (person.IsPseudonym)
            {
                // This is a pen name — find the real people behind it
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT real_person_id FROM person_aliases
                    WHERE pseudonym_person_id = @id
                    """;
                cmd.Parameters.AddWithValue("@id", id.ToString());
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var realId = Guid.Parse(reader.GetString(0));
                    var realPerson = await personRepo.FindByIdAsync(realId, ct);
                    if (realPerson is not null)
                        aliases.Add(new
                        {
                            id = realPerson.Id,
                            name = realPerson.Name,
                            roles = realPerson.Roles,
                            headshot_url = realPerson.HeadshotUrl,
                            is_pseudonym = realPerson.IsPseudonym,
                            wikidata_qid = realPerson.WikidataQid,
                            relationship = "real_person",
                        });
                }
            }
            else
            {
                // This is a real person — find pen names that point to them
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT pseudonym_person_id FROM person_aliases
                    WHERE real_person_id = @id
                    """;
                cmd.Parameters.AddWithValue("@id", id.ToString());
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var pseudonymId = Guid.Parse(reader.GetString(0));
                    var pseudonymPerson = await personRepo.FindByIdAsync(pseudonymId, ct);
                    if (pseudonymPerson is not null)
                        aliases.Add(new
                        {
                            id = pseudonymPerson.Id,
                            name = pseudonymPerson.Name,
                            roles = pseudonymPerson.Roles,
                            headshot_url = pseudonymPerson.HeadshotUrl,
                            is_pseudonym = pseudonymPerson.IsPseudonym,
                            wikidata_qid = pseudonymPerson.WikidataQid,
                            relationship = "pen_name",
                        });
                }
            }

            return Results.Ok(new
            {
                person_id = id,
                person_name = person.Name,
                is_pseudonym = person.IsPseudonym,
                aliases,
            });
        })
        .WithName("GetPersonAliases")
        .WithSummary("Linked pseudonym and real-person entries for a given person.");

        // GET /persons/{id}/headshot — serves headshot.jpg from the person image directory.
        // Checks .data/images/people/{QID}/ (via ImagePathService) first, then falls back
        // to legacy .people/ conventions. Downloads and caches if no local file exists.
        group.MapGet("/{id:guid}/headshot", async (
            Guid id,
            IPersonRepository personRepo,
            IConfigurationLoader configLoader,
            IHttpClientFactory httpFactory,
            ImagePathService? imagePaths,
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

            // Check the centralized .data/images/people/{QID}/ path first.
            if (imagePaths is not null && !string.IsNullOrWhiteSpace(person.WikidataQid))
            {
                var newStylePath = Path.Combine(imagePaths.GetPersonImageDir(person.WikidataQid), "headshot.jpg");
                if (File.Exists(newStylePath))
                {
                    await personRepo.UpdateLocalHeadshotPathAsync(id, newStylePath, ct);
                    return Results.File(newStylePath, "image/jpeg");
                }
            }

            // Legacy fallback: check the .people/ directory convention.
            var core = configLoader.LoadCore();
            if (!string.IsNullOrWhiteSpace(core.LibraryRoot))
            {
                // Try Name (QID) folder naming (legacy)
                if (!string.IsNullOrWhiteSpace(person.WikidataQid) && !string.IsNullOrWhiteSpace(person.Name))
                {
                    var sanitizedName = string.Join("_", person.Name.Split(Path.GetInvalidFileNameChars()));
                    var namedFolder = Path.Combine(core.LibraryRoot, ".people",
                        $"{sanitizedName} ({person.WikidataQid})");
                    var namedPath = Path.Combine(namedFolder, "headshot.jpg");
                    if (File.Exists(namedPath))
                    {
                        await personRepo.UpdateLocalHeadshotPathAsync(id, namedPath, ct);
                        return Results.File(namedPath, "image/jpeg");
                    }
                }

                // Try bare GUID folder (legacy)
                var guidPath = Path.Combine(core.LibraryRoot, ".people", id.ToString(), "headshot.jpg");
                if (File.Exists(guidPath))
                {
                    await personRepo.UpdateLocalHeadshotPathAsync(id, guidPath, ct);
                    return Results.File(guidPath, "image/jpeg");
                }
            }

            // No local file — download from Wikimedia and cache locally using ImagePathService.
            if (!string.IsNullOrEmpty(person.HeadshotUrl))
            {
                try
                {
                    using var client = httpFactory.CreateClient("headshot_download");
                    var bytes = await client.GetByteArrayAsync(person.HeadshotUrl, ct);
                    if (bytes.Length > 0)
                    {
                        string personFolder;
                        if (imagePaths is not null && !string.IsNullOrWhiteSpace(person.WikidataQid))
                        {
                            personFolder = imagePaths.GetPersonImageDir(person.WikidataQid);
                        }
                        else if (!string.IsNullOrWhiteSpace(core?.LibraryRoot))
                        {
                            var folderName = !string.IsNullOrWhiteSpace(person.WikidataQid) && !string.IsNullOrWhiteSpace(person.Name)
                                ? $"{string.Join("_", person.Name.Split(Path.GetInvalidFileNameChars()))} ({person.WikidataQid})"
                                : id.ToString();
                            personFolder = Path.Combine(core.LibraryRoot, ".people", folderName);
                        }
                        else
                        {
                            return Results.NotFound("Headshot not available (no library root configured).");
                        }
                        Directory.CreateDirectory(personFolder);
                        var localPath = Path.Combine(personFolder, "headshot.jpg");
                        await File.WriteAllBytesAsync(localPath, bytes, ct);
                        await personRepo.UpdateLocalHeadshotPathAsync(id, localPath, ct);
                        return Results.File(localPath, "image/jpeg");
                    }
                }
                catch
                {
                    // Download failed — fall through to 404
                }
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
                            roles              = p.Roles,
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

        // GET /persons/by-work/{workId} — all persons linked to a specific work.
        group.MapGet("/by-work/{workId:guid}", async (
            Guid workId,
            IPersonRepository personRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var conn = db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT ma.id
                FROM media_assets ma
                JOIN editions e ON e.id = ma.edition_id
                WHERE e.work_id = @workId;
                """;
            cmd.Parameters.AddWithValue("@workId", workId.ToString());

            var assetIds = new List<Guid>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                assetIds.Add(Guid.Parse(reader.GetString(0)));

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
                            roles              = p.Roles,
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
            {
                // Fallback: match by canonical author/narrator/director name
                using var fallbackCmd = conn.CreateCommand();
                fallbackCmd.CommandText = """
                    SELECT DISTINCT w.hub_id
                    FROM canonical_values cv
                    JOIN media_assets ma ON ma.id = cv.entity_id
                    JOIN editions e      ON e.id  = ma.edition_id
                    JOIN works w         ON w.id  = e.work_id
                    WHERE cv.key IN ('author', 'narrator', 'director', 'artist', 'composer', 'illustrator', 'performer')
                      AND cv.value = @personName
                      AND w.hub_id IS NOT NULL;
                    """;
                fallbackCmd.Parameters.AddWithValue("@personName", person.Name);

                using var fallbackReader = fallbackCmd.ExecuteReader();
                while (fallbackReader.Read())
                    hubIds.Add(Guid.Parse(fallbackReader.GetString(0)));
            }

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

        // GET /persons/role-counts — count of persons per role.
        // Excludes Composer (absorbed into Artist/Performer in the UI).
        group.MapGet("/role-counts", async (IPersonRepository personRepo, CancellationToken ct) =>
        {
            var counts = await personRepo.GetRoleCountsAsync(ct);
            // Remove Composer — not a UI-visible role
            var filtered = counts
                .Where(kvp => !kvp.Key.Equals("Composer", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            return Results.Ok(filtered);
        })
        .WithName("GetPersonRoleCounts")
        .WithSummary("Count of persons per role.");

        // GET /persons/presence?ids=guid1,guid2,... — media type counts per person.
        group.MapGet("/presence", async (string ids, IPersonRepository personRepo, IDatabaseConnection db, CancellationToken ct) =>
        {
            var personIds = ids.Split(',').Select(Guid.Parse).ToList();

            // Primary path: person_media_links
            var presence = await personRepo.GetPresenceBatchAsync(personIds, ct);

            // Fallback: if person_media_links is empty for ALL persons,
            // match by canonical author/narrator/director values
            if (presence.Values.All(d => d.Count == 0))
            {
                var persons = new Dictionary<Guid, string>();
                foreach (var pid in personIds)
                {
                    var person = await personRepo.FindByIdAsync(pid, ct);
                    if (person is not null)
                        persons[pid] = person.Name;
                }

                using var conn = db.CreateConnection();
                var fallbackPresence = new Dictionary<Guid, Dictionary<string, int>>();
                foreach (var (pid, name) in persons)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = """
                        SELECT cv2.value AS MediaType, COUNT(DISTINCT w.id) AS Count
                        FROM canonical_values cv
                        JOIN media_assets ma ON ma.id = cv.entity_id
                        JOIN editions e ON e.id = ma.edition_id
                        JOIN works w ON w.id = e.work_id
                        JOIN canonical_values cv2 ON cv2.entity_id = ma.id AND cv2.key = 'media_type'
                        WHERE cv.key IN ('author', 'narrator', 'director', 'artist', 'composer', 'illustrator', 'performer')
                          AND cv.value = @name
                        GROUP BY cv2.value;
                        """;
                    cmd.Parameters.AddWithValue("@name", name);

                    var mediaTypeCounts = new Dictionary<string, int>();
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var mediaType = reader.GetString(0);
                        var count = reader.GetInt32(1);
                        mediaTypeCounts[mediaType] = count;
                    }

                    if (mediaTypeCounts.Count > 0)
                        fallbackPresence[pid] = mediaTypeCounts;
                }

                return Results.Ok(fallbackPresence.ToDictionary(
                    kv => kv.Key.ToString(),
                    kv => kv.Value));
            }

            return Results.Ok(presence.ToDictionary(
                kv => kv.Key.ToString(),
                kv => kv.Value));
        })
        .WithName("GetPersonPresence")
        .WithSummary("Media type counts per person.");

        // GET /persons?role=Author&limit=50 -- list persons filtered by role.
        group.MapGet("/", async (
            string? role,
            int? limit,
            IPersonRepository personRepo,
            CancellationToken ct) =>
        {
            var all = await personRepo.ListAllAsync(ct);

            // Filter out Composer role and fix groups incorrectly tagged as Narrator
            IEnumerable<MediaEngine.Domain.Entities.Person> filtered = all
                .Select(p =>
                {
                    var cleanedRoles = p.Roles
                        .Where(r => !r.Equals("Composer", StringComparison.OrdinalIgnoreCase))
                        .Where(r => !(p.IsGroup && r.Equals("Narrator", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                    p.Roles = cleanedRoles;
                    return p;
                })
                .Where(p => p.Roles.Count > 0); // Exclude persons with no remaining roles

            if (!string.IsNullOrEmpty(role))
                filtered = filtered.Where(p => p.Roles.Any(r => r.Equals(role, StringComparison.OrdinalIgnoreCase)));

            var results = filtered
                .Take(limit ?? 50)
                .Select(p => new
                {
                    id                 = p.Id,
                    name               = p.Name,
                    roles              = p.Roles,
                    wikidata_qid       = p.WikidataQid,
                    headshot_url       = p.HeadshotUrl,
                    has_local_headshot = !string.IsNullOrEmpty(p.LocalHeadshotPath)
                                         && File.Exists(p.LocalHeadshotPath),
                    is_pseudonym       = p.IsPseudonym,
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
