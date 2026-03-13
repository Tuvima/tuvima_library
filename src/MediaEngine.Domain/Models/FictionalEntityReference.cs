namespace MediaEngine.Domain.Models;

/// <summary>
/// Lightweight reference to a fictional entity extracted from a work's claims.
/// Used by <see cref="Contracts.IRecursiveFictionalEntityService"/> to find-or-create
/// <see cref="Entities.FictionalEntity"/> records and link them to works.
/// </summary>
/// <param name="WikidataQid">The Wikidata Q-identifier (e.g. <c>"Q937618"</c>).</param>
/// <param name="Label">Human-readable label (e.g. <c>"Paul Atreides"</c>).</param>
/// <param name="EntitySubType">
/// One of <see cref="Enums.FictionalEntityType"/> constants:
/// <c>"Character"</c>, <c>"Location"</c>, or <c>"Organization"</c>.
/// </param>
public sealed record FictionalEntityReference(
    string WikidataQid,
    string Label,
    string EntitySubType);
