namespace MediaEngine.Domain.Models;

/// <summary>
/// A person signal extracted from a description or file metadata by the
/// <see cref="Contracts.IDescriptionSignalExtractor"/>. Signals start
/// unverified (no QID) and are promoted after batch Wikidata verification.
/// </summary>
/// <param name="Name">Display name extracted (e.g. "Scott Brick").</param>
/// <param name="Role">Person role: Narrator, Translator, Director, etc.</param>
/// <param name="Source">Where the signal came from: "description", "copyright", or "file_metadata".</param>
/// <param name="Pattern">Which regex pattern matched (for diagnostics).</param>
/// <param name="Confidence">Initial confidence (0.60 from description, 0.75 from file metadata).</param>
/// <param name="WikidataQid">Null until batch verification confirms the person.</param>
public sealed record ExtractedPersonSignal(
    string Name,
    string Role,
    string Source,
    string? Pattern,
    double Confidence,
    string? WikidataQid = null);
