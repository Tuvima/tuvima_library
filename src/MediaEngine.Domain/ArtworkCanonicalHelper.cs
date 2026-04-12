using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain;

/// <summary>
/// Helper methods for persisting artwork-truth canonicals.
/// </summary>
public static class ArtworkCanonicalHelper
{
    public static List<CanonicalValue> CreateFlags(
        Guid entityId,
        string coverState,
        string? coverSource,
        string heroState,
        DateTimeOffset lastScoredAt,
        bool settled)
    {
        var values = new List<CanonicalValue>
        {
            Create(entityId, MetadataFieldConstants.CoverState, coverState, lastScoredAt),
            Create(entityId, MetadataFieldConstants.HeroState, heroState, lastScoredAt),
        };

        if (!string.IsNullOrWhiteSpace(coverSource))
            values.Add(Create(entityId, MetadataFieldConstants.CoverSource, coverSource, lastScoredAt));

        if (settled)
            values.Add(Create(entityId, MetadataFieldConstants.ArtworkSettledAt, lastScoredAt.ToString("o"), lastScoredAt));

        return values;
    }

    public static CanonicalValue Create(Guid entityId, string key, string value, DateTimeOffset lastScoredAt) => new()
    {
        EntityId = entityId,
        Key = key,
        Value = value,
        LastScoredAt = lastScoredAt,
    };
}
