namespace MediaEngine.Domain.Enums;

/// <summary>
/// The level of a narrative root in the hierarchy.
/// Determined by the Wikidata property that resolved it.
/// </summary>
public static class NarrativeLevel
{
    /// <summary>P1434 — Broadest: a fictional universe (e.g. "Dune universe").</summary>
    public const string Universe = "Universe";

    /// <summary>P8345 — A franchise within or equal to a universe (e.g. "Dune franchise").</summary>
    public const string Franchise = "Franchise";

    /// <summary>P179 — A series of works (e.g. "Dune Chronicles").</summary>
    public const string Series = "Series";

    /// <summary>Fallback — standalone work with no broader Wikidata grouping.</summary>
    public const string Standalone = "Standalone";
}
