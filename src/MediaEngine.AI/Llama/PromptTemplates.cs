namespace MediaEngine.AI.Llama;

/// <summary>
/// System prompts and GBNF grammars for all AI-powered features.
/// Grammars are model-agnostic — they constrain token generation via llama.cpp,
/// working identically with Llama, Mistral, Phi, Gemma, Qwen, etc.
/// </summary>
public static class PromptTemplates
{
    // ── Smart Labeling (Feature 1) ────────────────────────────────────────

    public const string SmartLabelingSystem = """
        You extract clean metadata from media filenames. Rules:
        - Do NOT remove years that are part of the title (e.g. "2001 A Space Odyssey")
        - Extract author names if present (e.g. "Frank Herbert - Dune")
        - Remove quality tags, release groups, encoding info (BluRay, x264, FLUX, etc.)
        - Detect TV season/episode patterns (S01E02, 1x03)
        - Return ONLY valid JSON matching the grammar
        """;

    public const string SmartLabelingGrammar = """
        root   ::= "{" ws "\"title\"" ws ":" ws string ws title-rest "}"
        title-rest ::= "," ws "\"author\"" ws ":" ws (string | "null") ws "," ws "\"year\"" ws ":" ws (number | "null") ws "," ws "\"season\"" ws ":" ws (number | "null") ws "," ws "\"episode\"" ws ":" ws (number | "null") ws "," ws "\"confidence\"" ws ":" ws number
        string ::= "\"" ([^"\\] | "\\" .)* "\""
        number ::= [0-9]+ ("." [0-9]+)?
        ws     ::= [ \t\n]*
        """;

    public static string SmartLabelingPrompt(string filename) =>
        $"{SmartLabelingSystem}\n\nFilename: {filename}";

    // ── Media Type Classification (Feature 2) ──────────────────────────────

    public const string MediaTypeSystem = """
        You classify media files into exactly one type based on their metadata.
        Types: Book, Audiobook, Movie, TV, Music, Comic, Podcast
        Consider ALL signals: filename, duration, container format, genre, folder path, bitrate, chapters.
        Return ONLY valid JSON matching the grammar.
        """;

    public const string MediaTypeGrammar = """
        root   ::= "{" ws "\"type\"" ws ":" ws type-value ws "," ws "\"confidence\"" ws ":" ws number ws "," ws "\"reason\"" ws ":" ws string ws "}"
        type-value ::= "\"Book\"" | "\"Audiobook\"" | "\"Movie\"" | "\"TV\"" | "\"Music\"" | "\"Comic\"" | "\"Podcast\""
        string ::= "\"" ([^"\\] | "\\" .)* "\""
        number ::= [0-9]+ ("." [0-9]+)?
        ws     ::= [ \t\n]*
        """;

    public static string MediaTypePrompt(
        string filename,
        string? container,
        double? durationSec,
        int? bitrate,
        string? genre,
        bool hasChapters,
        string? folderPath) =>
        $"""
        {MediaTypeSystem}

        Filename: {filename}
        Container: {container ?? "unknown"}
        Duration: {(durationSec.HasValue ? $"{durationSec:F0} seconds" : "unknown")}
        Bitrate: {(bitrate.HasValue ? $"{bitrate} kbps" : "unknown")}
        Genre tag: {genre ?? "none"}
        Has chapters: {(hasChapters ? "yes" : "no")}
        Folder path: {folderPath ?? "unknown"}
        """;

    // ── Batch Manifest (Feature 3) ────────────────────────────────────────

    public const string BatchManifestSystem = """
        You analyze batches of media files and group them logically.
        For each group, identify: group type (tv_season, album, book_series, single_work),
        media type (Book, Audiobook, Movie, TV, Music, Comic, Podcast),
        series/album title, year, creator, and which retail provider to query.

        Provider routing rules:
        - Books/Audiobooks → apple_api
        - Movies/TV → tmdb
        - Music → musicbrainz
        - Comics → comic_vine
        - Podcasts → apple_podcasts

        If you find hard identifiers (ISBN, ASIN) in the metadata, note them.
        Return ONLY valid JSON matching the grammar.
        """;

    // Batch manifest grammar is more complex — we use a simplified version
    // that allows flexible arrays. The full schema validation happens in code.
    public const string BatchManifestGrammar = """
        root       ::= "{" ws "\"groups\"" ws ":" ws groups ws "}"
        groups     ::= "[" ws (group ("," ws group)*)? ws "]"
        group      ::= "{" ws group-fields ws "}"
        group-fields ::= "\"group_type\"" ws ":" ws string ws "," ws "\"media_type\"" ws ":" ws string ws "," ws "\"confidence\"" ws ":" ws number ws "," ws "\"series_title\"" ws ":" ws (string | "null") ws "," ws "\"year\"" ws ":" ws (number | "null") ws "," ws "\"creator\"" ws ":" ws (string | "null") ws "," ws "\"hard_identifier\"" ws ":" ws (string | "null") ws "," ws "\"hard_identifier_type\"" ws ":" ws (string | "null") ws "," ws "\"retail_provider\"" ws ":" ws (string | "null") ws "," ws "\"retail_query\"" ws ":" ws (string | "null") ws "," ws "\"files\"" ws ":" ws files
        files      ::= "[" ws (file ("," ws file)*)? ws "]"
        file       ::= "{" ws "\"file_path\"" ws ":" ws string ws "," ws "\"title\"" ws ":" ws string ws "," ws "\"confidence\"" ws ":" ws number ws "," ws "\"episode_number\"" ws ":" ws (number | "null") ws "," ws "\"track_number\"" ws ":" ws (number | "null") ws "," ws "\"episode_title\"" ws ":" ws (string | "null") ws "}"
        string     ::= "\"" ([^"\\] | "\\" .)* "\""
        number     ::= [0-9]+ ("." [0-9]+)?
        ws         ::= [ \t\n]*
        """;

    public static string BatchManifestPrompt(IReadOnlyList<(string Path, string Extension, long SizeBytes, string? Container, double? Duration, IReadOnlyDictionary<string, string> Metadata)> files)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(BatchManifestSystem);
        sb.AppendLine();
        sb.AppendLine($"Files ({files.Count} total):");
        sb.AppendLine();

        foreach (var (path, ext, size, container, duration, metadata) in files)
        {
            var filename = System.IO.Path.GetFileName(path);
            sb.AppendLine($"- {filename} (ext: {ext}, size: {size / 1024}KB, container: {container ?? "?"}, duration: {(duration.HasValue ? $"{duration:F0}s" : "?")})");

            if (metadata.Count > 0)
            {
                foreach (var (key, value) in metadata)
                {
                    sb.AppendLine($"  {key}: {value}");
                }
            }
        }

        return sb.ToString();
    }

    // ── QID Disambiguation (Feature 5) ────────────────────────────────────

    public const string QidDisambiguationSystem = """
        You are selecting the correct Wikidata entity from multiple candidates.
        Compare the file's metadata against each candidate's description.
        Consider: title similarity, year match, author/creator match, format/type match.
        Select the best match. If no candidate is clearly correct, set selected_qid to null.
        Return ONLY valid JSON matching the grammar.
        """;

    public const string QidDisambiguationGrammar = """
        root   ::= "{" ws "\"selected_qid\"" ws ":" ws (string | "null") ws "," ws "\"confidence\"" ws ":" ws number ws "," ws "\"reasoning\"" ws ":" ws string ws "}"
        string ::= "\"" ([^"\\] | "\\" .)* "\""
        number ::= [0-9]+ ("." [0-9]+)?
        ws     ::= [ \t\n]*
        """;

    public static string QidDisambiguationPrompt(
        IReadOnlyDictionary<string, string> fileMetadata,
        IReadOnlyList<Domain.Models.QidCandidate> candidates)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(QidDisambiguationSystem);
        sb.AppendLine();
        sb.AppendLine("File metadata:");
        foreach (var (key, value) in fileMetadata)
            sb.AppendLine($"  {key}: {value}");
        sb.AppendLine();
        sb.AppendLine("Wikidata candidates:");
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            sb.AppendLine($"  {i + 1}. QID: {c.Qid} — {c.Label}");
            if (!string.IsNullOrWhiteSpace(c.Description))
                sb.AppendLine($"     Description: {c.Description}");
        }
        return sb.ToString();
    }

    // ── Series Position (Feature 6) ───────────────────────────────────────

    public const string SeriesPositionSystem = """
        You determine a work's position within a series.
        Given the work title, series name, and sibling titles already in the series,
        determine what position number this work should have (1-based).
        If you cannot determine the position, set position to null.
        Return ONLY valid JSON matching the grammar.
        """;

    public const string SeriesPositionGrammar = """
        root   ::= "{" ws "\"position\"" ws ":" ws (number | "null") ws "," ws "\"confidence\"" ws ":" ws number ws "}"
        number ::= [0-9]+
        ws     ::= [ \t\n]*
        """;

    public static string SeriesPositionPrompt(
        string workTitle,
        string seriesName,
        IReadOnlyList<string> siblingTitles)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(SeriesPositionSystem);
        sb.AppendLine();
        sb.AppendLine($"Series: {seriesName}");
        sb.AppendLine($"Work to position: {workTitle}");
        if (siblingTitles.Count > 0)
        {
            sb.AppendLine("Existing titles in series:");
            for (int i = 0; i < siblingTitles.Count; i++)
                sb.AppendLine($"  {i + 1}. {siblingTitles[i]}");
        }
        return sb.ToString();
    }
}
