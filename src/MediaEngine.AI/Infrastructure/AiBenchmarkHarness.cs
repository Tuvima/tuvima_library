using System.Text.Json;
using MediaEngine.AI.Configuration;
using MediaEngine.Domain.Enums;

namespace MediaEngine.AI.Infrastructure;

public sealed class AiBenchmarkHarness
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public IReadOnlyList<AiBenchmarkSuite> GetBuiltInSuites() =>
    [
        new(
            Key: "text_instant",
            Role: AiModelRole.TextFast,
            Gates: new AiBenchmarkGates(
                TargetWarmLatencyMs: 1500,
                MaxWarmLatencyMs: 3000,
                MinJsonValidityRate: 0.99,
                MinTaskPassRate: 0.92,
                MaxHallucinationRate: 0.02,
                MaxWer: null,
                MaxTimestampDriftMs: null),
            Cases:
            [
                JsonCase("intent_search_space_horror", "intent_search", "something tense and scary set in space", "media_type", "movie"),
                JsonCase("tldr_wikipedia_short", "tldr", "Summarize the supplied two-sentence description without adding facts.", "summary"),
                JsonCase("filename_clean_book", "smart_labeling", "Author - Series 02 - Title (2019).epub", "title", "Title"),
            ]),
        new(
            Key: "text_ingestion",
            Role: AiModelRole.TextQuality,
            Gates: new AiBenchmarkGates(
                TargetWarmLatencyMs: 5000,
                MaxWarmLatencyMs: 10000,
                MinJsonValidityRate: 0.99,
                MinTaskPassRate: 0.94,
                MaxHallucinationRate: 0.02,
                MaxWer: null,
                MaxTimestampDriftMs: null),
            Cases:
            [
                JsonCase("qid_disambiguation_retail_bridge", "qid_disambiguation", "Candidates Q1 and Q2; ISBN bridge explicitly matches Q2.", "qid", "Q2"),
                JsonCase("vibe_tags_vocab_only", "vibe_tags", "Allowed: atmospheric, tense, cozy. Return tags for a tense atmospheric description.", "tags"),
                JsonCase("media_type_ambiguous_mp4", "type_logic", "MP4 has video stream, season=2 and episode=3.", "media_type", "tv"),
            ]),
        new(
            Key: "text_enrichment",
            Role: AiModelRole.TextScholar,
            Gates: new AiBenchmarkGates(
                TargetWarmLatencyMs: 10000,
                MaxWarmLatencyMs: 30000,
                MinJsonValidityRate: 0.99,
                MinTaskPassRate: 0.95,
                MaxHallucinationRate: 0.02,
                MaxWer: null,
                MaxTimestampDriftMs: null),
            Cases:
            [
                JsonCase("description_wikipedia_people", "description_intelligence", "Extract only supplied people, themes, mood, setting and summary.", "summary"),
                JsonCase("long_context_series_order", "series_alignment", "Owned entries have explicit ordinals 1, 2.5, and 4; preserve them.", "ordinals"),
                JsonCase("collection_relationships", "relationship_extraction", "A is explicitly stated to be part of B.", "relationships"),
            ]),
        new(
            Key: "text_multilingual",
            Role: AiModelRole.TextCjk,
            Gates: new AiBenchmarkGates(
                TargetWarmLatencyMs: 10000,
                MaxWarmLatencyMs: 30000,
                MinJsonValidityRate: 0.99,
                MinTaskPassRate: 0.93,
                MaxHallucinationRate: 0.03,
                MaxWer: null,
                MaxTimestampDriftMs: null),
            Cases:
            [
                JsonCase("cjk_title_author", "smart_labeling", "title=銀河鉄道の夜; author=宮沢賢治; preserve script.", "title", "銀河鉄道の夜"),
                JsonCase("cjk_description_summary", "description_intelligence", "Preserve canonical name 銀河鉄道の夜 in the summary.", "summary"),
            ]),
        new(
            Key: "audio_sync",
            Role: AiModelRole.Audio,
            Gates: new AiBenchmarkGates(
                TargetWarmLatencyMs: 0,
                MaxWarmLatencyMs: 0,
                MinJsonValidityRate: 0,
                MinTaskPassRate: 0.95,
                MaxHallucinationRate: 0,
                MaxWer: 0.12,
                MaxTimestampDriftMs: 250),
            Cases:
            [
                new("audiobook_chapter_segment", "whisper_alignment", "Known audiobook excerpt with transcript and expected segment boundaries.", false),
                new("subtitle_drift_clip", "subtitle_sync", "Known video clip with shifted subtitle timing.", false),
                new("language_detection_short", "audio_language_detection", "Thirty-second multilingual language detection sample.", false),
            ],
            OperationalRole: "audio_translation"),
        AudioSuite("audio_fast", "audio_fast", 0.93, 0.16, 350,
            new("short_mixed_language", "audio_language_detection", "Short known-language clips with expected labels and timestamps.", false)),
        AudioSuite("audio_english", "audio_english", 0.95, 0.12, 250,
            new("english_conversational", "whisper_alignment", "English conversational fixture with reference transcript and segments.", false)),
        AudioSuite("audio_multilingual", "audio_multilingual", 0.95, 0.12, 250,
            new("multilingual_source_transcript", "whisper_alignment", "Non-English fixture that must remain in the source language.", false)),
        AudioSuite("audio_translation", "audio_translation", 0.95, 0.12, 250,
            new("speech_to_english", "whisper_alignment", "Non-English fixture with an English reference translation.", false)),
        StructuredSuite("embedding_retrieval", "embedding_search", 0.95,
            new("cross_language_retrieval", "intent_search", "Rank known relevant works above distractors across languages.", false)),
        StructuredSuite("function_routing", "function_routing", 0.95,
            new("allowed_tool_and_arguments", "function_routing", "Select only an allowed function and schema-valid arguments.", true)),
        StructuredSuite("multimodal_analysis", "multimodal_analysis", 0.95,
            new("cover_description_grounding", "cover_art_validation", "Describe only details supported by supplied image and text fixtures.", true)),
    ];

    /// <summary>Execution is deny-by-default so ordinary tests never benchmark hardware or load models.</summary>
    public AiBenchmarkExecutionPlan CreateExecutionPlan(
        string suiteKey,
        string catalogKey,
        AiBenchmarkExecutionOptions? options = null)
    {
        options ??= new();
        var suiteExists = GetBuiltInSuites().Any(s => string.Equals(s.Key, suiteKey, StringComparison.OrdinalIgnoreCase));
        var reasons = new List<string>();
        if (!suiteExists) reasons.Add("Unknown benchmark suite.");
        if (string.IsNullOrWhiteSpace(catalogKey)) reasons.Add("A catalog key is required.");
        if (!options.AllowHardwareBenchmark) reasons.Add("Hardware benchmarking requires explicit opt-in.");
        if (!options.AllowModelExecution) reasons.Add("Model execution requires explicit opt-in.");
        return new(suiteKey, catalogKey, reasons.Count == 0, reasons);
    }

    public AiBenchmarkReport EvaluateRun(string suiteKey, string catalogKey, IReadOnlyList<AiBenchmarkObservation> observations)
    {
        var suite = GetBuiltInSuites().SingleOrDefault(s => string.Equals(s.Key, suiteKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown suite '{suiteKey}'.", nameof(suiteKey));
        var expected = suite.Cases.Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var included = observations.Where(o => expected.Contains(o.CaseKey)).ToList();
        var missing = expected.Where(key => included.All(o => !string.Equals(o.CaseKey, key, StringComparison.OrdinalIgnoreCase))).ToList();
        var total = Math.Max(1, included.Count);
        var jsonCases = included.Where(o => suite.Cases.First(c => c.Key.Equals(o.CaseKey, StringComparison.OrdinalIgnoreCase)).RequiresJson).ToList();
        var jsonRate = jsonCases.Count == 0 ? 1d : jsonCases.Count(o => o.JsonValid) / (double)jsonCases.Count;
        var taskRate = included.Count(o => o.TaskPassed) / (double)total;
        var hallucinationRate = included.Count(o => o.HallucinationDetected) / (double)total;
        var worstWer = included.Where(o => o.WordErrorRate.HasValue).Select(o => o.WordErrorRate!.Value).DefaultIfEmpty(0).Max();
        var worstDrift = included.Where(o => o.TimestampDriftMs.HasValue).Select(o => o.TimestampDriftMs!.Value).DefaultIfEmpty(0).Max();
        var worstLatency = included.Select(o => o.ElapsedMilliseconds).DefaultIfEmpty(0).Max();
        var failures = new List<string>();
        if (missing.Count > 0) failures.Add($"Missing observations: {string.Join(", ", missing)}.");
        if (jsonRate < suite.Gates.MinJsonValidityRate) failures.Add("JSON validity gate failed.");
        if (taskRate < suite.Gates.MinTaskPassRate) failures.Add("Task pass-rate gate failed.");
        if (hallucinationRate > suite.Gates.MaxHallucinationRate) failures.Add("Hallucination gate failed.");
        if (suite.Gates.MaxWer is { } maxWer && worstWer > maxWer) failures.Add("Word-error-rate gate failed.");
        if (suite.Gates.MaxTimestampDriftMs is { } maxDrift && worstDrift > maxDrift) failures.Add("Timestamp-drift gate failed.");
        if (suite.Gates.MaxWarmLatencyMs > 0 && worstLatency > suite.Gates.MaxWarmLatencyMs) failures.Add("Warm-latency gate failed.");
        return new(suite.Key, suite.OperationalRole ?? AiModelDefinitions.ToRoleKey(suite.Role), catalogKey,
            DateTimeOffset.UtcNow, failures.Count == 0, jsonRate, taskRate, hallucinationRate, worstWer, worstDrift, worstLatency, missing, failures);
    }

    public string SerializeReport(AiBenchmarkReport report) => JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

    public async Task<AiBenchmarkReport> RunAsync(
        string suiteKey,
        string catalogKey,
        IAiBenchmarkModelRunner runner,
        AiBenchmarkExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runner);
        var plan = CreateExecutionPlan(suiteKey, catalogKey, options);
        if (!plan.CanExecute)
            throw new AiBenchmarkExecutionBlockedException("evaluation_opt_in_required", plan.BlockingReasons);

        var suite = GetBuiltInSuites().SingleOrDefault(s => string.Equals(s.Key, suiteKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown suite '{suiteKey}'.", nameof(suiteKey));
        var observations = new List<AiBenchmarkObservation>(suite.Cases.Count);
        foreach (var testCase in suite.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await runner.ExecuteAsync(
                new(suite.Version, suite.Key, suite.OperationalRole ?? AiModelDefinitions.ToRoleKey(suite.Role), catalogKey,
                    testCase.Key, testCase.Feature, testCase.FixtureInputJson, testCase.AllowedRootProperties), cancellationToken);
            var json = ExtractJson(result.Output);
            var jsonValid = !testCase.RequiresJson || json is not null;
            var assertionPassed = jsonValid && EvaluateAssertions(json, testCase.Assertions);
            var ungrounded = json is not null && ContainsUnexpectedProperties(json, testCase.AllowedRootProperties);
            observations.Add(new(testCase.Key, result.Completed && assertionPassed, jsonValid, ungrounded,
                result.WordErrorRate, result.TimestampDriftMs, result.ElapsedMilliseconds));
        }

        return EvaluateRun(suiteKey, catalogKey, observations);
    }

    public AiBenchmarkEvaluation EvaluateJsonOutput(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
            return new(false, "empty_output");

        if (TryParse(rawOutput))
            return new(true, "valid_json");

        var objectStart = rawOutput.IndexOf('{');
        var objectEnd = rawOutput.LastIndexOf('}');
        if (objectStart >= 0 && objectEnd > objectStart && TryParse(rawOutput[objectStart..(objectEnd + 1)]))
            return new(true, "valid_embedded_object");

        var arrayStart = rawOutput.IndexOf('[');
        var arrayEnd = rawOutput.LastIndexOf(']');
        if (arrayStart >= 0 && arrayEnd > arrayStart && TryParse(rawOutput[arrayStart..(arrayEnd + 1)]))
            return new(true, "valid_embedded_array");

        return new(false, "invalid_json");
    }

    private static AiBenchmarkCase JsonCase(string key, string feature, string input, string property, string? expected = null) =>
        new(key, feature, input, true, JsonSerializer.Serialize(new { input }), [new(property, expected)], [property]);

    private static JsonDocument? ExtractJson(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        foreach (var candidate in new[]
        {
            output,
            Slice(output, '{', '}'),
            Slice(output, '[', ']'),
        }.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            try { return JsonDocument.Parse(candidate!); }
            catch (JsonException) { /* Try the next bounded JSON candidate. */ }
        }
        return null;
    }

    private static string? Slice(string value, char start, char end)
    {
        var first = value.IndexOf(start);
        var last = value.LastIndexOf(end);
        return first >= 0 && last > first ? value[first..(last + 1)] : null;
    }

    private static bool EvaluateAssertions(JsonDocument? document, IReadOnlyList<AiBenchmarkAssertion> assertions)
    {
        if (assertions.Count == 0) return document is not null;
        if (document?.RootElement.ValueKind != JsonValueKind.Object) return false;
        foreach (var assertion in assertions)
        {
            if (!document.RootElement.TryGetProperty(assertion.Property, out var value)) return false;
            if (assertion.ExpectedValue is not null && !string.Equals(value.ToString(), assertion.ExpectedValue, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static bool ContainsUnexpectedProperties(JsonDocument document, IReadOnlyList<string> allowed)
    {
        if (allowed.Count == 0 || document.RootElement.ValueKind != JsonValueKind.Object) return false;
        return document.RootElement.EnumerateObject().Any(property => !allowed.Contains(property.Name, StringComparer.Ordinal));
    }

    private static bool TryParse(string raw)
    {
        try
        {
            using var _ = JsonDocument.Parse(raw, new JsonDocumentOptions
            {
                AllowTrailingCommas = JsonOptions.AllowTrailingCommas,
                CommentHandling = JsonOptions.ReadCommentHandling,
            });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static AiBenchmarkSuite AudioSuite(string key, string role, double passRate, double maxWer, int maxDrift, AiBenchmarkCase testCase) =>
        new(key, AiModelRole.Audio, new(0, 0, 0, passRate, 0, maxWer, maxDrift), [testCase], role);

    private static AiBenchmarkSuite StructuredSuite(string key, string role, double passRate, AiBenchmarkCase testCase) =>
        new(key, AiModelRole.TextQuality, new(0, 0, 0.99, passRate, 0.02, null, null), [testCase], role);
}

public sealed record AiBenchmarkSuite(
    string Key,
    AiModelRole Role,
    AiBenchmarkGates Gates,
    IReadOnlyList<AiBenchmarkCase> Cases,
    string? OperationalRole = null,
    string Version = "v1");

public sealed record AiBenchmarkCase(
    string Key,
    string Feature,
    string FixtureDescription,
    bool RequiresJson,
    string FixtureInputJson = "{}",
    IReadOnlyList<AiBenchmarkAssertion>? ExpectedAssertions = null,
    IReadOnlyList<string>? ExpectedRootProperties = null)
{
    public IReadOnlyList<AiBenchmarkAssertion> Assertions => ExpectedAssertions ?? [];
    public IReadOnlyList<string> AllowedRootProperties => ExpectedRootProperties ?? [];
}

public sealed record AiBenchmarkAssertion(string Property, string? ExpectedValue = null);

public sealed record AiBenchmarkGates(
    int TargetWarmLatencyMs,
    int MaxWarmLatencyMs,
    double MinJsonValidityRate,
    double MinTaskPassRate,
    double MaxHallucinationRate,
    double? MaxWer,
    int? MaxTimestampDriftMs);

public sealed record AiBenchmarkEvaluation(bool Passed, string Reason);

public sealed record AiBenchmarkExecutionOptions(bool AllowHardwareBenchmark = false, bool AllowModelExecution = false);

public sealed record AiBenchmarkExecutionPlan(
    string SuiteKey,
    string CatalogKey,
    bool CanExecute,
    IReadOnlyList<string> BlockingReasons);

public sealed record AiBenchmarkObservation(
    string CaseKey,
    bool TaskPassed,
    bool JsonValid = true,
    bool HallucinationDetected = false,
    double? WordErrorRate = null,
    int? TimestampDriftMs = null,
    int ElapsedMilliseconds = 0);

public sealed record AiBenchmarkReport(
    string SuiteKey,
    string Role,
    string CatalogKey,
    DateTimeOffset EvaluatedAt,
    bool Passed,
    double JsonValidityRate,
    double TaskPassRate,
    double HallucinationRate,
    double WorstWordErrorRate,
    int WorstTimestampDriftMs,
    int WorstLatencyMs,
    IReadOnlyList<string> MissingCases,
    IReadOnlyList<string> Failures);

public interface IAiBenchmarkModelRunner
{
    Task<AiBenchmarkModelResult> ExecuteAsync(AiBenchmarkExecutionRequest request, CancellationToken cancellationToken);
}

public sealed record AiBenchmarkExecutionRequest(
    string FixtureVersion,
    string SuiteKey,
    string Role,
    string CatalogKey,
    string CaseKey,
    string Feature,
    string InputJson,
    IReadOnlyList<string> AllowedRootProperties);

public sealed record AiBenchmarkModelResult(
    string Output,
    bool Completed,
    int ElapsedMilliseconds,
    double? WordErrorRate = null,
    int? TimestampDriftMs = null);

public sealed class AiBenchmarkExecutionBlockedException : InvalidOperationException
{
    public AiBenchmarkExecutionBlockedException(string code, IReadOnlyList<string> blockingReasons)
        : base(string.Join(" ", blockingReasons))
    {
        Code = code;
        BlockingReasons = blockingReasons;
    }

    public string Code { get; }
    public IReadOnlyList<string> BlockingReasons { get; }
}
