using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>EPUB book metadata for the reader.</summary>
public sealed record EpubBookMetadataDto(
    string Title,
    string Author,
    int ChapterCount,
    long WordCount,
    string? Language,
    bool HasCoverImage);

/// <summary>Table of contents entry (recursive).</summary>
public sealed class EpubTocEntryDto
{
    public required string Title { get; init; }
    public required int ChapterIndex { get; init; }
    public string? FragmentId { get; init; }
    public List<EpubTocEntryDto> Children { get; init; } = [];
}

/// <summary>Chapter HTML content with word count.</summary>
public sealed record EpubChapterContentDto(
    int Index,
    string Title,
    string HtmlContent,
    int WordCount);

/// <summary>Search result within an EPUB book.</summary>
public sealed record EpubSearchHitDto(
    int ChapterIndex,
    string ChapterTitle,
    string ContextSnippet,
    int MatchOffset);

/// <summary>Reader bookmark for display.</summary>
public sealed class ReaderBookmarkDto
{
    public Guid Id { get; init; }
    public Guid AssetId { get; init; }
    public int ChapterIndex { get; init; }
    public string? CfiPosition { get; init; }
    public string? Label { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>Reader highlight for display.</summary>
public sealed class ReaderHighlightDto
{
    public Guid Id { get; init; }
    public Guid AssetId { get; init; }
    public int ChapterIndex { get; init; }
    public int StartOffset { get; init; }
    public int EndOffset { get; init; }
    public string SelectedText { get; init; } = string.Empty;
    public string Color { get; init; } = "#EAB308";
    public string? NoteText { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>Reader statistics for display.</summary>
public sealed class ReaderStatisticsDto
{
    public Guid AssetId { get; init; }
    public int ChaptersRead { get; init; }
    public long TotalReadingTimeSecs { get; init; }
    public long WordsRead { get; init; }
    public int SessionsCount { get; init; }
    public double AvgWordsPerMinute { get; init; }
    public DateTime? LastSessionAt { get; init; }
}

/// <summary>Request body for updating reading statistics.</summary>
public sealed record ReaderStatisticsUpdateDto(
    int ChaptersRead,
    long TotalReadingTimeSecs,
    long WordsRead,
    int SessionsCount,
    double AvgWordsPerMinute);

/// <summary>Progress state from the Engine (GET /progress/{assetId}).</summary>
public sealed class ProgressStateDto
{
    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("asset_id")]
    public Guid AssetId { get; init; }

    [JsonPropertyName("progress_pct")]
    public double ProgressPct { get; init; }

    [JsonPropertyName("last_accessed")]
    public DateTime? LastAccessed { get; init; }

    [JsonPropertyName("extended_properties")]
    public Dictionary<string, string> ExtendedProperties { get; init; } = new();
}

/// <summary>Reader settings stored in localStorage (per-device).</summary>
public sealed record ReaderSettingsDto
{
    public string FontFamily { get; set; } = "Merriweather";
    public int FontSize { get; set; } = 18;
    public double LineHeight { get; set; } = 1.8;
    public int Margins { get; set; } = 48;
    public string Theme { get; set; } = "dark";
}
