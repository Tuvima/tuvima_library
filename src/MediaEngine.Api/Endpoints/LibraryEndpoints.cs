using MediaEngine.Api.Security;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/library")
                       .WithTags("Library");

        group.MapGet("/works", async (
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var works = new List<LibraryWorkDto>();

            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();

            // Get all works with their canonical values, excluding staging files
            cmd.CommandText = """
                SELECT w.id, w.media_type, w.ordinal, w.wikidata_qid,
                       ma.id AS asset_id,
                       cv.key, cv.value
                FROM works w
                JOIN editions e ON e.work_id = w.id
                JOIN media_assets ma ON ma.edition_id = e.id
                LEFT JOIN canonical_values cv ON cv.entity_id = ma.id
                WHERE ma.file_path_root NOT LIKE '%/.data/staging/%'
                  AND ma.file_path_root NOT LIKE '%\.data\staging\%'
                  AND ma.file_path_root NOT LIKE '%/.data\staging/%'
                ORDER BY w.id, cv.key
                """;

            using var reader = cmd.ExecuteReader();

            var currentWorkId = Guid.Empty;
            LibraryWorkDto? current = null;

            while (reader.Read())
            {
                var workId = Guid.Parse(reader.GetString(0));
                if (workId != currentWorkId)
                {
                    if (current is not null)
                        works.Add(current);

                    currentWorkId = workId;
                    current = new LibraryWorkDto
                    {
                        Id            = workId,
                        MediaType     = reader.IsDBNull(1) ? null : reader.GetString(1),
                        Ordinal       = reader.IsDBNull(2) ? null : (int?)reader.GetInt32(2),
                        WikidataQid   = reader.IsDBNull(3) ? null : reader.GetString(3),
                        AssetId       = reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
                        CanonicalValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    };
                }

                if (current is not null && !reader.IsDBNull(5) && !reader.IsDBNull(6))
                {
                    var key = reader.GetString(5);
                    var val = reader.GetString(6);
                    current.CanonicalValues[key] = val;
                }
            }

            if (current is not null)
                works.Add(current);

            return Results.Ok(works);
        })
        .WithName("GetLibraryWorks")
        .WithSummary("Flat list of all works in the library with their canonical values (excludes staging).")
        .Produces<List<LibraryWorkDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        return app;
    }
}

public sealed class LibraryWorkDto
{
    public Guid Id { get; init; }
    public string? MediaType { get; init; }
    public int? Ordinal { get; init; }
    public string? WikidataQid { get; init; }
    public Guid? AssetId { get; init; }
    public Dictionary<string, string> CanonicalValues { get; init; } = new();
}
