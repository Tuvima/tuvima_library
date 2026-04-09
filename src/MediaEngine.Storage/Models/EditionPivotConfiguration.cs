using System.Text.Json.Serialization;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Storage.Models;

/// <summary>
/// Media-type → edition-pivot rule map loaded from <c>config/edition-pivot.json</c>.
/// Consumed by <c>ReconciliationAdapter.BuildStage2Request</c> to produce the
/// per-request <c>Tuvima.Wikidata.EditionPivotRule</c> that tells the
/// <c>Stage2Service</c> sub-service which Wikidata P31 classes identify work-level
/// vs edition-level entities.
///
/// <para>
/// Media types listed here are edition-aware; types not listed return
/// <see cref="EditionPivotRuleEntry"/>? null from <see cref="GetRuleFor"/> and are
/// resolved at the work level only. The rule keys in <see cref="Rules"/> are
/// compared against <see cref="MediaType"/> values via <c>ToString().ToLowerInvariant()</c>.
/// </para>
/// </summary>
public sealed class EditionPivotConfiguration
{
    /// <summary>
    /// Informational hint for hand-editing. Not read by code.
    /// </summary>
    [JsonPropertyName("$schema_hint")]
    public string? SchemaHint { get; set; }

    /// <summary>
    /// Media-type → rule map. Keys are lower-case media type names
    /// (e.g. <c>"books"</c>, <c>"audiobooks"</c>, <c>"music"</c>).
    /// </summary>
    [JsonPropertyName("rules")]
    public Dictionary<string, EditionPivotRuleEntry> Rules { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the rule for the given media type, or <c>null</c> when the
    /// media type is not edition-aware (movies, TV, comics, podcasts) or the
    /// config file is missing.
    /// </summary>
    public EditionPivotRuleEntry? GetRuleFor(MediaType mediaType)
    {
        var key = mediaType.ToString().ToLowerInvariant();
        return Rules.TryGetValue(key, out var rule) ? rule : null;
    }
}

/// <summary>
/// A single edition-pivot rule entry. Corresponds one-to-one with
/// <c>Tuvima.Wikidata.EditionPivotRule</c> but is serialisable from JSON —
/// the library's type is not.
/// </summary>
public sealed class EditionPivotRuleEntry
{
    /// <summary>
    /// P31 QIDs that identify work-level instances (e.g. Q7725634 "literary work").
    /// When the resolved entity matches one of these, no pivot is performed.
    /// </summary>
    [JsonPropertyName("work_classes")]
    public IReadOnlyList<string> WorkClasses { get; set; } = [];

    /// <summary>
    /// P31 QIDs that identify edition-level instances (e.g. Q3331189 "version, edition,
    /// or translation", Q122731938 "audiobook edition"). When the resolved entity matches
    /// one of these, the resolver walks P629 (edition of) to find the work.
    /// </summary>
    [JsonPropertyName("edition_classes")]
    public IReadOnlyList<string> EditionClasses { get; set; } = [];

    /// <summary>
    /// When <c>true</c>, prefer pivoting a work to its preferred edition (via P747 +
    /// ranking hints) rather than staying on the work. When <c>false</c>, edition → work
    /// pivoting still happens but work → edition does not. Default <c>false</c>.
    /// </summary>
    [JsonPropertyName("prefer_edition")]
    public bool PreferEdition { get; set; }
}
