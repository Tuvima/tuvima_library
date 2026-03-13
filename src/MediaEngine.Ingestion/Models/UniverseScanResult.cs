namespace MediaEngine.Ingestion.Models;

/// <summary>
/// Summary of universe graph recovery during a Great Inhale scan of
/// <c>.universe/*/universe.xml</c> sidecar files.
/// </summary>
public sealed class UniverseScanResult
{
    /// <summary>Number of narrative root records upserted.</summary>
    public int UniversesUpserted { get; init; }

    /// <summary>Number of fictional entity records upserted.</summary>
    public int EntitiesUpserted { get; init; }

    /// <summary>Number of relationship edges upserted.</summary>
    public int RelationshipsUpserted { get; init; }

    /// <summary>Number of universe.xml files that could not be parsed.</summary>
    public int Errors { get; init; }
}
