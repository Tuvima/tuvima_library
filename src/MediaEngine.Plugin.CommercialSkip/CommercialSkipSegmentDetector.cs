using System.Globalization;
using System.Text.RegularExpressions;
using MediaEngine.Plugins;

namespace MediaEngine.Plugin.CommercialSkip;

public sealed class CommercialSkipSegmentDetector : IPlaybackSegmentDetector
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".webm", ".avi", ".ts", ".mpeg", ".mpg",
    };

    private static readonly Regex EdlLineRegex = new(
        @"^\s*(?<start>\d+(?:\.\d+)?)\s+(?<end>\d+(?:\.\d+)?)\s+(?<type>\d+)",
        RegexOptions.Compiled);

    private static readonly Regex BlackStartRegex = new(@"black_start:(?<value>\d+(?:\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex BlackEndRegex = new(@"black_end:(?<value>\d+(?:\.\d+)?)", RegexOptions.Compiled);

    private readonly PluginManifest _manifest;

    public CommercialSkipSegmentDetector(PluginManifest manifest)
    {
        _manifest = manifest;
    }

    public string Kind => "playback-segment-detector";

    public bool CanAnalyze(PluginMediaAssetContext asset)
    {
        return File.Exists(asset.FilePath) && VideoExtensions.Contains(Path.GetExtension(asset.FilePath));
    }

    public async Task<IReadOnlyList<PluginPlaybackSegment>> AnalyzeAsync(
        PluginMediaAssetContext asset,
        IPluginExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PluginPlaybackSegment>();
        if (GetBool(context, "use_comskip", true))
        {
            var comskip = _manifest.ToolRequirements.First(t => t.Id == "comskip");
            var resolved = await context.Tools.ResolveToolAsync(_manifest.Id, comskip, context.Settings, cancellationToken).ConfigureAwait(false);
            if (resolved.IsAvailable && resolved.ExecutablePath is not null)
            {
                results.AddRange(await RunComskipAsync(asset, context, resolved.ExecutablePath, cancellationToken).ConfigureAwait(false));
            }
        }

        if (results.Count == 0 && GetBool(context, "use_ffmpeg_fallback", true))
        {
            var ffmpeg = _manifest.ToolRequirements.First(t => t.Id == "ffmpeg");
            var resolved = await context.Tools.ResolveToolAsync(_manifest.Id, ffmpeg, context.Settings, cancellationToken).ConfigureAwait(false);
            if (resolved.IsAvailable && resolved.ExecutablePath is not null)
            {
                results.AddRange(await RunFfmpegFallbackAsync(asset, context, resolved.ExecutablePath, cancellationToken).ConfigureAwait(false));
            }
        }

        return results;
    }

    private static async Task<IReadOnlyList<PluginPlaybackSegment>> RunComskipAsync(
        PluginMediaAssetContext asset,
        IPluginExecutionContext context,
        string executable,
        CancellationToken ct)
    {
        var outputDir = Path.Combine(context.TempDirectory, "comskip");
        Directory.CreateDirectory(outputDir);
        var args = new[] { $"--output={outputDir}", asset.FilePath };
        var result = await context.Tools.RunToolAsync(executable, args, outputDir, TimeSpan.FromHours(2), ct).ConfigureAwait(false);
        if (result.TimedOut || result.ExitCode != 0)
            return [];

        var edl = Directory.EnumerateFiles(outputDir, "*.edl").FirstOrDefault();
        return edl is null ? [] : ParseEdl(edl, context);
    }

    private static async Task<IReadOnlyList<PluginPlaybackSegment>> RunFfmpegFallbackAsync(
        PluginMediaAssetContext asset,
        IPluginExecutionContext context,
        string executable,
        CancellationToken ct)
    {
        var args = new[]
        {
            "-hide_banner",
            "-i", asset.FilePath,
            "-vf", "blackdetect=d=0.5:pix_th=0.10",
            "-af", "silencedetect=n=-35dB:d=0.7",
            "-f", "null",
            "-"
        };
        var result = await context.Tools.RunToolAsync(executable, args, context.TempDirectory, TimeSpan.FromHours(1), ct).ConfigureAwait(false);
        var log = result.StandardError + Environment.NewLine + result.StandardOutput;
        return BuildFallbackSegments(log, context);
    }

    private static IReadOnlyList<PluginPlaybackSegment> ParseEdl(string path, IPluginExecutionContext context)
    {
        var min = GetDouble(context, "minimum_commercial_seconds", 30);
        var max = GetDouble(context, "maximum_commercial_seconds", 600);
        var results = new List<PluginPlaybackSegment>();
        foreach (var line in File.ReadLines(path))
        {
            var match = EdlLineRegex.Match(line);
            if (!match.Success) continue;
            var start = double.Parse(match.Groups["start"].Value, CultureInfo.InvariantCulture);
            var end = double.Parse(match.Groups["end"].Value, CultureInfo.InvariantCulture);
            var duration = end - start;
            if (duration < min || duration > max) continue;
            results.Add(Segment(start, end, "plugin:commercial-skip:comskip", 0.88));
        }
        return results;
    }

    private static IReadOnlyList<PluginPlaybackSegment> BuildFallbackSegments(string ffmpegLog, IPluginExecutionContext context)
    {
        var min = GetDouble(context, "minimum_commercial_seconds", 30);
        var max = GetDouble(context, "maximum_commercial_seconds", 600);
        var starts = BlackStartRegex.Matches(ffmpegLog).Select(m => double.Parse(m.Groups["value"].Value, CultureInfo.InvariantCulture)).ToList();
        var ends = BlackEndRegex.Matches(ffmpegLog).Select(m => double.Parse(m.Groups["value"].Value, CultureInfo.InvariantCulture)).ToList();
        var cuts = starts.Concat(ends).OrderBy(v => v).Distinct().ToList();
        var results = new List<PluginPlaybackSegment>();

        for (var i = 0; i < cuts.Count - 1; i++)
        {
            var start = cuts[i];
            var end = cuts[i + 1];
            var duration = end - start;
            if (duration >= min && duration <= max)
                results.Add(Segment(start, end, "plugin:commercial-skip:ffmpeg", 0.55));
        }

        return results;
    }

    private static PluginPlaybackSegment Segment(double start, double end, string source, double confidence) => new()
    {
        Kind = "commercial",
        StartSeconds = Math.Max(0, start),
        EndSeconds = Math.Max(start, end),
        Confidence = confidence,
        Source = source,
        IsSkippable = true,
        ReviewStatus = "detected",
    };

    private static bool GetBool(IPluginExecutionContext context, string key, bool fallback)
    {
        if (!context.Settings.TryGetValue(key, out var value)) return fallback;
        return value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => fallback,
        };
    }

    private static double GetDouble(IPluginExecutionContext context, string key, double fallback)
    {
        if (!context.Settings.TryGetValue(key, out var value)) return fallback;
        return value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number when value.TryGetDouble(out var parsed) => parsed,
            System.Text.Json.JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => fallback,
        };
    }
}
