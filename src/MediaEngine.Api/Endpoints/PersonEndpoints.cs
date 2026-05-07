using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Services;
using MediaEngine.Api.Models;
using MediaEngine.Application.Services;
using MediaEngine.Contracts.Paging;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

public static class PersonEndpoints
{
    public static IEndpointRouteBuilder MapPersonEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/persons")
                       .WithTags("Persons");

        // GET /persons/{id} â€” person detail including local headshot availability.
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
                date_of_birth   = person.DateOfBirth,
                date_of_death   = person.DateOfDeath,
                place_of_birth  = person.PlaceOfBirth,
                place_of_death  = person.PlaceOfDeath,
                nationality     = person.Nationality,
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

        // GET /persons/{id}/aliases â€” linked pseudonym and real-person entries.
        group.MapGet("/{id:guid}/aliases", async (
            Guid id,
            IPersonAliasReadService aliasReadService,
            CancellationToken ct) =>
        {
            var response = await aliasReadService.GetAliasesAsync(id, ct);
            return response is null
                ? Results.NotFound($"Person '{id}' not found.")
                : Results.Ok(response);
        })
        .WithName("GetPersonAliases")
        .WithSummary("Linked pseudonym and real-person entries for a given person.");

        // GET /persons/{id}/headshot â€” serves headshot.jpg from the person image directory.
        // Checks .data/images/people/{QID}/ (via ImagePathService) first, then falls back
        // to legacy .people/ conventions. Downloads and caches if no local file exists.
        group.MapGet("/{id:guid}/headshot", async (
            Guid id,
            IPersonRepository personRepo,
            IConfigurationLoader configLoader,
            IHttpClientFactory httpFactory,
            AssetPathService assetPaths,
            ImagePathService? imagePaths,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("MediaEngine.Api.PersonHeadshots");
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

            // No local file â€” download from Wikimedia and cache locally using ImagePathService.
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

                        logger.LogDebug(
                            "Rejected person headshot for {PersonId} ({Name}) because response was not an image. ContentType={ContentType}, Bytes={ByteCount}",
                            id, person.Name, contentType, bytes.Length);
                    }
                    else
                    {
                        logger.LogDebug(
                            "Remote person headshot request failed for {PersonId} ({Name}) with HTTP {StatusCode}",
                            id, person.Name, (int)response.StatusCode);
                    }
                }
                catch
                {
                    // Download failed â€” fall through to 404
                }
            }

            logger.LogDebug(
                "No person headshot available for {PersonId} ({Name}). HasQid={HasQid}, HasRemoteUrl={HasRemoteUrl}, LocalPath={LocalPath}",
                id,
                person.Name,
                !string.IsNullOrWhiteSpace(person.WikidataQid),
                !string.IsNullOrWhiteSpace(person.HeadshotUrl),
                person.LocalHeadshotPath);

            return Results.NotFound("Headshot not available.");
        });

        // GET /persons/by-collection/{collectionId} â€” all persons linked to works in a collection.
        group.MapGet("/by-collection/{collectionId:guid}", async (
            Guid collectionId,
            IPersonAssetScopeReadService personScopeReadService,
            CancellationToken ct) =>
        {
            var persons = await personScopeReadService.GetByCollectionAsync(collectionId, ct);
            return Results.Ok(persons);
        });

        // GET /persons/by-work/{workId} â€” all persons linked to a specific work.
        group.MapGet("/by-work/{workId:guid}", async (
            Guid workId,
            IPersonAssetScopeReadService personScopeReadService,
            CancellationToken ct) =>
        {
            var persons = await personScopeReadService.GetByWorkAsync(workId, ct);
            return Results.Ok(persons);
        });

        // GET /persons/{id}/library-credits â€” role-aware owned work credits for a person.
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

        // GET /persons/{id}/works â€” all collections containing works by this person.
        group.MapGet("/{id:guid}/works", async (
            Guid id,
            IPersonRepository personRepo,
            ICollectionRepository collectionRepo,
            IPersonWorksReadService personWorksReadService,
            CancellationToken ct) =>
        {
            var person = await personRepo.FindByIdAsync(id, ct);
            if (person is null)
                return Results.NotFound($"Person '{id}' not found.");

            var collectionIds = await personWorksReadService.GetCollectionIdsForPersonAsync(id, ct);
            if (collectionIds.Count == 0)
                return Results.Ok(Array.Empty<MediaEngine.Api.Models.CollectionDto>());

            var allCollections = await collectionRepo.GetAllAsync(ct);
            var dtos = allCollections
                .Where(h => collectionIds.Contains(h.Id))
                .Select(MediaEngine.Api.Models.CollectionDto.FromDomain)
                .ToList();

            return Results.Ok(dtos);
        })
        .WithName("GetWorksByPerson")
        .WithSummary("All collections containing works linked to this person (author/narrator/director).")
        .Produces<List<MediaEngine.Api.Models.CollectionDto>>(StatusCodes.Status200OK);

        // GET /persons/role-counts â€” count of persons per role.
        // Excludes Composer (absorbed into Artist/Performer in the UI).
        group.MapGet("/role-counts", async (IPersonRepository personRepo, CancellationToken ct) =>
        {
            var counts = await personRepo.GetRoleCountsAsync(ct);
            // Remove Composer â€” not a UI-visible role
            var filtered = counts
                .Where(kvp => !kvp.Key.Equals("Composer", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            return Results.Ok(filtered);
        })
        .WithName("GetPersonRoleCounts")
        .WithSummary("Count of persons per role.");

        // GET /persons/presence?ids=guid1,guid2,... â€” media type counts per person.
        group.MapGet("/presence", async (string ids, IPersonPresenceReadService presenceReadService, CancellationToken ct) =>
        {
            var personIds = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Take(500)
                .ToList();
            var presence = await presenceReadService.GetPresenceAsync(personIds, ct);
            return Results.Ok(presence);
        })
        .WithName("GetPersonPresence")
        .WithSummary("Media type counts per person.");

        // GET /persons?role=Author&limit=50 -- list persons filtered by role.
        group.MapGet("/", async (
            string? role,
            int? offset,
            int? limit,
            IPersonRepository personRepo,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var page = PagedRequest.From(offset, limit, defaultLimit: 100, maxLimit: 500);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var all = await personRepo.ListPagedAsync(role, page.Offset, page.Limit + 1, ct);

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

            var results = filtered
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

            var response = PagedResponse<object>.FromPage(results.Cast<object>().ToList(), page);
            var logger = loggerFactory.CreateLogger("MediaEngine.Api.Persons");
            sw.Stop();
            if (sw.ElapsedMilliseconds >= 1000)
            {
                logger.LogWarning(
                    "Large-list read {Operation} took {ElapsedMs} ms with role {Role}, offset {Offset}, limit {Limit}, returned {ItemCount}, has_more {HasMore}",
                    "persons.list",
                    sw.ElapsedMilliseconds,
                    role,
                    response.Offset,
                    response.Limit,
                    response.Items.Count,
                    response.HasMore);
            }
            else
            {
                logger.LogDebug(
                    "Large-list read {Operation} took {ElapsedMs} ms with role {Role}, offset {Offset}, limit {Limit}, returned {ItemCount}, has_more {HasMore}",
                    "persons.list",
                    sw.ElapsedMilliseconds,
                    role,
                    response.Offset,
                    response.Limit,
                    response.Items.Count,
                    response.HasMore);
            }

            return Results.Ok(response);
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

