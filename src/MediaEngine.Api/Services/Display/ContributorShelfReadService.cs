using Dapper;
using MediaEngine.Api.Endpoints;
using MediaEngine.Contracts.Collections;
using MediaEngine.Domain.Services;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Display;

/// <summary>
/// Builds the cross-lane Collections shelf catalog from canonical primary
/// contributor credits. Structural containers (series, shows, and albums)
/// are deliberately not shelves here; each result represents two or more
/// distinct owned top-level works by one contributor in one meaningful role.
/// </summary>
public sealed class ContributorShelfReadService
{
    private readonly DisplayWorkProjectionReader _works;
    private readonly IDatabaseConnection _db;

    public ContributorShelfReadService(DisplayWorkProjectionReader works, IDatabaseConnection db)
    {
        _works = works;
        _db = db;
    }

    public async Task<IReadOnlyList<ContributorShelfDto>> LoadAsync(CancellationToken ct)
    {
        var works = await _works.LoadAsync(ct);
        if (works.Count == 0)
            return [];

        var workByAsset = works.ToDictionary(work => work.AssetId);
        using var conn = _db.CreateConnection();
        ct.ThrowIfCancellationRequested();
        var credits = conn.Query<ContributorCreditRow>(new CommandDefinition(
            """
            SELECT credit.media_asset_id AS AssetId,
                   credit.person_id AS PersonId,
                   credit.person_name AS PersonName,
                   credit.role AS Role,
                   person.wikidata_qid AS WikidataQid,
                   person.headshot_url AS HeadshotUrl,
                   person.local_headshot_path AS LocalHeadshotPath,
                   person.is_pseudonym AS IsPseudonym
            FROM primary_person_media_credits credit
            INNER JOIN persons person ON person.id = credit.person_id
            WHERE credit.role != 'Author'
               OR NOT EXISTS (
                  SELECT 1
                  FROM primary_person_media_credits collective
                  INNER JOIN persons collective_person ON collective_person.id = collective.person_id
                  WHERE collective.media_asset_id = credit.media_asset_id
                    AND collective.role = credit.role
                    AND collective_person.is_pseudonym = 1
                    AND credit.person_id != collective.person_id
              )
            ORDER BY credit.media_asset_id, credit.billing_order;
            """,
            cancellationToken: ct)).ToList();

        var candidates = credits
            .Where(credit => workByAsset.ContainsKey(credit.AssetId))
            .Select(credit => CreateCandidate(credit, workByAsset[credit.AssetId]))
            .Where(candidate => candidate is not null)
            .Cast<ContributorShelfCandidate>()
            .ToList();
        var identityEvidence = candidates
            .GroupBy(BuildNameScope)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(candidate => candidate.WikidataQid?.Trim())
                    .Where(qid => !string.IsNullOrWhiteSpace(qid))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Cast<string>()
                    .ToList());

        var shelves = candidates
            .GroupBy(candidate => new ContributorShelfIdentity(
                ResolveIdentityKey(candidate, identityEvidence[BuildNameScope(candidate)]),
                candidate.Role,
                candidate.Lane,
                candidate.ShelfType))
            .Select(BuildShelf)
            .Where(shelf => shelf is not null)
            .Cast<ContributorShelfDto>()
            .ToList();

        return shelves
            .GroupBy(shelf => new ContributorShelfPresentationIdentity(
                NormalizeIdentityName(shelf.PersonName),
                shelf.Role,
                shelf.Lane,
                shelf.ShelfType,
                string.Join(',', shelf.Items.Select(item => item.WorkId).Order())))
            .Select(group => group
                .OrderByDescending(shelf => !string.IsNullOrWhiteSpace(shelf.HeadshotUrl))
                .ThenBy(shelf => shelf.PersonId)
                .First())
            .OrderBy(shelf => shelf.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ContributorShelfCandidate? CreateCandidate(ContributorCreditRow credit, DisplayWorkRow work)
    {
        var definition = ShelfDefinition(work.MediaType, credit.Role);
        if (definition is null)
            return null;

        var isMusic = work.MediaType.Contains("music", StringComparison.OrdinalIgnoreCase);
        return new ContributorShelfCandidate(
            credit.PersonId,
            credit.PersonName,
            ApiImageUrls.BuildPersonHeadshotUrl(credit.PersonId, credit.LocalHeadshotPath, credit.HeadshotUrl),
            credit.WikidataQid,
            definition.Value.Role,
            definition.Value.Lane,
            definition.Value.ShelfType,
            isMusic ? work.RootWorkId : work.WorkId,
            isMusic ? StringHelpers.FirstNonBlankOr("Untitled", work.Album, work.Title) : work.Title,
            work.MediaType,
            isMusic
                ? StringHelpers.FirstNonBlankOr(string.Empty, work.RootSquareUrl, work.RootCoverUrl, work.SquareUrl, work.CoverUrl)
                : StringHelpers.FirstNonBlankOr(string.Empty, work.CoverUrl, work.SquareUrl),
            ParseYear(work.Year));
    }

    private static ContributorShelfDto? BuildShelf(
        IGrouping<ContributorShelfIdentity, ContributorShelfCandidate> group)
    {
        var items = group
            .GroupBy(item => item.WorkId)
            .Select(itemGroup => itemGroup
                .OrderByDescending(item => !string.IsNullOrWhiteSpace(item.CoverUrl))
                .ThenByDescending(IdentityQuality)
                .First())
            .OrderBy(item => item.Year ?? int.MaxValue)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (items.Count < 2)
            return null;

        var identity = group
            .OrderByDescending(IdentityQuality)
            .ThenBy(candidate => candidate.PersonId)
            .First();
        var years = items
            .Where(item => item.Year.HasValue)
            .Select(item => item.Year!.Value)
            .ToList();

        return new ContributorShelfDto
        {
            Key = $"{group.Key.ShelfType}:{identity.PersonId:D}",
            PersonId = identity.PersonId,
            PersonName = identity.PersonName,
            HeadshotUrl = identity.HeadshotUrl,
            Role = group.Key.Role,
            Lane = group.Key.Lane,
            ShelfType = group.Key.ShelfType,
            Title = ShelfTitle(group.Key.ShelfType, identity.PersonName),
            OwnedCount = items.Count,
            EarliestYear = years.Count > 0 ? years.Min() : null,
            LatestYear = years.Count > 0 ? years.Max() : null,
            Items = items.Select(item => new ContributorShelfItemDto
            {
                WorkId = item.WorkId,
                Title = item.Title,
                MediaType = item.MediaType,
                CoverUrl = item.CoverUrl,
                Year = item.Year,
            }).ToList(),
        };
    }

    private static int IdentityQuality(ContributorShelfCandidate candidate) =>
        (string.IsNullOrWhiteSpace(candidate.WikidataQid) ? 0 : 4)
        + (string.IsNullOrWhiteSpace(candidate.HeadshotUrl) ? 0 : 2);

    private static string NormalizeIdentityName(string name) =>
        string.Join(' ', name.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();

    private static ContributorShelfNameScope BuildNameScope(ContributorShelfCandidate candidate) => new(
        NormalizeIdentityName(candidate.PersonName),
        candidate.Role,
        candidate.Lane,
        candidate.ShelfType);

    private static string ResolveIdentityKey(
        ContributorShelfCandidate candidate,
        IReadOnlyList<string> knownQids)
    {
        if (knownQids.Count == 1)
            return $"QID:{knownQids[0].ToUpperInvariant()}";
        if (!string.IsNullOrWhiteSpace(candidate.WikidataQid))
            return $"QID:{candidate.WikidataQid.Trim().ToUpperInvariant()}";
        return $"NAME:{NormalizeIdentityName(candidate.PersonName)}";
    }

    private static (string Role, string Lane, string ShelfType)? ShelfDefinition(string mediaType, string role)
    {
        if (mediaType.Contains("tv", StringComparison.OrdinalIgnoreCase))
            return null;
        if (mediaType.Contains("movie", StringComparison.OrdinalIgnoreCase)
            && role.Equals("Director", StringComparison.OrdinalIgnoreCase))
            return ("Director", "Watch", "MoviesByDirector");
        if (mediaType.Contains("comic", StringComparison.OrdinalIgnoreCase)
            && (role.Equals("Author", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Screenwriter", StringComparison.OrdinalIgnoreCase)))
            return (role.Equals("Screenwriter", StringComparison.OrdinalIgnoreCase) ? "Writer" : "Author", "Read", "ComicsByCreator");
        if (mediaType.Contains("audio", StringComparison.OrdinalIgnoreCase)
            && (role.Equals("Author", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Narrator", StringComparison.OrdinalIgnoreCase)))
            return (role, "Listen", role.Equals("Narrator", StringComparison.OrdinalIgnoreCase) ? "AudiobooksByNarrator" : "AudiobooksByAuthor");
        if (mediaType.Contains("book", StringComparison.OrdinalIgnoreCase)
            && role.Equals("Author", StringComparison.OrdinalIgnoreCase))
            return ("Author", "Read", "BooksByAuthor");
        if (mediaType.Contains("music", StringComparison.OrdinalIgnoreCase)
            && (role.Equals("Artist", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Performer", StringComparison.OrdinalIgnoreCase)))
            return ("Artist", "Listen", "AlbumsByArtist");
        return null;
    }

    private static string ShelfTitle(string shelfType, string personName) => shelfType switch
    {
        "BooksByAuthor" => $"Books by {personName}",
        "ComicsByCreator" => $"Comics by {personName}",
        "MoviesByDirector" => $"Movies directed by {personName}",
        "AlbumsByArtist" => $"Albums by {personName}",
        "AudiobooksByNarrator" => $"Audiobooks narrated by {personName}",
        "AudiobooksByAuthor" => $"Audiobooks by {personName}",
        _ => $"Works by {personName}",
    };

    private static int? ParseYear(string? value)
        => int.TryParse(value?.Length >= 4 ? value[..4] : value, out var year) ? year : null;

    private sealed class ContributorCreditRow
    {
        public Guid AssetId { get; init; }
        public Guid PersonId { get; init; }
        public string PersonName { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public string? WikidataQid { get; init; }
        public string? HeadshotUrl { get; init; }
        public string? LocalHeadshotPath { get; init; }
        public bool IsPseudonym { get; init; }
    }

    private sealed record ContributorShelfCandidate(
        Guid PersonId,
        string PersonName,
        string? HeadshotUrl,
        string? WikidataQid,
        string Role,
        string Lane,
        string ShelfType,
        Guid WorkId,
        string Title,
        string MediaType,
        string? CoverUrl,
        int? Year);

    private sealed record ContributorShelfIdentity(
        string IdentityKey,
        string Role,
        string Lane,
        string ShelfType);

    private sealed record ContributorShelfNameScope(
        string PersonName,
        string Role,
        string Lane,
        string ShelfType);

    private sealed record ContributorShelfPresentationIdentity(
        string PersonName,
        string Role,
        string Lane,
        string ShelfType,
        string WorkSignature);
}
