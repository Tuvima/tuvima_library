namespace MediaEngine.Ingestion.Models;

/// <summary>
/// Summary of a Great Inhale library scan triggered via
/// <c>POST /ingestion/library-scan</c>.
/// </summary>
public sealed class LibraryScanResult
{
    /// <summary>Number of Hub records created or updated in the database.</summary>
    public int HubsUpserted { get; init; }

    /// <summary>Number of Edition/MediaAsset records created or updated.</summary>
    public int EditionsUpserted { get; init; }

    /// <summary>Number of sidecar files that could not be parsed or hydrated.</summary>
    public int Errors { get; init; }

    /// <summary>Wall-clock time taken for the full scan.</summary>
    public TimeSpan Elapsed { get; init; }
}
