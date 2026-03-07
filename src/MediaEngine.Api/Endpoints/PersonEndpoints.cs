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

        return app;
    }
}
