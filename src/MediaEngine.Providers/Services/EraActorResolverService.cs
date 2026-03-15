using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Resolves the correct actor for a character by examining performer relationship
/// edges with temporal qualifiers (P580 start time, P582 end time).
///
/// <para>
/// When a <c>timelineYear</c> is specified, the service finds the performer whose
/// temporal range contains that year. When no year is given or no temporal match
/// is found, it returns the most recently discovered performer.
/// </para>
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

        // Find performer edges where this character is the object.
        var edges = await _relRepo.GetByObjectAsync(characterQid, ct).ConfigureAwait(false);
        var performerEdges = edges
            .Where(e => e.RelationshipTypeValue == RelationshipType.Performer)
            .ToList();

        if (performerEdges.Count == 0)
            return null;

        EntityRelationship? matchedEdge = null;

        // If timeline year specified, find the performer active in that year.
        if (timelineYear.HasValue)
        {
            matchedEdge = performerEdges.FirstOrDefault(e =>
            {
                var startYear = ParseYear(e.StartTime);
                var endYear = ParseYear(e.EndTime);

                // If both bounds exist, check range.
                if (startYear.HasValue && endYear.HasValue)
                    return timelineYear.Value >= startYear.Value && timelineYear.Value <= endYear.Value;

                // If only start, performer is active from that year onward.
                if (startYear.HasValue)
                    return timelineYear.Value >= startYear.Value;

                // If only end, performer was active until that year.
                if (endYear.HasValue)
                    return timelineYear.Value <= endYear.Value;

                // No temporal data — not a match for era-specific query.
                return false;
            });
        }

        // Fallback: most recently discovered performer.
        matchedEdge ??= performerEdges
            .OrderByDescending(e => e.DiscoveredAt)
            .First();

        // Resolve the person record for the performer via their Wikidata QID.
        var actorQid = matchedEdge.SubjectQid;
        Person? person = null;
        try
        {
            person = await _personRepo.FindByQidAsync(actorQid, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to look up person record for actor QID {ActorQid}; proceeding without person detail.",
                actorQid);
        }

        return new ActorResolution(
            ActorPersonQid: actorQid,
            ActorLabel: person?.Name,
            HeadshotUrl: person?.HeadshotUrl,
            StartTime: matchedEdge.StartTime,
            EndTime: matchedEdge.EndTime,
            ContextWorkQid: matchedEdge.ContextWorkQid);
    }

    private static int? ParseYear(string? isoDate)
    {
        if (string.IsNullOrWhiteSpace(isoDate))
            return null;

        // Try parsing as a 4-digit year directly.
        if (isoDate.Length == 4 && int.TryParse(isoDate, out var year4))
            return year4;

        // Try extracting year from ISO 8601 date (e.g. "1984-06-01").
        if (isoDate.Length >= 4 && int.TryParse(isoDate[..4], out var yearPrefix))
            return yearPrefix;

        // Try full DateTimeOffset parse as final fallback.
        if (DateTimeOffset.TryParse(isoDate, out var dto))
            return dto.Year;

        return null;
    }
}
