using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

internal static class CastCreditQueries
{
    public static async Task<List<CastCreditDto>> BuildForWorkAsync(
        Guid workId,
        ICanonicalValueArrayRepository canonicalArrayRepo,
        IPersonRepository personRepo,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        using var conn = db.CreateConnection();
        var work = await conn.QueryFirstOrDefaultAsync<WorkIdentityRow>(
            """
            SELECT id AS WorkId,
                   wikidata_qid AS WorkQid,
                   COALESCE(gp.id, p.id, w.id) AS RootWorkId,
                   root.wikidata_qid AS RootWorkQid
            FROM works w
            LEFT JOIN works p
                ON p.id = w.parent_work_id
            LEFT JOIN works gp
                ON gp.id = p.parent_work_id
            LEFT JOIN works root
                ON root.id = COALESCE(gp.id, p.id, w.id)
            WHERE w.id = @workId
            LIMIT 1;
            """,
            new { workId = workId.ToString() });

        if (work is null)
            return [];

        var explicitCredits = string.IsNullOrWhiteSpace(work.WorkQid)
            ? []
            : await BuildExplicitCastAsync(work.WorkQid, db, ct);
        if (explicitCredits.Count > 0)
            return explicitCredits;

        var rootExplicitCredits = string.IsNullOrWhiteSpace(work.RootWorkQid)
            || string.Equals(work.RootWorkQid, work.WorkQid, StringComparison.OrdinalIgnoreCase)
            ? []
            : await BuildExplicitCastAsync(work.RootWorkQid, db, ct);
        if (rootExplicitCredits.Count > 0)
            return rootExplicitCredits;

        var linkedActors = await BuildActorOnlyCreditsFromMediaLinksAsync(workId, db, ct);
        if (linkedActors.Count > 0)
            return linkedActors;

        var fallbackCredits = await BuildFallbackCreditsFromCanonicalArrayAsync(workId, canonicalArrayRepo, personRepo, ct);
        if (fallbackCredits.Count > 0)
            return fallbackCredits;

        return work.RootWorkId.HasValue && work.RootWorkId.Value != workId
            ? await BuildFallbackCreditsFromCanonicalArrayAsync(work.RootWorkId.Value, canonicalArrayRepo, personRepo, ct)
            : [];
    }

    public static async Task<List<CastCreditDto>> BuildForCollectionRootAsync(
        Guid rootWorkId,
        string? rootWorkQid,
        ICanonicalValueArrayRepository canonicalArrayRepo,
        IPersonRepository personRepo,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        var explicitCredits = string.IsNullOrWhiteSpace(rootWorkQid)
            ? []
            : await BuildExplicitCastAsync(rootWorkQid, db, ct);
        if (explicitCredits.Count > 0)
            return explicitCredits;

        return await BuildFallbackCreditsFromCanonicalArrayAsync(rootWorkId, canonicalArrayRepo, personRepo, ct);
    }

    private static async Task<List<CastCreditDto>> BuildExplicitCastAsync(
        string workQid,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        using var conn = db.CreateConnection();
        var rows = (await conn.QueryAsync<ExplicitCastRow>(
            """
            SELECT p.id                  AS ActorPersonId,
                   p.name                AS ActorName,
                   p.wikidata_qid        AS ActorQid,
                   p.headshot_url        AS ActorHeadshotUrl,
                   p.local_headshot_path AS ActorLocalHeadshotPath,
                   fe.id                 AS CharacterId,
                   fe.label              AS CharacterName,
                   fe.wikidata_qid       AS CharacterQid,
                   fe.image_url          AS CharacterImageUrl,
                   cp.id                 AS PortraitId,
                   cp.image_url          AS PortraitImageUrl,
                   cp.local_image_path   AS PortraitLocalImagePath,
                   cp.is_default         AS PortraitIsDefault
            FROM character_performer_links cpl
            INNER JOIN persons p
                ON p.id = cpl.person_id
            LEFT JOIN fictional_entities fe
                ON fe.id = cpl.fictional_entity_id
            LEFT JOIN character_portraits cp
                ON cp.fictional_entity_id = fe.id
               AND cp.person_id = p.id
            WHERE cpl.work_qid = @workQid
            ORDER BY p.name, fe.label, cp.is_default DESC;
            """,
            new { workQid })).ToList();

        return rows
            .Where(row => row.ActorPersonId.HasValue && !string.IsNullOrWhiteSpace(row.ActorName))
            .GroupBy(row => new
            {
                row.ActorPersonId,
                row.ActorName,
                row.ActorQid,
                HeadshotUrl = BuildHeadshotUrl(row.ActorPersonId, row.ActorLocalHeadshotPath, row.ActorHeadshotUrl),
            })
            .Select(group => new CastCreditDto
            {
                PersonId = group.Key.ActorPersonId,
                Name = group.Key.ActorName ?? "Unknown",
                WikidataQid = group.Key.ActorQid,
                HeadshotUrl = group.Key.HeadshotUrl,
                Characters = group
                    .Where(row => row.CharacterId != Guid.Empty)
                    .GroupBy(row => new { row.CharacterId, row.CharacterName, row.CharacterQid })
                    .Select(characterGroup =>
                    {
                        var preferred = characterGroup
                            .OrderByDescending(row => row.PortraitIsDefault)
                            .ThenByDescending(row => !string.IsNullOrWhiteSpace(row.PortraitImageUrl))
                            .First();

                        return new CharacterPortrayalDto
                        {
                            FictionalEntityId = characterGroup.Key.CharacterId,
                            CharacterName = characterGroup.Key.CharacterName,
                            CharacterQid = characterGroup.Key.CharacterQid,
                            PortraitUrl = ApiImageUrls.BuildCharacterPortraitUrl(
                                preferred.PortraitId,
                                preferred.PortraitLocalImagePath,
                                preferred.PortraitImageUrl)
                                ?? preferred.CharacterImageUrl,
                        };
                    })
                    .OrderBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            })
            .OrderBy(cast => cast.Name, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static async Task<List<CastCreditDto>> BuildActorOnlyCreditsFromMediaLinksAsync(
        Guid workId,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        using var conn = db.CreateConnection();
        var rows = (await conn.QueryAsync<ActorOnlyRow>(
            """
            SELECT p.id                  AS ActorPersonId,
                   p.name                AS ActorName,
                   p.wikidata_qid        AS ActorQid,
                   p.headshot_url        AS ActorHeadshotUrl,
                   p.local_headshot_path AS ActorLocalHeadshotPath
            FROM person_media_links pml
            INNER JOIN persons p
                ON p.id = pml.person_id
            INNER JOIN media_assets ma
                ON ma.id = pml.media_asset_id
            INNER JOIN editions e
                ON e.id = ma.edition_id
            WHERE e.work_id = @workId
              AND pml.role IN ('Actor', 'Voice Actor')
            GROUP BY p.id, p.name, p.wikidata_qid, p.headshot_url, p.local_headshot_path
            ORDER BY p.name;
            """,
            new { workId = workId.ToString() })).ToList();

        return rows
            .Where(row => row.ActorPersonId.HasValue && !string.IsNullOrWhiteSpace(row.ActorName))
            .Select(row => new CastCreditDto
            {
                PersonId = row.ActorPersonId,
                Name = row.ActorName ?? "Unknown",
                WikidataQid = row.ActorQid,
                HeadshotUrl = BuildHeadshotUrl(row.ActorPersonId, row.ActorLocalHeadshotPath, row.ActorHeadshotUrl),
            })
            .Take(12)
            .ToList();
    }

    private static async Task<List<CastCreditDto>> BuildFallbackCreditsFromCanonicalArrayAsync(
        Guid workId,
        ICanonicalValueArrayRepository canonicalArrayRepo,
        IPersonRepository personRepo,
        CancellationToken ct)
    {
        var credits = new List<CastCreditDto>();
        var castEntries = await canonicalArrayRepo.GetValuesAsync(workId, "cast_member", ct);
        foreach (var entry in castEntries.OrderBy(value => value.Ordinal).Take(12))
        {
            if (string.IsNullOrWhiteSpace(entry.Value))
                continue;

            Person? person = null;
            if (!string.IsNullOrWhiteSpace(entry.ValueQid))
                person = await personRepo.FindByQidAsync(entry.ValueQid, ct);
            person ??= await personRepo.FindByNameAsync(entry.Value, ct);

            credits.Add(new CastCreditDto
            {
                PersonId = person?.Id,
                Name = person?.Name ?? entry.Value,
                WikidataQid = entry.ValueQid ?? person?.WikidataQid,
                HeadshotUrl = BuildHeadshotUrl(person?.Id, person?.LocalHeadshotPath, person?.HeadshotUrl),
            });
        }

        return credits;
    }

    private static string? BuildHeadshotUrl(Guid? personId, string? localHeadshotPath, string? remoteHeadshotUrl)
        => personId.HasValue
            ? ApiImageUrls.BuildPersonHeadshotUrl(personId.Value, localHeadshotPath, remoteHeadshotUrl)
            : remoteHeadshotUrl;

    private sealed class WorkIdentityRow
    {
        public Guid WorkId { get; init; }
        public string? WorkQid { get; init; }
        public Guid? RootWorkId { get; init; }
        public string? RootWorkQid { get; init; }
    }

    private sealed class ExplicitCastRow
    {
        public Guid? ActorPersonId { get; init; }
        public string? ActorName { get; init; }
        public string? ActorQid { get; init; }
        public string? ActorHeadshotUrl { get; init; }
        public string? ActorLocalHeadshotPath { get; init; }
        public Guid CharacterId { get; init; }
        public string? CharacterName { get; init; }
        public string? CharacterQid { get; init; }
        public string? CharacterImageUrl { get; init; }
        public Guid? PortraitId { get; init; }
        public string? PortraitImageUrl { get; init; }
        public string? PortraitLocalImagePath { get; init; }
        public bool PortraitIsDefault { get; init; }
    }

    private sealed class ActorOnlyRow
    {
        public Guid? ActorPersonId { get; init; }
        public string? ActorName { get; init; }
        public string? ActorQid { get; init; }
        public string? ActorHeadshotUrl { get; init; }
        public string? ActorLocalHeadshotPath { get; init; }
    }
}

internal static class PersonCreditQueries
{
    public static async Task<List<PersonGroupMemberDto>> GetGroupMembersAsync(
        Guid personId,
        bool isGroup,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        using var conn = db.CreateConnection();
        var rows = (await conn.QueryAsync<PersonGroupMemberRow>(
            isGroup
                ? """
                  SELECT p.id AS Id,
                         p.name AS Name
                  FROM person_group_members pgm
                  INNER JOIN persons p ON p.id = pgm.member_id
                  WHERE pgm.group_id = @personId
                  ORDER BY p.name;
                  """
                : """
                  SELECT p.id AS Id,
                         p.name AS Name
                  FROM person_group_members pgm
                  INNER JOIN persons p ON p.id = pgm.group_id
                  WHERE pgm.member_id = @personId
                  ORDER BY p.name;
                  """,
            new { personId = personId.ToString() })).ToList();

        return rows
            .Select(row => new PersonGroupMemberDto
            {
                Id = row.Id,
                Name = row.Name ?? string.Empty,
            })
            .ToList();
    }

    public static async Task<List<PersonLibraryCreditDto>> GetLibraryCreditsAsync(
        Guid personId,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        using var conn = db.CreateConnection();
        var baseRows = (await conn.QueryAsync<PersonLibraryCreditRow>(
            """
            SELECT w.id                                   AS WorkId,
                   w.collection_id                        AS CollectionId,
                   w.media_type                           AS MediaType,
                   w.wikidata_qid                         AS WorkQid,
                   COALESCE(MAX(CASE WHEN cv.key = 'title' THEN cv.value END), c.display_name, 'Untitled') AS Title,
                   MAX(CASE WHEN cv.key = 'year' THEN cv.value END) AS Year,
                   pml.role                               AS Role,
                   MIN(ma.id)                             AS FirstAssetId
            FROM person_media_links pml
            INNER JOIN media_assets ma
                ON ma.id = pml.media_asset_id
            INNER JOIN editions e
                ON e.id = ma.edition_id
            INNER JOIN works w
                ON w.id = e.work_id
            LEFT JOIN collections c
                ON c.id = w.collection_id
            LEFT JOIN canonical_values cv
                ON cv.entity_id = w.id
               AND cv.key IN ('title', 'year')
            WHERE pml.person_id = @personId
            GROUP BY w.id, w.collection_id, w.media_type, w.wikidata_qid, c.display_name, pml.role
            ORDER BY MAX(CASE WHEN cv.key = 'year' THEN cv.value END) DESC, Title, pml.role;
            """,
            new { personId = personId.ToString() })).ToList();

        var workQids = baseRows
            .Where(row => !string.IsNullOrWhiteSpace(row.WorkQid))
            .Select(row => row.WorkQid!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var charactersByWorkQid = new Dictionary<string, List<CharacterPortrayalDto>>(StringComparer.OrdinalIgnoreCase);
        if (workQids.Count > 0)
        {
            var characterRows = (await conn.QueryAsync<PersonCreditCharacterRow>(
                """
                SELECT cpl.work_qid           AS WorkQid,
                       fe.id                  AS CharacterId,
                       fe.label               AS CharacterName,
                       fe.wikidata_qid        AS CharacterQid,
                       fe.image_url           AS CharacterImageUrl,
                       cp.id                  AS PortraitId,
                       cp.image_url           AS PortraitImageUrl,
                       cp.local_image_path    AS PortraitLocalImagePath,
                       cp.is_default          AS PortraitIsDefault
                FROM character_performer_links cpl
                INNER JOIN fictional_entities fe
                    ON fe.id = cpl.fictional_entity_id
                LEFT JOIN character_portraits cp
                    ON cp.fictional_entity_id = fe.id
                   AND cp.person_id = @personId
                WHERE cpl.person_id = @personId
                  AND cpl.work_qid IN @workQids
                ORDER BY cpl.work_qid, fe.label, cp.is_default DESC;
                """,
                new { personId = personId.ToString(), workQids })).ToList();

            charactersByWorkQid = characterRows
                .GroupBy(row => row.WorkQid ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .GroupBy(row => new { row.CharacterId, row.CharacterName, row.CharacterQid })
                        .Select(characterGroup =>
                        {
                            var preferred = characterGroup
                                .OrderByDescending(row => row.PortraitIsDefault)
                                .ThenByDescending(row => !string.IsNullOrWhiteSpace(row.PortraitImageUrl))
                                .First();

                            return new CharacterPortrayalDto
                            {
                                FictionalEntityId = characterGroup.Key.CharacterId,
                                CharacterName = characterGroup.Key.CharacterName,
                                CharacterQid = characterGroup.Key.CharacterQid,
                                PortraitUrl = ApiImageUrls.BuildCharacterPortraitUrl(
                                    preferred.PortraitId,
                                    preferred.PortraitLocalImagePath,
                                    preferred.PortraitImageUrl)
                                    ?? preferred.CharacterImageUrl,
                            };
                        })
                        .OrderBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);
        }

        return baseRows
            .Select(row =>
            {
                var includeCharacters = row.Role.Equals("Actor", StringComparison.OrdinalIgnoreCase)
                    || row.Role.Equals("Voice Actor", StringComparison.OrdinalIgnoreCase);

                return new PersonLibraryCreditDto
                {
                    WorkId = row.WorkId,
                    CollectionId = row.CollectionId,
                    MediaType = row.MediaType,
                    Title = row.Title ?? "Untitled",
                    CoverUrl = row.FirstAssetId.HasValue ? $"/stream/{row.FirstAssetId.Value}/cover-thumb" : null,
                    Year = row.Year,
                    Role = row.Role,
                    Characters = includeCharacters && !string.IsNullOrWhiteSpace(row.WorkQid)
                        && charactersByWorkQid.TryGetValue(row.WorkQid, out var characters)
                        ? characters
                        : [],
                };
            })
            .ToList();
    }

    public static async Task<List<PersonCharacterRoleDto>> GetCharacterRolesAsync(
        Guid personId,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        using var conn = db.CreateConnection();
        var rows = (await conn.QueryAsync<PersonCharacterRoleRow>(
            """
            SELECT cpl.fictional_entity_id  AS FictionalEntityId,
                   fe.label                 AS CharacterName,
                   fe.image_url             AS CharacterImageUrl,
                   cp.id                    AS PortraitId,
                   cp.image_url             AS PortraitImageUrl,
                   cp.local_image_path      AS PortraitLocalImagePath,
                   cp.is_default            AS PortraitIsDefault,
                   w.id                     AS WorkId,
                   w.wikidata_qid           AS WorkQid,
                   w.collection_id          AS CollectionId,
                   w.media_type             AS MediaType,
                   COALESCE(MAX(CASE WHEN cv.key = 'title' THEN cv.value END), 'Untitled') AS WorkTitle,
                   fe.fictional_universe_qid AS UniverseQid,
                   fe.fictional_universe_label AS UniverseLabel
            FROM character_performer_links cpl
            INNER JOIN fictional_entities fe
                ON fe.id = cpl.fictional_entity_id
            INNER JOIN works w
                ON w.wikidata_qid = cpl.work_qid
            LEFT JOIN canonical_values cv
                ON cv.entity_id = w.id
               AND cv.key = 'title'
            LEFT JOIN character_portraits cp
                ON cp.fictional_entity_id = fe.id
               AND cp.person_id = cpl.person_id
            WHERE cpl.person_id = @personId
              AND cpl.work_qid IS NOT NULL
            GROUP BY cpl.fictional_entity_id, fe.label, fe.image_url, cp.id, cp.image_url, cp.local_image_path, cp.is_default,
                     w.id, w.wikidata_qid, w.collection_id, w.media_type, fe.fictional_universe_qid, fe.fictional_universe_label
            ORDER BY UniverseLabel, WorkTitle, CharacterName, cp.is_default DESC;
            """,
            new { personId = personId.ToString() })).ToList();

        return rows
            .GroupBy(row => new { row.WorkId, row.FictionalEntityId })
            .Select(group =>
            {
                var preferred = group
                    .OrderByDescending(row => row.PortraitIsDefault)
                    .ThenByDescending(row => !string.IsNullOrWhiteSpace(row.PortraitImageUrl))
                    .First();

                return new PersonCharacterRoleDto
                {
                    FictionalEntityId = group.Key.FictionalEntityId,
                    CharacterName = preferred.CharacterName,
                    PortraitUrl = ApiImageUrls.BuildCharacterPortraitUrl(
                        preferred.PortraitId,
                        preferred.PortraitLocalImagePath,
                        preferred.PortraitImageUrl)
                        ?? preferred.CharacterImageUrl,
                    WorkId = preferred.WorkId,
                    WorkQid = preferred.WorkQid,
                    WorkTitle = preferred.WorkTitle,
                    CollectionId = preferred.CollectionId,
                    MediaType = preferred.MediaType,
                    IsDefault = preferred.PortraitIsDefault,
                    UniverseQid = preferred.UniverseQid,
                    UniverseLabel = preferred.UniverseLabel,
                };
            })
            .OrderBy(role => role.UniverseLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(role => role.WorkTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(role => role.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class PersonGroupMemberRow
    {
        public Guid Id { get; init; }
        public string? Name { get; init; }
    }

    private sealed class PersonLibraryCreditRow
    {
        public Guid WorkId { get; init; }
        public Guid? CollectionId { get; init; }
        public string? MediaType { get; init; }
        public string? WorkQid { get; init; }
        public string? Title { get; init; }
        public string? Year { get; init; }
        public string Role { get; init; } = string.Empty;
        public Guid? FirstAssetId { get; init; }
    }

    private sealed class PersonCreditCharacterRow
    {
        public string? WorkQid { get; init; }
        public Guid CharacterId { get; init; }
        public string? CharacterName { get; init; }
        public string? CharacterQid { get; init; }
        public string? CharacterImageUrl { get; init; }
        public Guid? PortraitId { get; init; }
        public string? PortraitImageUrl { get; init; }
        public string? PortraitLocalImagePath { get; init; }
        public bool PortraitIsDefault { get; init; }
    }

    private sealed class PersonCharacterRoleRow
    {
        public Guid FictionalEntityId { get; init; }
        public string? CharacterName { get; init; }
        public string? CharacterImageUrl { get; init; }
        public Guid? PortraitId { get; init; }
        public string? PortraitImageUrl { get; init; }
        public string? PortraitLocalImagePath { get; init; }
        public bool PortraitIsDefault { get; init; }
        public Guid WorkId { get; init; }
        public string? WorkQid { get; init; }
        public Guid? CollectionId { get; init; }
        public string? MediaType { get; init; }
        public string? WorkTitle { get; init; }
        public string? UniverseQid { get; init; }
        public string? UniverseLabel { get; init; }
    }
}
