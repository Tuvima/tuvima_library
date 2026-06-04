using System.Text.Json;
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
                new("intent_search_space_horror", "intent_search", "something tense and scary set in space", true),
                new("tldr_wikipedia_short", "tldr", "Summarize a Wikipedia-backed work description in two sentences.", true),
                new("filename_clean_book", "smart_labeling", "Author - Series 02 - Title (2019).epub", true),
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
                new("qid_disambiguation_retail_bridge", "qid_disambiguation", "Choose the correct Wikidata candidate from retail bridge IDs and descriptions.", true),
                new("vibe_tags_vocab_only", "vibe_tags", "Return 3-5 allowed vibe tags from the configured vocabulary.", true),
                new("media_type_ambiguous_mp4", "type_logic", "Classify an ambiguous MP4 with audiobook-like metadata.", true),
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
                new("description_wikipedia_people", "description_intelligence", "Extract people, themes, mood, setting, and TLDR from Wikipedia-backed descriptions.", true),
                new("long_context_series_order", "series_alignment", "Infer order from long mixed provider descriptions without inventing missing works.", true),
                new("collection_relationships", "relationship_extraction", "Extract only relationships supported by supplied descriptions.", true),
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
                new("cjk_title_author", "smart_labeling", "Parse CJK title and contributor metadata without romanizing unless requested.", true),
                new("cjk_description_summary", "description_intelligence", "Summarize a non-English description while preserving canonical names.", true),
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
            ]),
    ];

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
}

public sealed record AiBenchmarkSuite(
    string Key,
    AiModelRole Role,
    AiBenchmarkGates Gates,
    IReadOnlyList<AiBenchmarkCase> Cases);

public sealed record AiBenchmarkCase(
    string Key,
    string Feature,
    string FixtureDescription,
    bool RequiresJson);

public sealed record AiBenchmarkGates(
    int TargetWarmLatencyMs,
    int MaxWarmLatencyMs,
    double MinJsonValidityRate,
    double MinTaskPassRate,
    double MaxHallucinationRate,
    double? MaxWer,
    int? MaxTimestampDriftMs);

public sealed record AiBenchmarkEvaluation(bool Passed, string Reason);
