using MediaEngine.Api.Models;
using MediaEngine.Api.Endpoints;
using MediaEngine.Application.ReadModels;
using MediaEngine.Application.Services;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

public sealed class PersonAliasReadService : IPersonAliasReadService
{
    private readonly IPersonRepository _personRepo;
    private readonly IDatabaseConnection _db;

    public PersonAliasReadService(IPersonRepository personRepo, IDatabaseConnection db)
    {
        _personRepo = personRepo;
        _db = db;
    }

    public async Task<PersonAliasResponse?> GetAliasesAsync(Guid personId, CancellationToken ct)
    {
        var person = await _personRepo.FindByIdAsync(personId, ct);
        if (person is null)
            return null;

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = person.IsPseudonym
            ? "SELECT real_person_id FROM person_aliases WHERE pseudonym_person_id = @id"
            : "SELECT pseudonym_person_id FROM person_aliases WHERE real_person_id = @id";
        cmd.Parameters.AddWithValue("@id", personId.ToString());

        var aliases = new List<PersonAliasItemResponse>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var aliasId = Guid.Parse(reader.GetString(0));
            var aliasPerson = await _personRepo.FindByIdAsync(aliasId, ct);
            if (aliasPerson is null)
                continue;

            aliases.Add(new PersonAliasItemResponse
            {
                Id = aliasPerson.Id,
                Name = aliasPerson.Name,
                Roles = aliasPerson.Roles,
                HeadshotUrl = ApiImageUrls.BuildPersonHeadshotUrl(aliasPerson.Id, aliasPerson.LocalHeadshotPath, aliasPerson.HeadshotUrl),
                IsPseudonym = aliasPerson.IsPseudonym,
                WikidataQid = aliasPerson.WikidataQid,
                Relationship = person.IsPseudonym ? "real_person" : "pen_name",
            });
        }

        return new PersonAliasResponse
        {
            PersonId = personId,
            PersonName = person.Name,
            IsPseudonym = person.IsPseudonym,
            Aliases = aliases,
        };
    }
}

public sealed class PersonPresenceReadService : IPersonPresenceReadService
{
    private readonly IPersonRepository _personRepo;

    public PersonPresenceReadService(IPersonRepository personRepo)
    {
        _personRepo = personRepo;
    }

    public async Task<IReadOnlyDictionary<string, Dictionary<string, int>>> GetPresenceAsync(
        IReadOnlyList<Guid> personIds,
        CancellationToken ct)
    {
        var presence = await _personRepo.GetPresenceBatchAsync(personIds, ct);
        return presence.ToDictionary(
            kv => kv.Key.ToString(),
            kv => kv.Value);
    }
}

public sealed class PersonWorksReadService : IPersonWorksReadService
{
    private readonly IPersonRepository _personRepo;
    private readonly IDatabaseConnection _db;

    public PersonWorksReadService(IPersonRepository personRepo, IDatabaseConnection db)
    {
        _personRepo = personRepo;
        _db = db;
    }

    public async Task<IReadOnlySet<Guid>> GetCollectionIdsForPersonAsync(Guid personId, CancellationToken ct)
    {
        var person = await _personRepo.FindByIdAsync(personId, ct);
        if (person is null)
            return new HashSet<Guid>();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT w.collection_id
            FROM person_media_links pml
            JOIN media_assets ma ON ma.id = pml.media_asset_id
            JOIN editions e      ON e.id  = ma.edition_id
            JOIN works w         ON w.id  = e.work_id
            WHERE pml.person_id = @personId
              AND w.collection_id IS NOT NULL;
            """;
        cmd.Parameters.AddWithValue("@personId", personId.ToString());

        var collectionIds = new HashSet<Guid>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
                collectionIds.Add(Guid.Parse(reader.GetString(0)));
        }

        if (collectionIds.Count > 0)
            return collectionIds;

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

        return collectionIds;
    }
}

public sealed class PersonAssetScopeReadService : IPersonAssetScopeReadService
{
    private readonly IPersonRepository _personRepo;
    private readonly IDatabaseConnection _db;

    public PersonAssetScopeReadService(IPersonRepository personRepo, IDatabaseConnection db)
    {
        _personRepo = personRepo;
        _db = db;
    }

    public async Task<IReadOnlyList<PersonSummaryResponse>> GetByCollectionAsync(Guid collectionId, CancellationToken ct)
    {
        return await GetByAssetQueryAsync("""
            SELECT ma.id
            FROM media_assets ma
            JOIN editions e ON e.id = ma.edition_id
            JOIN works w    ON w.id = e.work_id
            WHERE w.collection_id = @id;
            """, collectionId, ct);
    }

    public async Task<IReadOnlyList<PersonSummaryResponse>> GetByWorkAsync(Guid workId, CancellationToken ct)
    {
        return await GetByAssetQueryAsync("""
            SELECT ma.id
            FROM media_assets ma
            JOIN editions e ON e.id = ma.edition_id
            WHERE e.work_id = @id;
            """, workId, ct);
    }

    private async Task<IReadOnlyList<PersonSummaryResponse>> GetByAssetQueryAsync(
        string query,
        Guid id,
        CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = query;
        cmd.Parameters.AddWithValue("@id", id.ToString());

        var assetIds = new List<Guid>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
                assetIds.Add(Guid.Parse(reader.GetString(0)));
        }

        var linked = await _personRepo.GetByMediaAssetsAsync(assetIds, ct);
        return linked
            .GroupBy(p => p.Id)
            .Select(group => group.First())
            .Select(p => new PersonSummaryResponse
            {
                Id = p.Id,
                Name = p.Name,
                Roles = p.Roles,
                WikidataQid = p.WikidataQid,
                HeadshotUrl = ApiImageUrls.BuildPersonHeadshotUrl(p.Id, p.LocalHeadshotPath, p.HeadshotUrl),
                HasLocalHeadshot = !string.IsNullOrEmpty(p.LocalHeadshotPath) && File.Exists(p.LocalHeadshotPath),
                Biography = p.Biography,
                Occupation = p.Occupation,
            })
            .ToList();
    }
}
