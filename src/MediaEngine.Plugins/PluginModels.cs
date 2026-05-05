namespace MediaEngine.Plugins;

public sealed record PluginMediaAssetContext
{
    public Guid AssetId { get; init; }
    public string FilePath { get; init; } = "";
    public string MediaType { get; init; } = "Unknown";
    public double? DurationSeconds { get; init; }
    public string? Container { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record PluginPlaybackSegment
{
    public string Kind { get; init; } = "";
    public double StartSeconds { get; init; }
    public double? EndSeconds { get; init; }
    public double Confidence { get; init; }
    public string Source { get; init; } = "";
    public bool IsSkippable { get; init; } = true;
    public string ReviewStatus { get; init; } = "detected";
}

public sealed record PluginHealthResult
{
    public string Status { get; init; } = "unknown";
    public string? Message { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record PluginToolResolution
{
    public bool IsAvailable { get; init; }
    public string? ExecutablePath { get; init; }
    public string Status { get; init; } = "missing";
    public string? Message { get; init; }
}

public sealed record PluginToolRunResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
    public bool TimedOut { get; init; }
}

public sealed record PluginAiOptions
{
    public int? MaxTokens { get; init; }
    public string Schedule { get; init; } = "scheduled";
    public string ResourceClass { get; init; } = "ai";
}
