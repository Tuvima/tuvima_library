namespace MediaEngine.Domain.Enums;

/// <summary>
/// Available highlight colours for the EPUB reader.
/// Values are CSS hex colour codes stored directly in the database.
/// </summary>
public static class HighlightColor
{
    public const string Yellow = "#EAB308";
    public const string Blue   = "#60A5FA";
    public const string Green  = "#34D399";
    public const string Pink   = "#F472B6";
    public const string Purple = "#A78BFA";

    /// <summary>All valid highlight colours.</summary>
    public static readonly string[] All = [Yellow, Blue, Green, Pink, Purple];
}
