namespace MediaEngine.Domain.Models;

/// <summary>Provider evidence for a single, identified episode. Show cast is never episode evidence.</summary>
public sealed record TvEpisodeCredits(Guid WorkId, Guid ShowWorkId, string ProviderEpisodeId,
    int Season, int Episode, IReadOnlyList<TvPersonCredit> Credits);

public sealed record TvPersonCredit(string PersonId, string CreditId, string Name, string Job,
    string? Character, string? ProfileUrl, int Order);
