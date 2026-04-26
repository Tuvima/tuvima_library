using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MediaEngine.Providers.Services;

public static class SubtitleNormalizer
{
    private static readonly Regex SrtTimingPattern = new(
        @"(?<start>\d{1,2}:\d{2}:\d{2},\d{3})\s*-->\s*(?<end>\d{1,2}:\d{2}:\d{2},\d{3})(?<settings>[^\r\n]*)",
        RegexOptions.Compiled);

    public static string NormalizeToWebVtt(string content, string sourceFormat)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "WEBVTT\n\n";

        if (sourceFormat.Equals("vtt", StringComparison.OrdinalIgnoreCase)
            || content.TrimStart().StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeLineEndings(content).TrimStart('\uFEFF') + "\n";
        }

        var normalized = NormalizeLineEndings(content).TrimStart('\uFEFF');
        if (sourceFormat.Equals("ass", StringComparison.OrdinalIgnoreCase)
            || sourceFormat.Equals("ssa", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("[Events]", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeAssToWebVtt(normalized);
        }

        var blocks = Regex.Split(normalized.Trim(), @"\n{2,}");
        var sb = new StringBuilder("WEBVTT\n\n");

        foreach (var block in blocks)
        {
            var lines = block.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0)
                continue;

            if (int.TryParse(lines[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                lines.RemoveAt(0);

            if (lines.Count == 0)
                continue;

            var timing = SrtTimingPattern.Match(lines[0]);
            if (!timing.Success)
                continue;

            var timingLine = SrtTimingPattern.Replace(lines[0], m =>
                $"{m.Groups["start"].Value.Replace(',', '.')} --> {m.Groups["end"].Value.Replace(',', '.')}{m.Groups["settings"].Value}");

            sb.AppendLine(timingLine);
            for (var i = 1; i < lines.Count; i++)
                sb.AppendLine(WebUtility.HtmlDecode(lines[i]));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string NormalizeAssToWebVtt(string normalized)
    {
        var format = new List<string>();
        var sb = new StringBuilder("WEBVTT\n\n");

        foreach (var line in normalized.Split('\n').Select(l => l.Trim()))
        {
            if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
            {
                format = line["Format:".Length..]
                    .Split(',')
                    .Select(part => part.Trim().ToLowerInvariant())
                    .ToList();
                continue;
            }

            if (!line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
                continue;

            var payload = line["Dialogue:".Length..].Trim();
            var fields = SplitAssDialogue(payload, Math.Max(format.Count, 10));
            var startIndex = format.IndexOf("start");
            var endIndex = format.IndexOf("end");
            var textIndex = format.IndexOf("text");
            if (startIndex < 0 || endIndex < 0 || textIndex < 0 || fields.Count <= Math.Max(startIndex, Math.Max(endIndex, textIndex)))
                continue;

            if (!TryFormatAssTimestamp(fields[startIndex], out var start)
                || !TryFormatAssTimestamp(fields[endIndex], out var end))
                continue;

            var text = Regex.Replace(fields[textIndex], @"\{[^}]*\}", string.Empty)
                .Replace(@"\N", "\n", StringComparison.Ordinal)
                .Replace(@"\n", "\n", StringComparison.Ordinal)
                .Trim();
            if (text.Length == 0)
                continue;

            sb.AppendLine($"{start} --> {end}");
            foreach (var textLine in text.Split('\n'))
                sb.AppendLine(WebUtility.HtmlDecode(textLine));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static List<string> SplitAssDialogue(string payload, int fieldCount)
    {
        var maxSplits = Math.Max(fieldCount - 1, 0);
        var fields = new List<string>();
        var start = 0;
        for (var i = 0; i < payload.Length && fields.Count < maxSplits; i++)
        {
            if (payload[i] != ',')
                continue;

            fields.Add(payload[start..i].Trim());
            start = i + 1;
        }

        fields.Add(payload[start..].Trim());
        return fields;
    }

    private static bool TryFormatAssTimestamp(string value, out string formatted)
    {
        formatted = string.Empty;
        var parts = value.Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
        {
            return false;
        }

        var secondParts = parts[2].Split('.');
        if (secondParts.Length == 0
            || !int.TryParse(secondParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        var fraction = secondParts.Length > 1 ? secondParts[1] : "0";
        if (!int.TryParse(fraction.PadRight(3, '0')[..3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds))
            return false;

        formatted = $"{hours:00}:{minutes:00}:{seconds:00}.{milliseconds:000}";
        return true;
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n").Replace('\r', '\n');
}
