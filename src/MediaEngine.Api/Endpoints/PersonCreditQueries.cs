using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;
using System.Security.Cryptography;
using System.Text;

namespace MediaEngine.Api.Endpoints;

internal static class CastCreditQueries
{
    private const int MaxCastCredits = 24;

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
            SELECT w.id AS WorkId,
                   COALESCE(
                       NULLIF(TRIM(w.wikidata_qid), ''),
                       (SELECT cv.value
                        FROM editions e
                        INNER JOIN media_assets ma ON ma.edition_id = e.id
                        INNER JOIN canonical_values cv ON cv.entity_id = ma.id
                        WHERE e.work_id = w.id AND cv.key = 'wikidata_qid'
                        LIMIT 1),
                       (SELECT cv.value
                        FROM canonical_values cv
                        WHERE cv.entity_id = w.id AND cv.key = 'wikidata_qid'
                        LIMIT 1),
                       (SELECT ij.resolved_qid
                        FROM editions e
                        INNER JOIN media_assets ma ON ma.edition_id = e.id
                        INNER JOIN identity_jobs ij ON ij.entity_id = ma.id
                        WHERE e.work_id = w.id
                          AND ij.resolved_qid IS NOT NULL
                          AND TRIM(ij.resolved_qid) <> ''
                        ORDER BY ij.updated_at DESC, ij.created_at DESC
                        LIMIT 1)
                   ) AS WorkQid,
                   COALESCE(gp.id, p.id, w.id) AS RootWorkId,
                   COALESCE(
                       NULLIF(TRIM(root.wikidata_qid), ''),
                       (SELECT cv.value
                        FROM canonical_values cv
                        WHERE cv.entity_id = root.id AND cv.key = 'wikidata_qid'
                        LIMIT 1),
                       (SELECT cv.value
                        FROM editions e
                        INNER JOIN media_assets ma ON ma.edition_id = e.id
                        INNER JOIN canonical_values cv ON cv.entity_id = ma.id
                        WHERE e.work_id = root.id AND cv.key = 'wikidata_qid'
                        LIMIT 1)
                   ) AS RootWorkQid
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

        var workRankMap = await CastRankMap.BuildAsync(work.WorkId, canonicalArrayRepo, db, ct);
        var credits = new List<CastCreditDto>();
        var explicitCredits = string.IsNullOrWhiteSpace(work.WorkQid)
            ? []
            : await BuildExplicitCastAsync(work.WorkQid, workRankMap, db, ct);
        AddUniqueCredits(credits, explicitCredits);

        var rootRankMap = work.RootWorkId.HasValue
            ? await CastRankMap.BuildAsync(work.RootWorkId.Value, canonicalArrayRepo, db, ct)
            : CastRankMap.Empty;
        var rootExplicitCredits = string.IsNullOrWhiteSpace(work.RootWorkQid)
            || string.Equals(work.RootWorkQid, work.WorkQid, StringComparison.OrdinalIgnoreCase)
            ? []
            : await BuildExplicitCastAsync(work.RootWorkQid, rootRankMap, db, ct);
        AddUniqueCredits(credits, rootExplicitCredits);

        var linkedActors = await BuildActorOnlyCreditsFromMediaLinksAsync(workId, workRankMap, db, ct);
        AddUniqueCredits(credits, linkedActors);

        var fallbackCredits = await BuildFallbackCreditsFromCanonicalArrayAsync(workId, canonicalArrayRepo, personRepo, ct);
        AddUniqueCredits(credits, fallbackCredits);

        fallbackCredits = await BuildFallbackCreditsFromMetadataClaimsAsync(workId, personRepo, db, ct);
        AddUniqueCredits(credits, fallbackCredits);

        if (work.RootWorkId.HasValue && work.RootWorkId.Value != workId)
        {
            fallbackCredits = await BuildFallbackCreditsFromCanonicalArrayAsync(work.RootWorkId.Value, canonicalArrayRepo, personRepo, ct);
            AddUniqueCredits(credits, fallbackCredits);

            fallbackCredits = await BuildFallbackCreditsFromMetadataClaimsAsync(work.RootWorkId.Value, personRepo, db, ct);
            AddUniqueCredits(credits, fallbackCredits);
        }

        return credits.Take(MaxCastCredits).ToList();
    }

    public static async Task<List<CastCreditDto>> BuildForCollectionRootAsync(
        Guid rootWorkId,
        string? rootWorkQid,
        ICanonicalValueArrayRepository canonicalArrayRepo,
        IPersonRepository personRepo,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        var rankMap = await CastRankMap.BuildAsync(rootWorkId, canonicalArrayRepo, db, ct);
        var explicitCredits = string.IsNullOrWhiteSpace(rootWorkQid)
            ? []
            : await BuildExplicitCastAsync(rootWorkQid, rankMap, db, ct);
        if (explicitCredits.Count > 0)
            return explicitCredits;

        var fallbackCredits = await BuildFallbackCreditsFromCanonicalArrayAsync(rootWorkId, canonicalArrayRepo, personRepo, ct);
        AddUniqueCredits(explicitCredits, fallbackCredits);
        fallbackCredits = await BuildFallbackCreditsFromMetadataClaimsAsync(rootWorkId, personRepo, db, ct);
        AddUniqueCredits(explicitCredits, fallbackCredits);
        return explicitCredits.Take(MaxCastCredits).ToList();
    }

    private static void AddUniqueCredits(List<CastCreditDto> destination, IEnumerable<CastCreditDto> source)
    {
        foreach (var credit in source)
        {
            var duplicate = destination.Any(existing =>
                (credit.PersonId.HasValue && existing.PersonId == credit.PersonId)
                || (!string.IsNullOrWhiteSpace(credit.WikidataQid)
                    && string.Equals(existing.WikidataQid, credit.WikidataQid, StringComparison.OrdinalIgnoreCase))
                || string.Equals(existing.Name, credit.Name, StringComparison.OrdinalIgnoreCase));
            if (!duplicate)
                destination.Add(credit);

            if (destination.Count >= MaxCastCredits)
                return;
        }
    }

    private static async Task<List<CastCreditDto>> BuildExplicitCastAsync(
        string workQid,
        CastRankMap rankMap,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        using var conn = db.CreateConnection();
        var rows = (await conn.QueryAsync<ExplicitCastRow>(
            """
            SELECT cpl.rowid             AS LinkOrder,
                   p.id                  AS ActorPersonId,
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
            ORDER BY cpl.rowid, fe.label, cp.is_default DESC;
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
            .Select(group =>
            {
                var sourceOrder = group.Min(row => row.LinkOrder);
                var credit = new CastCreditDto
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
                                .OrderBy(row => row.LinkOrder)
                                .ThenByDescending(row => row.PortraitIsDefault)
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
                                    preferred.PortraitImageUrl),
                            };
                        })
                        .OrderBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                };

                return new RankedCastCredit(
                    credit,
                    rankMap.RankFor(group.Key.ActorQid, group.Key.ActorName) ?? sourceOrder,
                    sourceOrder);
            })
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.SourceOrder)
            .ThenBy(item => item.Credit.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxCastCredits)
            .Select(item => item.Credit)
            .ToList();
    }

    private static async Task<List<CastCreditDto>> BuildActorOnlyCreditsFromMediaLinksAsync(
        Guid workId,
        CastRankMap rankMap,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        using var conn = db.CreateConnection();
        var rows = (await conn.QueryAsync<ActorOnlyRow>(
            """
            SELECT MIN(pml.rowid)        AS LinkOrder,
                   p.id                  AS ActorPersonId,
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
            ORDER BY MIN(pml.rowid);
            """,
            new { workId = workId.ToString() })).ToList();

        return rows
            .Where(row => row.ActorPersonId.HasValue && !string.IsNullOrWhiteSpace(row.ActorName))
            .Select(row =>
            {
                var credit = new CastCreditDto
                {
                    PersonId = row.ActorPersonId,
                    Name = row.ActorName ?? "Unknown",
                    WikidataQid = row.ActorQid,
                    HeadshotUrl = BuildHeadshotUrl(row.ActorPersonId, row.ActorLocalHeadshotPath, row.ActorHeadshotUrl),
                };

                return new RankedCastCredit(
                    credit,
                    rankMap.RankFor(row.ActorQid, row.ActorName) ?? row.LinkOrder,
                    row.LinkOrder);
            })
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.SourceOrder)
            .ThenBy(item => item.Credit.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxCastCredits)
            .Select(item => item.Credit)
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
        foreach (var entry in castEntries.OrderBy(value => value.Ordinal).Take(MaxCastCredits))
        {
            if (string.IsNullOrWhiteSpace(entry.Value))
                continue;

            Person? person = null;
            var qid = ExtractQid(entry.ValueQid);
            if (!string.IsNullOrWhiteSpace(qid))
                person = await personRepo.FindByQidAsync(qid, ct);
            person ??= await personRepo.FindByNameAsync(entry.Value, ct);

            credits.Add(new CastCreditDto
            {
                PersonId = person?.Id,
                Name = person?.Name ?? entry.Value,
                WikidataQid = qid ?? person?.WikidataQid,
                HeadshotUrl = BuildHeadshotUrl(person?.Id, person?.LocalHeadshotPath, person?.HeadshotUrl),
            });
        }

        return credits;
    }

    private static async Task<List<CastCreditDto>> BuildFallbackCreditsFromMetadataClaimsAsync(
        Guid workId,
        IPersonRepository personRepo,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        using var conn = db.CreateConnection();
        var rows = (await conn.QueryAsync<CastClaimRow>(new CommandDefinition(
            """
            SELECT mc.rowid       AS RowNumber,
                   mc.claim_key   AS ClaimKey,
                   mc.claim_value AS ClaimValue
            FROM metadata_claims mc
            WHERE mc.entity_id = @workId
              AND mc.claim_key IN ('cast_member', 'cast_member_qid', 'cast_member_character')
              AND NULLIF(mc.claim_value, '') IS NOT NULL
            ORDER BY mc.rowid;
            """,
            new { workId = workId.ToString("D") },
            cancellationToken: ct))).ToList();

        var entries = BuildCastEntriesFromClaims(rows)
            .Take(MaxCastCredits)
            .ToList();

        var credits = new List<CastCreditDto>(entries.Count);
        foreach (var entry in entries)
        {
            Person? person = null;
            if (!string.IsNullOrWhiteSpace(entry.Qid))
                person = await personRepo.FindByQidAsync(entry.Qid, ct);
            person ??= await personRepo.FindByNameAsync(entry.Name, ct);

            credits.Add(new CastCreditDto
            {
                PersonId = person?.Id,
                Name = person?.Name ?? entry.Name,
                WikidataQid = entry.Qid ?? person?.WikidataQid,
                HeadshotUrl = BuildHeadshotUrl(person?.Id, person?.LocalHeadshotPath, person?.HeadshotUrl),
                Characters = BuildClaimCharacterPortrayals(workId, entry.Name, entry.Characters),
            });
        }

        return credits;
    }

    private static List<CastClaimEntry> BuildCastEntriesFromClaims(IReadOnlyList<CastClaimRow> rows)
    {
        var nameClaims = rows
            .Where(row => row.ClaimKey.Equals("cast_member", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var qidClaims = rows
            .Where(row => row.ClaimKey.Equals("cast_member_qid", StringComparison.OrdinalIgnoreCase))
            .Select(row => ParseQidLabel(row.ClaimValue))
            .Where(parsed => !string.IsNullOrWhiteSpace(parsed.Qid) || !string.IsNullOrWhiteSpace(parsed.Label))
            .ToList();
        var characterClaims = rows
            .Where(row => row.ClaimKey.Equals("cast_member_character", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.ClaimValue)
            .ToList();

        var qidByName = qidClaims
            .Where(parsed => !string.IsNullOrWhiteSpace(parsed.Label) && !string.IsNullOrWhiteSpace(parsed.Qid))
            .GroupBy(parsed => parsed.Label!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Qid, StringComparer.OrdinalIgnoreCase);

        var entries = new List<CastClaimEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < nameClaims.Count; i++)
        {
            var name = nameClaims[i].ClaimValue.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            qidByName.TryGetValue(name, out var qid);
            qid ??= i < qidClaims.Count ? qidClaims[i].Qid : null;
            var characters = i < characterClaims.Count
                ? SplitCharacterNames(characterClaims[i])
                : [];
            var key = qid ?? name;
            if (seen.Add(key))
                entries.Add(new CastClaimEntry(name, qid, characters));
        }

        foreach (var parsed in qidClaims)
        {
            var name = FirstNonBlank(parsed.Label, parsed.Qid);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var key = parsed.Qid ?? name;
            if (seen.Add(key))
                entries.Add(new CastClaimEntry(name, parsed.Qid, []));
        }

        return entries;
    }

    private static List<CharacterPortrayalDto> BuildClaimCharacterPortrayals(
        Guid workId,
        string actorName,
        IReadOnlyList<string> characterNames)
        => characterNames
            .Where(characterName => !string.IsNullOrWhiteSpace(characterName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(characterName => new CharacterPortrayalDto
            {
                FictionalEntityId = CreateDeterministicCharacterId(workId, actorName, characterName),
                CharacterName = characterName.Trim(),
            })
            .ToList();

    private static IReadOnlyList<string> SplitCharacterNames(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(" / ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static Guid CreateDeterministicCharacterId(Guid workId, string actorName, string characterName)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"{workId:D}|{actorName}|{characterName}"));
        return new Guid(bytes);
    }

    private static (string? Qid, string? Label) ParseQidLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (null, null);

        var trimmed = value.Trim();
        var delimiter = trimmed.IndexOf("::", StringComparison.Ordinal);
        if (delimiter > 0)
            return (ExtractQid(trimmed[..delimiter]), FirstNonBlank(trimmed[(delimiter + 2)..], null));

        return (ExtractQid(trimmed), null);
    }

    private static string? ExtractQid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        var delimiter = trimmed.IndexOf("::", StringComparison.Ordinal);
        if (delimiter > 0)
            trimmed = trimmed[..delimiter].Trim();

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? BuildHeadshotUrl(Guid? personId, string? localHeadshotPath, string? remoteHeadshotUrl)
        => personId.HasValue
            ? ApiImageUrls.BuildPersonHeadshotUrl(personId.Value, localHeadshotPath, remoteHeadshotUrl)
            : remoteHeadshotUrl;

    private sealed record RankedCastCredit(CastCreditDto Credit, long Rank, long SourceOrder);

    private sealed class CastRankMap
    {
        public static readonly CastRankMap Empty = new();

        private readonly Dictionary<string, int> _qidRanks = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _nameRanks = new(StringComparer.OrdinalIgnoreCase);

        public static async Task<CastRankMap> BuildAsync(
            Guid workId,
            ICanonicalValueArrayRepository canonicalArrayRepo,
            IDatabaseConnection db,
            CancellationToken ct)
        {
            var map = new CastRankMap();
            var nextRank = 0;

            using var conn = db.CreateConnection();
            var assetCastEntries = (await conn.QueryAsync<CanonicalArrayEntryRow>(new CommandDefinition(
                """
                SELECT cva.ordinal   AS Ordinal,
                       cva.value     AS Value,
                       cva.value_qid AS ValueQid
                FROM editions e
                INNER JOIN media_assets ma
                    ON ma.edition_id = e.id
                INNER JOIN canonical_value_arrays cva
                    ON cva.entity_id = ma.id
                   AND cva.key = 'cast_member'
                WHERE e.work_id = @workId
                ORDER BY cva.ordinal ASC;
                """,
                new { workId = workId.ToString("D") },
                cancellationToken: ct))).ToList();

            foreach (var entry in assetCastEntries)
            {
                if (map.Add(entry.ValueQid, entry.Value, nextRank))
                    nextRank++;
            }

            var castEntries = await canonicalArrayRepo.GetValuesAsync(workId, "cast_member", ct);
            foreach (var entry in castEntries.OrderBy(value => value.Ordinal))
            {
                if (map.Add(entry.ValueQid, entry.Value, nextRank))
                    nextRank++;
            }

            var assetRows = (await conn.QueryAsync<CastClaimRow>(new CommandDefinition(
                """
                SELECT mc.rowid       AS RowNumber,
                       mc.claim_key   AS ClaimKey,
                       mc.claim_value AS ClaimValue
                FROM editions e
                INNER JOIN media_assets ma
                    ON ma.edition_id = e.id
                INNER JOIN metadata_claims mc
                    ON mc.entity_id = ma.id
                WHERE e.work_id = @workId
                  AND mc.claim_key IN ('cast_member', 'cast_member_qid')
                  AND NULLIF(mc.claim_value, '') IS NOT NULL
                ORDER BY mc.rowid;
                """,
                new { workId = workId.ToString("D") },
                cancellationToken: ct))).ToList();

            foreach (var entry in BuildCastEntriesFromClaims(assetRows))
            {
                if (map.Add(entry.Qid, entry.Name, nextRank))
                    nextRank++;
            }

            var rows = (await conn.QueryAsync<CastClaimRow>(new CommandDefinition(
                """
                SELECT mc.rowid       AS RowNumber,
                       mc.claim_key   AS ClaimKey,
                       mc.claim_value AS ClaimValue
                FROM metadata_claims mc
                WHERE mc.entity_id = @workId
                  AND mc.claim_key IN ('cast_member', 'cast_member_qid')
                  AND NULLIF(mc.claim_value, '') IS NOT NULL
                ORDER BY mc.rowid;
                """,
                new { workId = workId.ToString("D") },
                cancellationToken: ct))).ToList();

            foreach (var entry in BuildCastEntriesFromClaims(rows))
            {
                if (map.Add(entry.Qid, entry.Name, nextRank))
                    nextRank++;
            }

            return map;
        }

        public int? RankFor(string? qid, string? name)
        {
            var normalizedQid = NormalizeRankKey(ExtractQid(qid));
            if (!string.IsNullOrWhiteSpace(normalizedQid)
                && _qidRanks.TryGetValue(normalizedQid, out var qidRank))
            {
                return qidRank;
            }

            var normalizedName = NormalizeRankKey(name);
            return !string.IsNullOrWhiteSpace(normalizedName)
                && _nameRanks.TryGetValue(normalizedName, out var nameRank)
                ? nameRank
                : null;
        }

        private bool Add(string? qid, string? name, int rank)
        {
            var added = false;
            var normalizedQid = NormalizeRankKey(ExtractQid(qid));
            if (!string.IsNullOrWhiteSpace(normalizedQid) && !_qidRanks.ContainsKey(normalizedQid))
            {
                _qidRanks[normalizedQid] = rank;
                added = true;
            }

            var normalizedName = NormalizeRankKey(name);
            if (!string.IsNullOrWhiteSpace(normalizedName) && !_nameRanks.ContainsKey(normalizedName))
            {
                _nameRanks[normalizedName] = rank;
                added = true;
            }

            return added;
        }

        private static string? NormalizeRankKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToUpperInvariant();
        }
    }

    private sealed class WorkIdentityRow
    {
        public Guid WorkId { get; init; }
        public string? WorkQid { get; init; }
        public Guid? RootWorkId { get; init; }
        public string? RootWorkQid { get; init; }
    }

    private sealed class ExplicitCastRow
    {
        public long LinkOrder { get; init; }
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
        public long LinkOrder { get; init; }
        public Guid? ActorPersonId { get; init; }
        public string? ActorName { get; init; }
        public string? ActorQid { get; init; }
        public string? ActorHeadshotUrl { get; init; }
        public string? ActorLocalHeadshotPath { get; init; }
    }

    private sealed class CastClaimRow
    {
        public long RowNumber { get; init; }
        public string ClaimKey { get; init; } = string.Empty;
        public string ClaimValue { get; init; } = string.Empty;
    }

    private sealed class CanonicalArrayEntryRow
    {
        public int Ordinal { get; init; }
        public string Value { get; init; } = string.Empty;
        public string? ValueQid { get; init; }
    }

    private sealed record CastClaimEntry(string Name, string? Qid, IReadOnlyList<string> Characters);
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
                   c.display_name                         AS CollectionTitle,
                   c.wikidata_qid                         AS CollectionQid,
                   COALESCE(
                       NULLIF(TRIM(w.wikidata_qid), ''),
                       (SELECT cvq.value
                        FROM canonical_values cvq
                        WHERE cvq.entity_id = ma.id AND cvq.key = 'wikidata_qid'
                        LIMIT 1),
                       (SELECT cvq.value
                        FROM canonical_values cvq
                        WHERE cvq.entity_id = w.id AND cvq.key = 'wikidata_qid'
                        LIMIT 1),
                       (SELECT ij.resolved_qid
                        FROM identity_jobs ij
                        WHERE ij.entity_id = ma.id
                          AND ij.resolved_qid IS NOT NULL
                          AND TRIM(ij.resolved_qid) <> ''
                        ORDER BY ij.updated_at DESC, ij.created_at DESC
                        LIMIT 1)
                   )                                      AS WorkQid,
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
            .SelectMany(row => new[] { row.WorkQid, row.CollectionQid })
            .Where(qid => !string.IsNullOrWhiteSpace(qid))
            .Select(qid => qid!)
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
                                    preferred.PortraitImageUrl),
                            };
                        })
                        .OrderBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);
        }

        return NormalizeLibraryCreditRows(baseRows, charactersByWorkQid);
    }

    private static List<PersonLibraryCreditDto> NormalizeLibraryCreditRows(
        IReadOnlyList<PersonLibraryCreditRow> baseRows,
        IReadOnlyDictionary<string, List<CharacterPortrayalDto>> charactersByWorkQid)
        => baseRows
            .GroupBy(LibraryCreditGroupKey)
            .Select(group =>
            {
                var orderedRows = group
                    .OrderBy(row => row.Year, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var representative = orderedRows.First();
                var isTvSeriesCredit = IsTvMediaType(representative.MediaType) && representative.CollectionId.HasValue;
                var role = ResolveLibraryCreditRole(orderedRows, charactersByWorkQid, preferSeriesRole: isTvSeriesCredit);
                var characterQids = orderedRows
                    .SelectMany(row => new[] { row.CollectionQid, row.WorkQid })
                    .Where(qid => !string.IsNullOrWhiteSpace(qid))
                    .Select(qid => qid!)
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                var characters = characterQids
                    .SelectMany(qid => charactersByWorkQid.TryGetValue(qid, out var qidCharacters)
                        ? qidCharacters
                        : [])
                    .GroupBy(character => character.FictionalEntityId)
                    .Select(characterGroup => characterGroup.First())
                    .OrderBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var includeCharacters = role.Equals("Actor", StringComparison.OrdinalIgnoreCase)
                    || role.Equals("Voice Actor", StringComparison.OrdinalIgnoreCase);

                return new PersonLibraryCreditDto
                {
                    WorkId = representative.WorkId,
                    CollectionId = representative.CollectionId,
                    MediaType = representative.MediaType,
                    Title = isTvSeriesCredit
                        ? FirstNonBlank(representative.CollectionTitle, representative.Title, "Untitled")!
                        : FirstNonBlank(representative.Title, "Untitled")!,
                    CoverUrl = representative.FirstAssetId.HasValue
                        ? $"/stream/{representative.FirstAssetId.Value}/cover-thumb"
                        : null,
                    Year = representative.Year,
                    Role = role,
                    Characters = includeCharacters ? characters : [],
                };
            })
            .OrderByDescending(credit => credit.Year, StringComparer.OrdinalIgnoreCase)
            .ThenBy(credit => credit.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(credit => credit.Role, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string LibraryCreditGroupKey(PersonLibraryCreditRow row)
        => IsTvMediaType(row.MediaType) && row.CollectionId.HasValue
            ? $"tv::{row.CollectionId.Value:D}"
            : $"work::{row.WorkId:D}";

    private static string ResolveLibraryCreditRole(
        IReadOnlyList<PersonLibraryCreditRow> rows,
        IReadOnlyDictionary<string, List<CharacterPortrayalDto>> charactersByWorkQid,
        bool preferSeriesRole)
    {
        var hasCharacterEvidence = rows
            .SelectMany(row => new[] { row.CollectionQid, row.WorkQid })
            .Where(qid => !string.IsNullOrWhiteSpace(qid))
            .Any(qid => charactersByWorkQid.ContainsKey(qid!));

        if (hasCharacterEvidence)
            return "Actor";

        var roles = rows
            .Select(row => row.Role)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(LibraryCreditRoleRank)
            .ThenBy(role => role, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (roles.Count == 0)
            return "Credit";

        if (preferSeriesRole)
        {
            var seriesRole = roles.FirstOrDefault(role =>
                role.Equals("Actor", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Voice Actor", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(seriesRole))
                return seriesRole;
        }

        return roles[0];
    }

    private static int LibraryCreditRoleRank(string role)
        => role.Trim().ToLowerInvariant() switch
        {
            "actor" => 0,
            "voice actor" => 1,
            "author" => 2,
            "narrator" => 3,
            "director" => 4,
            "screenwriter" => 5,
            "writer" => 5,
            "producer" => 6,
            "composer" => 7,
            "artist" => 8,
            "performer" => 9,
            _ => 50,
        };

    private static bool IsTvMediaType(string? mediaType)
        => mediaType?.Contains("tv", StringComparison.OrdinalIgnoreCase) == true;

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

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
                   cpl.work_qid             AS WorkQid,
                   w.collection_id          AS CollectionId,
                   w.media_type             AS MediaType,
                   COALESCE(MAX(CASE WHEN cv.key = 'title' THEN cv.value END), 'Untitled') AS WorkTitle,
                   fe.fictional_universe_qid AS UniverseQid,
                   fe.fictional_universe_label AS UniverseLabel
            FROM character_performer_links cpl
            INNER JOIN fictional_entities fe
                ON fe.id = cpl.fictional_entity_id
            INNER JOIN works w
                ON cpl.work_qid = COALESCE(
                    NULLIF(TRIM(w.wikidata_qid), ''),
                    (SELECT cvq.value
                     FROM canonical_values cvq
                     WHERE cvq.entity_id = w.id AND cvq.key = 'wikidata_qid'
                     LIMIT 1),
                    (SELECT cvq.value
                     FROM editions e
                     INNER JOIN media_assets ma ON ma.edition_id = e.id
                     INNER JOIN canonical_values cvq ON cvq.entity_id = ma.id
                     WHERE e.work_id = w.id AND cvq.key = 'wikidata_qid'
                     LIMIT 1),
                    (SELECT ij.resolved_qid
                     FROM editions e
                     INNER JOIN media_assets ma ON ma.edition_id = e.id
                     INNER JOIN identity_jobs ij ON ij.entity_id = ma.id
                     WHERE e.work_id = w.id
                       AND ij.resolved_qid IS NOT NULL
                       AND TRIM(ij.resolved_qid) <> ''
                     ORDER BY ij.updated_at DESC, ij.created_at DESC
                     LIMIT 1)
                )
            LEFT JOIN canonical_values cv
                ON cv.entity_id = w.id
               AND cv.key = 'title'
            LEFT JOIN character_portraits cp
                ON cp.fictional_entity_id = fe.id
               AND cp.person_id = cpl.person_id
            WHERE cpl.person_id = @personId
              AND cpl.work_qid IS NOT NULL
            GROUP BY cpl.fictional_entity_id, fe.label, fe.image_url, cp.id, cp.image_url, cp.local_image_path, cp.is_default,
                     w.id, cpl.work_qid, w.collection_id, w.media_type, fe.fictional_universe_qid, fe.fictional_universe_label
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
                        preferred.PortraitImageUrl),
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
        public string? CollectionTitle { get; init; }
        public string? CollectionQid { get; init; }
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
