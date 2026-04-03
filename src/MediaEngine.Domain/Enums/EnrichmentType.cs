namespace MediaEngine.Domain.Enums;

/// <summary>
/// Identifies a single enrichment concern that can be dispatched independently
/// by <c>IEnrichmentService</c>.
///
/// Each value maps to exactly one enrichment worker. Used for targeted re-runs
/// (e.g. "re-download cover art" from the Dashboard) and for the dispatcher's
/// <c>RunSingleEnrichmentAsync</c> method.
/// </summary>
public enum EnrichmentType
{
    /// <summary>Cover art download, pHash comparison, thumbnail + hero banner generation.</summary>
    CoverArt = 0,

    /// <summary>Person extraction from claims, standalone Wikidata reconciliation, person enrichment.</summary>
    Persons = 1,

    /// <summary>Child entity discovery: TV seasons/episodes, album tracks, comic issues.</summary>
    Children = 2,

    /// <summary>Fanart.tv backdrops, logos, and additional imagery.</summary>
    Images = 3,

    /// <summary>Fictional entity enrichment: characters, locations, narrative root.</summary>
    Fictional = 4,

    /// <summary>Wikipedia description fetch and persistence.</summary>
    Descriptions = 5,

    /// <summary>Write resolved metadata back to file tags.</summary>
    WriteBack = 6,
}
