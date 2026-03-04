namespace MediaEngine.Domain.Enums;

/// <summary>
/// String constants for the <c>status</c> column in <c>review_queue</c>.
///
/// Stored as TEXT in SQLite with a CHECK constraint limiting values to
/// <c>Pending</c>, <c>Resolved</c>, and <c>Dismissed</c>.
/// </summary>
public static class ReviewStatus
{
    /// <summary>The review item is awaiting user action.</summary>
    public const string Pending = "Pending";

    /// <summary>
    /// The user resolved the review item — selected a QID, confirmed field
    /// overrides, or otherwise supplied the missing information.
    /// </summary>
    public const string Resolved = "Resolved";

    /// <summary>
    /// The user dismissed the review item as irrelevant or unactionable.
    /// </summary>
    public const string Dismissed = "Dismissed";
}
