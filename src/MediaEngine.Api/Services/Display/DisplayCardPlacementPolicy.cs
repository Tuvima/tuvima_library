using MediaEngine.Contracts.Display;

namespace MediaEngine.Api.Services.Display;

/// <summary>
/// Applies cross-shelf placement rules using structural media identity. Display text is
/// deliberately excluded so copy changes and similarly named works cannot affect placement.
/// </summary>
internal static class DisplayCardPlacementPolicy
{
    public static HashSet<DisplayCardIdentity> CollectIdentities(
        params IEnumerable<DisplayCardDto>[] cardSets)
    {
        var identities = new HashSet<DisplayCardIdentity>();
        foreach (var cards in cardSets)
        {
            foreach (var card in cards)
            {
                identities.Add(IdentityFor(card));
            }
        }

        return identities;
    }

    public static IReadOnlyList<DisplayCardDto> TakeUnplaced(
        IEnumerable<DisplayCardDto> candidates,
        ISet<DisplayCardIdentity> occupied,
        int limit)
    {
        var result = new List<DisplayCardDto>(Math.Max(0, limit));
        foreach (var card in candidates)
        {
            var identity = IdentityFor(card);
            if (!occupied.Add(identity))
            {
                continue;
            }

            result.Add(card);
            if (result.Count >= limit)
            {
                break;
            }
        }

        return result;
    }

    internal static DisplayCardIdentity IdentityFor(DisplayCardDto card)
    {
        if (card.Flags.IsCollection || !string.Equals(card.GroupingType, "work", StringComparison.OrdinalIgnoreCase))
        {
            return new DisplayCardIdentity($"group:{card.GroupingType}", card.CollectionId ?? card.Id);
        }

        if (card.WorkId is { } workId && workId != Guid.Empty)
        {
            return new DisplayCardIdentity("work", workId);
        }

        if (card.AssetId is { } assetId && assetId != Guid.Empty)
        {
            return new DisplayCardIdentity("asset", assetId);
        }

        return new DisplayCardIdentity("card", card.Id);
    }
}

internal readonly record struct DisplayCardIdentity(string Kind, Guid Id);
