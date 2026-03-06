namespace MediaEngine.Web.Shared;

/// <summary>
/// Shared date formatting utilities used across Dashboard components.
/// </summary>
public static class DateFormatHelper
{
    /// <summary>
    /// Formats date values for display — 4-digit years pass through,
    /// parseable dates become "January 14, 2013", everything else as-is.
    /// </summary>
    public static string FormatFriendlyDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        // 4-digit year → return as-is.
        if (value.Length == 4 && int.TryParse(value, out _))
            return value;

        // Full date → friendly format.
        if (DateTimeOffset.TryParse(value, out var dto))
            return dto.DateTime.ToString("MMMM d, yyyy");
        if (DateTime.TryParse(value, out var dt))
            return dt.ToString("MMMM d, yyyy");

        return value;
    }
}
