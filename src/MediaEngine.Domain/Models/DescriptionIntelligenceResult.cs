namespace MediaEngine.Domain.Models;

/// <summary>
/// Structured output from the Description Intelligence LLM pass.
/// </summary>
public sealed record DescriptionIntelligenceResult
{
    /// <summary>People mentioned in descriptions with their roles.</summary>
    public IReadOnlyList<ExtractedPersonRef> People { get; init; } = [];

    /// <summary>3-5 key themes (e.g. "survival", "colonialism", "ecology").</summary>
    public IReadOnlyList<string> Themes { get; init; } = [];

    /// <summary>2-3 mood tags from controlled vocabulary.</summary>
    public IReadOnlyList<string> Mood { get; init; } = [];

    /// <summary>Primary setting location (null if unclear).</summary>
    public string? Setting { get; init; }

    /// <summary>When the story takes place (null if unclear).</summary>
    public string? TimePeriod { get; init; }

    /// <summary>Target audience: adult, young-adult, children, all-ages.</summary>
    public string? Audience { get; init; }

    /// <summary>Content warnings (empty if none).</summary>
    public IReadOnlyList<string> ContentWarnings { get; init; } = [];

    /// <summary>Story pacing: slow-burn, fast-paced, moderate, varied.</summary>
    public string? Pace { get; init; }

    /// <summary>One punchy sentence summary, no spoilers.</summary>
    public string? Tldr { get; init; }
}

/// <summary>
/// A person reference extracted from a description by the LLM.
/// </summary>
public sealed record ExtractedPersonRef(string Name, string Role, double Confidence);
