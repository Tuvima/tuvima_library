namespace Tanaste.Web.Models;

/// <summary>
/// A draggable item in the Metadata Prioritization Matrix.
/// Represents one provider placed in one zone (field column or disabled pool).
/// A single provider (e.g. "Audnexus") may exist as multiple MatrixItem instances —
/// one per column it appears in.
/// </summary>
public sealed class MatrixItem
{
    /// <summary>Internal config name sent to the Engine for saves (e.g. "audnexus", "apple_books_ebook").</summary>
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>Human-readable name shown in the chip (e.g. "Audnexus", "Apple Books").</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Current zone identifier — matches a <c>MudDropZone.Identifier</c>.
    /// Values: a claim key ("cover", "title"), "universe_info", or "disabled".
    /// </summary>
    public string Zone { get; set; } = "disabled";

    /// <summary>Priority order within the zone (0 = highest).</summary>
    public int Order { get; set; }

    /// <summary>All claim keys this provider can supply (from <c>available_fields</c>).</summary>
    public HashSet<string> SupportedFields { get; set; } = [];

    /// <summary>Accent colour for the provider chip (hex string, e.g. "#FF9500").</summary>
    public string Color { get; set; } = "#78909C";

    /// <summary>Material Design icon string for the provider.</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>Optional custom image URL for provider branding.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Whether this provider is enabled at the Engine level.</summary>
    public bool Enabled { get; set; } = true;
}
