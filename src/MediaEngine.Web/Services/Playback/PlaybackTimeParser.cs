namespace MediaEngine.Web.Services.Playback;

public static class PlaybackTimeParser
{
    public static double? TryParseDurationSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
        {
            return seconds >= 60000 ? seconds / 1000d : seconds;
        }

        var parts = text.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (2 or 3)
            || !parts.All(part => double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)))
        {
            return null;
        }

        var multiplier = 1d;
        var total = 0d;
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            total += double.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture) * multiplier;
            multiplier *= 60d;
        }

        return total;
    }

    public static string FormatDuration(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : span.ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }
}

