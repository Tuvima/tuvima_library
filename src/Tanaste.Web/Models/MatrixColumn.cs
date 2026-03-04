namespace Tanaste.Web.Models;

/// <summary>
/// Describes one column in the Metadata Prioritization Matrix.
/// Can represent a single claim key (e.g. "cover") or a group of keys
/// (e.g. "Universe Information" covering series, franchise, etc.).
/// </summary>
public sealed class MatrixColumn
{
    /// <summary>Zone identifier — matches the <c>MudDropZone.Identifier</c>.</summary>
    public string ZoneId { get; set; } = string.Empty;

    /// <summary>Display name shown in the column header.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Material icon shown in the column header.</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// The claim keys this column controls.
    /// Single-field columns have one entry; the Universe group has many.
    /// </summary>
    public List<string> ClaimKeys { get; set; } = [];

    /// <summary>Whether this is a group column that can be expanded to extract individual fields.</summary>
    public bool IsGroup { get; set; }

    /// <summary>Whether this column was added by the user (and can therefore be removed).</summary>
    public bool IsUserAdded { get; set; }

    /// <summary>Left-to-right display order.</summary>
    public int SortOrder { get; set; }
}
