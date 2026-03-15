using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Resolves the correct actor for a character at a given timeline year,
/// using temporal qualifiers on performer relationship edges.
/// </summary>
public interface IEraActorResolverService
{
    /// <summary>
    /// Find the performer (actor) for the given character, optionally filtered
    /// to a specific timeline year. Returns <c>null</c> if no performer is found.
    /// </summary>
    /// <param name="characterQid">Wikidata QID of the fictional character.</param>
    /// <param name="timelineYear">Optional: filter to performer active in this year.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ActorResolution?> ResolveActorForEraAsync(
        string characterQid, int? timelineYear = null, CancellationToken ct = default);
}
