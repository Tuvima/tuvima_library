using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Services;
using MediaEngine.Api.Models;
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
            IEntityAssetRepository assetRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var person = await personRepo.FindByIdAsync(id, ct);
            if (person is null)
                return Results.NotFound($"Person '{id}' not found.");

            var groupMembers = await PersonCreditQueries.GetGroupMembersAsync(id, person.IsGroup, db, ct);
            var memberOfGroups = await PersonCreditQueries.GetGroupMembersAsync(id, false, db, ct);
            var preferredBanner = await assetRepo.GetPreferredAsync(id.ToString(), "Banner", ct);
            var preferredBackground = await assetRepo.GetPreferredAsync(id.ToString(), "Background", ct);
            var preferredLogo = await assetRepo.GetPreferredAsync(id.ToString(), "Logo", ct);

            return Results.Ok(new
            {
                id              = person.Id,
                name            = person.Name,
                roles           = person.Roles,
                wikidata_qid    = person.WikidataQid,
                headshot_url    = ApiImageUrls.BuildPersonHeadshotUrl(person.Id, person.LocalHeadshotPath, person.HeadshotUrl),
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
                is_group        = person.IsGroup,
                group_members   = person.IsGroup ? groupMembers : [],
                member_of_groups = person.IsGroup ? [] : memberOfGroups,
                banner_url      = preferredBanner is null ? null : $"/stream/artwork/{preferredBanner.Id}",
                background_url  = preferredBackground is null ? null : $"/stream/artwork/{preferredBackground.Id}",
                logo_url        = preferredLogo is null ? null : $"/stream/artwork/{preferredLogo.Id}",
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
                            headshot_url = ApiImageUrls.BuildPersonHeadshotUrl(realPerson.Id, realPerson.LocalHeadshotPath, realPerson.HeadshotUrl),
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
                            headshot_url = ApiImageUrls.BuildPersonHeadshotUrl(pseudonymPerson.Id, pseudonymPerson.LocalHeadshotPath, pseudonymPerson.HeadshotUrl),
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
            AssetPathService assetPaths,
            ImagePathService? imagePaths,
            CancellationToken ct) =>
        {
            var person = await personRepo.FindByIdAsync(id, ct);
            if (person is null)
                return Results.NotFound($"Person '{id}' not found.");

            // Try local headshot path first.
            if (!string.IsNullOrEmpty(person.LocalHeadshotPath)
                && File.Exists(person.LocalHeadshotPath)
                && IsLikelyImageFile(person.LocalHeadshotPath))
            {
                return Results.File(person.LocalHeadshotPath, GetImageMimeType(person.LocalHeadshotPath));
            }

            var canonicalPath = assetPaths.GetPersonHeadshotPath(id);
            if (File.Exists(canonicalPath) && IsLikelyImageFile(canonicalPath))
            {
                await personRepo.UpdateLocalHeadshotPathAsync(id, canonicalPath, ct);
                return Results.File(canonicalPath, GetImageMimeType(canonicalPath));
            }

            // Check the legacy centralized .data/images/people/{QID}/ path first.
            if (imagePaths is not null && !string.IsNullOrWhiteSpace(person.WikidataQid))
            {
                var newStylePath = Path.Combine(imagePaths.GetPersonImageDir(person.WikidataQid), "headshot.jpg");
                if (File.Exists(newStylePath) && IsLikelyImageFile(newStylePath))
                {
                    await personRepo.UpdateLocalHeadshotPathAsync(id, newStylePath, ct);
                    return Results.File(newStylePath, GetImageMimeType(newStylePath));
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
                    if (File.Exists(namedPath) && IsLikelyImageFile(namedPath))
                    {
                        await personRepo.UpdateLocalHeadshotPathAsync(id, namedPath, ct);
                        return Results.File(namedPath, GetImageMimeType(namedPath));
                    }
                }

                // Try bare GUID folder (legacy)
                var guidPath = Path.Combine(core.LibraryRoot, ".people", id.ToString(), "headshot.jpg");
                if (File.Exists(guidPath) && IsLikelyImageFile(guidPath))
                {
                    await personRepo.UpdateLocalHeadshotPathAsync(id, guidPath, ct);
                    return Results.File(guidPath, GetImageMimeType(guidPath));
                }
            }

            // No local file — download from Wikimedia and cache locally using ImagePathService.
            if (!string.IsNullOrEmpty(person.HeadshotUrl))
            {
                try
                {
                    using var client = httpFactory.CreateClient("headshot_download");
                    using var response = await client.GetAsync(person.HeadshotUrl, ct);
                    if (response.IsSuccessStatusCode)
                    {
                        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                        var contentType = response.Content.Headers.ContentType?.MediaType;
                        if (bytes.Length > 0 && IsLikelyImageBytes(bytes, contentType))
                        {
                            var localPath = assetPaths.GetPersonHeadshotPath(id, InferImageExtension(person.HeadshotUrl, contentType));
                            AssetPathService.EnsureDirectory(localPath);
                            await File.WriteAllBytesAsync(localPath, bytes, ct);
                            await personRepo.UpdateLocalHeadshotPathAsync(id, localPath, ct);
                            return Results.File(bytes, contentType ?? GetImageMimeType(localPath), Path.GetFileName(localPath));
                        }
                    }
                }
                catch
                {
                    // Download failed — fall through to 404
                }
            }

            if (!string.IsNullOrEmpty(person.HeadshotUrl)
                && Uri.TryCreate(person.HeadshotUrl, UriKind.Absolute, out var remoteUri)
                && (remoteUri.Scheme == Uri.UriSchemeHttp || remoteUri.Scheme == Uri.UriSchemeHttps))
            {
                return Results.Redirect(remoteUri.ToString());
            }

            return Results.NotFound("Headshot not available.");
        });

        // GET /persons/by-collection/{collectionId} — all persons linked to works in a collection.
        group.MapGet("/by-collection/{collectionId:guid}", async (
            Guid collectionId,
            IPersonRepository personRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            // Find all media asset IDs for works in this collection.
            var conn = db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT ma.id
                FROM media_assets ma
                JOIN editions e ON e.id = ma.edition_id
                JOIN works w    ON w.id = e.work_id
                WHERE w.collection_id = @collectionId;
                """;
            cmd.Parameters.AddWithValue("@collectionId", collectionId.ToString());

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
                            headshot_url       = ApiImageUrls.BuildPersonHeadshotUrl(p.Id, p.LocalHeadshotPath, p.HeadshotUrl),
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
                            headshot_url       = ApiImageUrls.BuildPersonHeadshotUrl(p.Id, p.LocalHeadshotPath, p.HeadshotUrl),
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

        // GET /persons/{id}/library-credits — role-aware owned work credits for a person.
        group.MapGet("/{id:guid}/library-credits", async (
            Guid id,
            IPersonRepository personRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var person = await personRepo.FindByIdAsync(id, ct);
            if (person is null)
                return Results.NotFound($"Person '{id}' not found.");

            var credits = await PersonCreditQueries.GetLibraryCreditsAsync(id, db, ct);
            return Results.Ok(credits);
        })
        .WithName("GetPersonLibraryCredits")
        .WithSummary("Owned work credits for a person, grouped client-side by role and media type.")
        .Produces<List<PersonLibraryCreditDto>>(StatusCodes.Status200OK);

        // GET /persons/{id}/works — all collections containing works by this person.
        group.MapGet("/{id:guid}/works", async (
            Guid id,
            IPersonRepository personRepo,
            ICollectionRepository collectionRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var person = await personRepo.FindByIdAsync(id, ct);
            if (person is null)
                return Results.NotFound($"Person '{id}' not found.");

            // Find all collection IDs linked to this person via person_media_links.
            using var conn = db.CreateConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = """
                SELECT DISTINCT w.collection_id
                FROM person_media_links pml
                JOIN media_assets ma ON ma.id = pml.media_asset_id
                JOIN editions e      ON e.id  = ma.edition_id
                JOIN works w         ON w.id  = e.work_id
                WHERE pml.person_id = @personId
                  AND w.collection_id IS NOT NULL;
                """;
            cmd.Parameters.AddWithValue("@personId", id.ToString());

            var collectionIds = new HashSet<Guid>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                collectionIds.Add(Guid.Parse(reader.GetString(0)));

            if (collectionIds.Count == 0)
            {
                // Fallback: match by canonical author/narrator/director name
                using var fallbackCmd = conn.CreateCommand();
                fallbackCmd.CommandText = """
                    SELECT DISTINCT w.collection_id
                    FROM canonical_values cv
                    JOIN media_assets ma ON ma.id = cv.entity_id
                    JOIN editions e      ON e.id  = ma.edition_id
                    JOIN works w         ON w.id  = e.work_id
                    WHERE cv.key IN ('author', 'narrator', 'director', 'artist', 'composer', 'illustrator', 'performer')
                      AND cv.value = @personName
                      AND w.collection_id IS NOT NULL;
                    """;
                fallbackCmd.Parameters.AddWithValue("@personName", person.Name);

                using var fallbackReader = fallbackCmd.ExecuteReader();
                while (fallbackReader.Read())
                    collectionIds.Add(Guid.Parse(fallbackReader.GetString(0)));
            }

            if (collectionIds.Count == 0)
                return Results.Ok(Array.Empty<MediaEngine.Api.Models.CollectionDto>());

            // Load full collection data and filter to matching IDs.
            var allCollections = await collectionRepo.GetAllAsync(ct);
            var dtos    = allCollections
                .Where(h => collectionIds.Contains(h.Id))
                .Select(MediaEngine.Api.Models.CollectionDto.FromDomain)
                .ToList();

            return Results.Ok(dtos);
        })
        .WithName("GetWorksByPerson")
        .WithSummary("All collections containing works linked to this person (author/narrator/director).")
        .Produces<List<MediaEngine.Api.Models.CollectionDto>>(StatusCodes.Status200OK);

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
                    headshot_url       = ApiImageUrls.BuildPersonHeadshotUrl(p.Id, p.LocalHeadshotPath, p.HeadshotUrl),
                    has_local_headshot = !string.IsNullOrEmpty(p.LocalHeadshotPath)
                                         && File.Exists(p.LocalHeadshotPath),
                    is_pseudonym       = p.IsPseudonym,
                    is_group           = p.IsGroup,
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

    private static string GetImageMimeType(string fileName)
        => Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            _ => "image/jpeg",
        };

    private static string InferImageExtension(string imageUrl, string? contentType)
    {
        var extension = contentType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            "image/jpeg" or "image/jpg" => ".jpg",
            _ => null,
        };

        if (!string.IsNullOrWhiteSpace(extension))
            return extension;

        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
        {
            extension = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(extension))
                return extension.ToLowerInvariant();
        }

        return ".jpg";
    }

    private static bool IsLikelyImageFile(string path)
    {
        try
        {
            var header = new byte[32];
            using var stream = File.OpenRead(path);
            var read = stream.Read(header, 0, header.Length);
            return IsLikelyImageBytes(header[..read], null);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLikelyImageBytes(byte[] bytes, string? contentType)
    {
        if (bytes.Length == 0)
            return false;

        if (contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return true;

        if (bytes.Length >= 8
            && bytes[0] == 0x89
            && bytes[1] == 0x50
            && bytes[2] == 0x4E
            && bytes[3] == 0x47
            && bytes[4] == 0x0D
            && bytes[5] == 0x0A
            && bytes[6] == 0x1A
            && bytes[7] == 0x0A)
        {
            return true;
        }

        if (bytes.Length >= 6
            && bytes[0] == 0x47
            && bytes[1] == 0x49
            && bytes[2] == 0x46)
        {
            return true;
        }

        return bytes.Length >= 12
            && bytes[0] == 0x52
            && bytes[1] == 0x49
            && bytes[2] == 0x46
            && bytes[3] == 0x46
            && bytes[8] == 0x57
            && bytes[9] == 0x45
            && bytes[10] == 0x42
            && bytes[11] == 0x50;
    }
}
