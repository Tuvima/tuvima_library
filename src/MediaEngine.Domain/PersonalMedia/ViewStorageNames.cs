namespace MediaEngine.Domain.PersonalMedia;

/// <summary>Portable, readable labels. Persist once; never derive authority from a display name.</summary>
public static class ViewStorageNames
{
    public static string FromDisplayName(string name)
    {
        var label = new string(name.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()).Trim('-');
        if (label.Length > 64) label = label[..64];
        if (string.IsNullOrWhiteSpace(label)) label = "profile";
        if (IsReserved(label)) label = "profile-" + label;
        return label;
    }

    public static bool IsValid(string label) => label.Length is > 0 and <= 110
        && label.All(c => char.IsLetterOrDigit(c) || c is '-' or '_') && !IsReserved(label);

    private static bool IsReserved(string label) => label.ToUpperInvariant() is
        "CON" or "PRN" or "AUX" or "NUL" or "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9"
        or "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9";
}
