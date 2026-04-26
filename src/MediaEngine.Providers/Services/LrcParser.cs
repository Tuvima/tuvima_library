using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MediaEngine.Providers.Services;

public static class LrcParser
{
    private static readonly Regex TimestampPattern = new(@"\[(?<min>\d{1,3}):(?<sec>\d{2})(?:[.:](?<frac>\d{1,3}))?\]", RegexOptions.Compiled);

    public static IReadOnlyList<LrcLine> Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        var lines = new List<LrcLine>();
        foreach (var rawLine in content.Replace("\r\n", "\n").Split('\n'))
        {
            var matches = TimestampPattern.Matches(rawLine);
            if (matches.Count == 0)
                continue;

            var text = TimestampPattern.Replace(rawLine, string.Empty).Trim();
            foreach (Match match in matches)
            {
                if (TryParseTimestamp(match, out var seconds))
                    lines.Add(new LrcLine(seconds, text));
            }
        }

        return lines.OrderBy(l => l.StartSeconds).ToList();
    }

    public static string Normalize(string content)
    {
        var parsed = Parse(content);
        if (parsed.Count == 0)
            return content.Replace("\r\n", "\n").Trim() + "\n";

        var sb = new StringBuilder();
        foreach (var line in parsed)
        {
            var span = TimeSpan.FromSeconds(line.StartSeconds);
            sb.Append('[')
              .Append(((int)span.TotalMinutes).ToString("00", CultureInfo.InvariantCulture))
              .Append(':')
              .Append(span.Seconds.ToString("00", CultureInfo.InvariantCulture))
              .Append('.')
              .Append((span.Milliseconds / 10).ToString("00", CultureInfo.InvariantCulture))
              .Append("] ")
              .AppendLine(line.Text);
        }

        return sb.ToString();
    }

    public static LrcLine? GetActiveLine(IReadOnlyList<LrcLine> lines, double playbackSeconds)
    {
        if (lines.Count == 0)
            return null;

        LrcLine? active = null;
        foreach (var line in lines)
        {
            if (line.StartSeconds > playbackSeconds)
                break;

            active = line;
        }

        return active;
    }

    private static bool TryParseTimestamp(Match match, out double seconds)
    {
        seconds = 0;
        if (!int.TryParse(match.Groups["min"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            || !int.TryParse(match.Groups["sec"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var secs))
        {
            return false;
        }

        var fraction = match.Groups["frac"].Success ? match.Groups["frac"].Value : "0";
        var fractionSeconds = fraction.Length switch
        {
            1 => int.Parse(fraction, CultureInfo.InvariantCulture) / 10d,
            2 => int.Parse(fraction, CultureInfo.InvariantCulture) / 100d,
            _ => int.Parse(fraction.PadRight(3, '0')[..3], CultureInfo.InvariantCulture) / 1000d,
        };

        seconds = minutes * 60d + secs + fractionSeconds;
        return true;
    }
}

public sealed record LrcLine(double StartSeconds, string Text);
