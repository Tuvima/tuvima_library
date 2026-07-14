using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Resolves performers for fictional characters using temporal qualifiers on
/// relationship edges. Multi-character requests use bounded batch reads.
/// </summary>
public sealed class EraActorResolverService : IEraActorResolverService
{
    private readonly IEntityRelationshipRepository _relRepo;
    private readonly IPersonRepository _personRepo;
    private readonly ILogger<EraActorResolverService> _logger;

    public EraActorResolverService(
        IEntityRelationshipRepository relRepo,
        IPersonRepository personRepo,
        ILogger<EraActorResolverService> logger)
    {
        ArgumentNullException.ThrowIfNull(relRepo);
        ArgumentNullException.ThrowIfNull(personRepo);
        ArgumentNullException.ThrowIfNull(logger);
        _relRepo = relRepo;
        _personRepo = personRepo;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ActorResolution?> ResolveActorForEraAsync(
        string characterQid, int? timelineYear = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterQid);

        var resolutions = await ResolveActorsForEraAsync([characterQid], timelineYear, ct)
            .ConfigureAwait(false);
        return resolutions.GetValueOrDefault(characterQid);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, ActorResolution>> ResolveActorsForEraAsync(
        IEnumerable<string> characterQids,
        int? timelineYear = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(characterQids);
        ct.ThrowIfCancellationRequested();

        var qids = characterQids
            .Where(qid => !string.IsNullOrWhiteSpace(qid))
            .Select(qid => qid.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (qids.Count == 0)
            return new Dictionary<string, ActorResolution>(StringComparer.OrdinalIgnoreCase);

        var performerEdges = (await _relRepo.GetByObjectsAsync(qids, ct).ConfigureAwait(false))
            .Where(edge => edge.RelationshipTypeValue == RelationshipType.Performer)
            .ToList();
        if (performerEdges.Count == 0)
            return new Dictionary<string, ActorResolution>(StringComparer.OrdinalIgnoreCase);

        var matchedEdges = new Dictionary<string, EntityRelationship>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in performerEdges.GroupBy(edge => edge.ObjectQid, StringComparer.OrdinalIgnoreCase))
        {
            var matchedEdge = SelectPerformerForEra(group, timelineYear);
            if (matchedEdge is not null)
                matchedEdges[group.Key] = matchedEdge;
        }

        var peopleByQid = new Dictionary<string, Person>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var people = await _personRepo.FindByQidsAsync(
                matchedEdges.Values.Select(edge => edge.SubjectQid), ct).ConfigureAwait(false);
            peopleByQid = people
                .Where(person => !string.IsNullOrWhiteSpace(person.WikidataQid))
                .GroupBy(person => person.WikidataQid!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to look up person records for {ActorCount} era performer QIDs; proceeding without person detail.",
                matchedEdges.Count);
        }

        var results = new Dictionary<string, ActorResolution>(StringComparer.OrdinalIgnoreCase);
        foreach (var (characterQid, edge) in matchedEdges)
        {
            peopleByQid.TryGetValue(edge.SubjectQid, out var person);
            results[characterQid] = new ActorResolution(
                ActorPersonQid: edge.SubjectQid,
                ActorPersonId: person?.Id,
                ActorLabel: person?.Name,
                HeadshotUrl: person?.HeadshotUrl,
                LocalHeadshotPath: person?.LocalHeadshotPath,
                StartTime: edge.StartTime,
                EndTime: edge.EndTime,
                ContextWorkQid: edge.ContextWorkQid);
        }

        return results;
    }

    private static EntityRelationship? SelectPerformerForEra(
        IEnumerable<EntityRelationship> performerEdges,
        int? timelineYear)
    {
        var edges = performerEdges.ToList();
        if (edges.Count == 0)
            return null;

        EntityRelationship? matchedEdge = null;
        if (timelineYear.HasValue)
        {
            matchedEdge = edges.FirstOrDefault(edge =>
            {
                var startYear = ParseYear(edge.StartTime);
                var endYear = ParseYear(edge.EndTime);

                if (startYear.HasValue && endYear.HasValue)
                    return timelineYear.Value >= startYear.Value && timelineYear.Value <= endYear.Value;
                if (startYear.HasValue)
                    return timelineYear.Value >= startYear.Value;
                if (endYear.HasValue)
                    return timelineYear.Value <= endYear.Value;
                return false;
            });
        }

        return matchedEdge ?? edges.OrderByDescending(edge => edge.DiscoveredAt).First();
    }

    private static int? ParseYear(string? isoDate)
    {
        if (string.IsNullOrWhiteSpace(isoDate))
            return null;

        if (isoDate.Length == 4 && int.TryParse(isoDate, out var year4))
            return year4;
        if (isoDate.Length >= 4 && int.TryParse(isoDate[..4], out var yearPrefix))
            return yearPrefix;
        if (DateTimeOffset.TryParse(isoDate, out var dto))
            return dto.Year;

        return null;
    }
}
