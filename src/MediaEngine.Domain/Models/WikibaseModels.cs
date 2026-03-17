namespace MediaEngine.Domain.Models;

/// <summary>
/// A Wikidata statement with its qualifiers, as returned by the <c>wbgetclaims</c> API action.
///
/// <para>
/// Example: P161 (cast_member) → Q15072805 (Oscar Isaac) with qualifier
/// P453 (character) → Q3079065 (Duke Leto Atreides).
/// </para>
/// </summary>
/// <param name="ValueQid">The main value QID (e.g. "Q15072805" for the actor). Null for novalue/somevalue snaks.</param>
/// <param name="ValueLabel">The main value label if resolved (e.g. "Oscar Isaac"). Null if not resolved.</param>
/// <param name="Rank">Statement rank: "preferred", "normal", or "deprecated".</param>
/// <param name="Qualifiers">Qualifiers attached to this statement, keyed by property ID (e.g. "P453").</param>
public sealed record QualifiedStatement(
    string? ValueQid,
    string? ValueLabel,
    string Rank,
    IReadOnlyDictionary<string, IReadOnlyList<QualifierValue>> Qualifiers
);

/// <summary>
/// A qualifier value from a Wikidata statement.
/// </summary>
/// <param name="EntityQid">QID if this qualifier is entity-valued; null for literal values.</param>
/// <param name="Label">The label (for entity-valued) or the raw string/time value (for literals).</param>
/// <param name="DataType">Raw Wikibase datatype (e.g. "wikibase-item", "time", "string").</param>
public sealed record QualifierValue(
    string? EntityQid,
    string? Label,
    string DataType
);

/// <summary>
/// A Wikidata entity with basic identifying information, as returned by the <c>wbgetentities</c> API action.
/// </summary>
/// <param name="Qid">The Wikidata QID (e.g. "Q15072805").</param>
/// <param name="Label">The label in the requested language, or null if absent.</param>
/// <param name="Description">The description in the requested language, or null if absent.</param>
/// <param name="Sitelinks">Map of site key → article title (e.g. "enwiki" → "Oscar Isaac").</param>
public sealed record WikibaseEntity(
    string Qid,
    string? Label,
    string? Description,
    IReadOnlyDictionary<string, string> Sitelinks
);
