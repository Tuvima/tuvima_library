namespace Tanaste.Web.Models;

/// <summary>
/// A bridge field that a provider can contribute to the Universe (Wikidata)
/// cross-reference pipeline. Used in the provider flyout for reordering bridge priority.
/// </summary>
public sealed class BridgePriorityItem
{
    /// <summary>The claim key this provider produces (e.g. "isbn", "asin").</summary>
    public string ClaimKey { get; set; } = string.Empty;

    /// <summary>Human-readable display name (e.g. "ISBN-13").</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The Wikidata P-code this bridge maps to (e.g. "P212").</summary>
    public string WikidataPCode { get; set; } = string.Empty;

    /// <summary>Priority order (0 = highest). Determines bridge resolution order.</summary>
    public int Order { get; set; }

    /// <summary>Drop zone identifier for MudDropContainer reorder.</summary>
    public string Zone { get; set; } = "bridge_list";
}
