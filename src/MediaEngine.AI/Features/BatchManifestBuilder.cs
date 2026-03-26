using MediaEngine.AI.Configuration;
using MediaEngine.AI.Llama;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Features;

/// <summary>
/// Analyzes a batch of incoming files together and produces an Ingestion Manifest.
/// Groups files by series/album/work and specifies targeted retail queries.
/// Uses the text_quality model.
/// Files are processed in chunks of <see cref="MaxChunkSize"/> to avoid
/// LLM prompt timeout on CPU inference.
/// </summary>
public sealed class BatchManifestBuilder : IBatchManifestBuilder
{
    private const int MaxChunkSize = 8;

    private readonly LlamaInferenceService _llama;
    private readonly AiSettings _settings;
    private readonly ILogger<BatchManifestBuilder> _logger;

    public BatchManifestBuilder(
        LlamaInferenceService llama,
        AiSettings settings,
        ILogger<BatchManifestBuilder> logger)
    {
        _llama = llama;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IngestionManifest> AnalyzeAsync(
        IReadOnlyList<BatchFileInput> files,
        CancellationToken ct = default)
    {
        if (files.Count == 0)
            return new IngestionManifest { Groups = [], ProcessingTimeMs = 0 };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var allGroups = new List<ManifestGroup>();

        // Chunk files to avoid LLM prompt timeout on CPU inference.
        // 8 files per chunk keeps inference under 10 seconds.
        var chunks = files
            .Select((f, i) => (File: f, Index: i))
            .GroupBy(x => x.Index / MaxChunkSize)
            .Select(g => g.Select(x => x.File).ToList())
            .ToList();

        _logger.LogInformation(
            "BatchManifestBuilder: analyzing {Files} files in {Chunks} chunk(s)",
            files.Count, chunks.Count);

        foreach (var chunk in chunks)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var groups = await AnalyzeChunkAsync(chunk, ct);
                allGroups.AddRange(groups);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "BatchManifestBuilder: chunk of {Count} files failed — using fallback",
                    chunk.Count);
                // Fallback: one group per file in this chunk.
                allGroups.AddRange(chunk.Select(f => new ManifestGroup
                {
                    GroupType = "single_work",
                    MediaType = MediaType.Unknown,
                    Confidence = 0.1,
                    Files = [new ManifestFile
                    {
                        FilePath = f.FilePath,
                        Title = Path.GetFileNameWithoutExtension(f.FilePath),
                        Confidence = 0.1,
                    }],
                }));
            }
        }

        sw.Stop();

        _logger.LogInformation(
            "BatchManifestBuilder: {Files} files → {Groups} groups in {Ms}ms",
            files.Count, allGroups.Count, sw.ElapsedMilliseconds);

        return new IngestionManifest
        {
            Groups = allGroups,
            ProcessingTimeMs = sw.ElapsedMilliseconds,
        };
    }

    private async Task<List<ManifestGroup>> AnalyzeChunkAsync(
        List<BatchFileInput> chunk,
        CancellationToken ct)
    {
        var promptFiles = chunk.Select(f => (
            Path: f.FilePath,
            Extension: f.Extension,
            SizeBytes: f.FileSizeBytes,
            Container: f.Container,
            Duration: f.DurationSeconds,
            Metadata: f.ExtractedMetadata
        )).ToList();

        var prompt = PromptTemplates.BatchManifestPrompt(promptFiles);

        var result = await _llama.InferJsonAsync<ManifestResponse>(
            AiModelRole.TextQuality,
            prompt,
            PromptTemplates.BatchManifestGrammar,
            ct);

        if (result?.Groups is null || result.Groups.Count == 0)
        {
            _logger.LogWarning("BatchManifestBuilder chunk returned empty for {Count} files", chunk.Count);
            return chunk.Select(f => new ManifestGroup
            {
                GroupType = "single_work",
                MediaType = MediaType.Unknown,
                Confidence = 0.1,
                Files = [new ManifestFile
                {
                    FilePath = f.FilePath,
                    Title = Path.GetFileNameWithoutExtension(f.FilePath),
                    Confidence = 0.1,
                }],
            }).ToList();
        }

        return result.Groups.Select(g => new ManifestGroup
        {
            GroupType = g.GroupType ?? "single_work",
            MediaType = ParseMediaType(g.MediaType),
            Confidence = Math.Clamp(g.Confidence, 0.0, 1.0),
            SeriesTitle = g.SeriesTitle,
            Year = g.Year,
            Creator = g.Creator,
            HardIdentifier = g.HardIdentifier,
            HardIdentifierType = g.HardIdentifierType,
            RetailProvider = g.RetailProvider,
            RetailQuery = g.RetailQuery,
            Files = (g.Files ?? []).Select(f => new ManifestFile
            {
                FilePath = f.FilePath ?? "",
                Title = f.Title ?? Path.GetFileNameWithoutExtension(f.FilePath ?? ""),
                Confidence = Math.Clamp(f.Confidence, 0.0, 1.0),
                EpisodeNumber = f.EpisodeNumber,
                TrackNumber = f.TrackNumber,
                EpisodeTitle = f.EpisodeTitle,
            }).ToList(),
        }).ToList();
    }

    private static MediaType ParseMediaType(string? type) => (type?.Trim().ToLowerInvariant()) switch
    {
        "book" or "books" => MediaType.Books,
        "audiobook" or "audiobooks" => MediaType.Audiobooks,
        "movie" or "movies" => MediaType.Movies,
        "tv" => MediaType.TV,
        "music" => MediaType.Music,
        "comic" or "comics" => MediaType.Comics,
        "podcast" or "podcasts" => MediaType.Podcasts,
        _ => MediaType.Unknown,
    };

    // ── Internal DTOs matching the GBNF grammar output ───────────────────

    private sealed class ManifestResponse
    {
        public List<ManifestGroupDto>? Groups { get; set; }
    }

    private sealed class ManifestGroupDto
    {
        public string? GroupType { get; set; }
        public string? MediaType { get; set; }
        public double Confidence { get; set; }
        public string? SeriesTitle { get; set; }
        public int? Year { get; set; }
        public string? Creator { get; set; }
        public string? HardIdentifier { get; set; }
        public string? HardIdentifierType { get; set; }
        public string? RetailProvider { get; set; }
        public string? RetailQuery { get; set; }
        public List<ManifestFileDto>? Files { get; set; }
    }

    private sealed class ManifestFileDto
    {
        public string? FilePath { get; set; }
        public string? Title { get; set; }
        public double Confidence { get; set; }
        public int? EpisodeNumber { get; set; }
        public int? TrackNumber { get; set; }
        public string? EpisodeTitle { get; set; }
    }
}
