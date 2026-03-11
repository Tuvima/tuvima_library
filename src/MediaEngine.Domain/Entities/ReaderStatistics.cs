namespace MediaEngine.Domain.Entities;

/// <summary>
/// Aggregated reading statistics for a user and a specific EPUB asset.
/// One row per (user_id, asset_id) pair — upserted on every auto-save.
/// </summary>
public sealed class ReaderStatistics
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid AssetId { get; set; }

    /// <summary>Number of distinct chapters the user has read (any amount).</summary>
    public int ChaptersRead { get; set; }

    /// <summary>Total reading time in seconds across all sessions.</summary>
    public long TotalReadingTimeSecs { get; set; }

    /// <summary>Estimated total words read (proportional to pages viewed).</summary>
    public long WordsRead { get; set; }

    /// <summary>Number of reading sessions started.</summary>
    public int SessionsCount { get; set; }

    /// <summary>Rolling average words per minute across all sessions.</summary>
    public double AvgWordsPerMinute { get; set; }

    /// <summary>Timestamp of the most recent reading session.</summary>
    public DateTime? LastSessionAt { get; set; }
}
