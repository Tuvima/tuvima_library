using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using MediaEngine.Api.Endpoints;
using MediaEngine.Api.Models;
using MediaEngine.Api.Services.Display;
using MediaEngine.Api.Services.Playback;
using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Contracts.Collections;
using SeriesManifestViewDto = MediaEngine.Domain.Models.SeriesManifestViewDto;
using SeriesManifestItemDto = MediaEngine.Domain.Models.SeriesManifestItemDto;
using MediaEngine.Contracts.Details;
using MediaEngine.Contracts.Persons;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;
using static MediaEngine.Api.Services.Details.Internals.DetailPresentationPolicy;

namespace MediaEngine.Api.Services.Details.Internals;

internal sealed partial class DetailCompositionOrchestrator
{
    private async Task<List<SequenceItemViewModel>> MergeLegacySequenceMemberPlaceholdersAsync(
        IReadOnlyList<SequenceItemViewModel> items,
        string seriesQid,
        DetailEntityType entityType,
        CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var rawMembers = await conn.QueryAsync(new CommandDefinition(
            """
            SELECT work_qid AS WorkQid,
                   work_label AS WorkLabel,
                   position AS Position
            FROM series_members
            WHERE series_qid = @seriesQid
            ORDER BY CAST(position AS REAL), work_label;
            """,
            new { seriesQid },
            cancellationToken: ct));

        var merged = items.ToList();
        var ownedPositions = BuildOwnedPositionSet(merged);

        foreach (var member in rawMembers)
        {
            var positionSort = TryParseSeriesPositionSort(StringValue(member.Position));
            var position = ToDisplayPositionNumber(positionSort);
            var positionKey = SequencePositionKey(positionSort);
            if (string.IsNullOrWhiteSpace(positionKey) || ownedPositions.Contains(positionKey))
            {
                continue;
            }

            merged.Add(new SequenceItemViewModel
            {
                Id = $"missing-{seriesQid}-{positionKey}",
                EntityType = entityType,
                Title = StringHelpers.FirstNonBlankOr(string.Empty, StringValue(member.WorkLabel), $"Book {FormatSequenceSort(positionSort)}"),
                PositionNumber = position,
                PositionSort = positionSort,
                PositionLabel = FormatSequenceSort(positionSort),
                IsOwned = false,
                ProgressState = LibraryProgressState.Unknown,
            });
        }

        return merged
            .OrderBy(item => item.PositionSort ?? item.PositionNumber ?? double.MaxValue)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<SequenceItemViewModel> AddMissingSequencePlaceholders(
        IReadOnlyList<SequenceItemViewModel> items,
        DetailEntityType entityType)
    {
        var numbered = items
            .Where(item => item.PositionNumber.HasValue && item.PositionNumber.Value > 0)
            .GroupBy(item => item.PositionNumber!.Value)
            .ToDictionary(group => group.Key, group => group.First());
        var unnumbered = items
            .Where(item => !item.PositionNumber.HasValue || item.PositionNumber.Value <= 0)
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (numbered.Count == 0)
        {
            return unnumbered;
        }

        var max = numbered.Keys.Max();
        var filled = new List<SequenceItemViewModel>(max);
        for (var position = 1; position <= max; position++)
        {
            if (numbered.TryGetValue(position, out var existing))
            {
                filled.Add(existing);
                continue;
            }

            filled.Add(new SequenceItemViewModel
            {
                Id = $"missing-{position}",
                EntityType = entityType,
                Title = "Missing from library",
                PositionNumber = position,
                PositionSort = position,
                PositionLabel = position.ToString(),
                IsOwned = false,
                ProgressState = LibraryProgressState.Unknown,
            });
        }

        filled.AddRange(unnumbered);
        return filled;
    }

    private static List<SequenceItemViewModel> SortSequenceItems(IEnumerable<SequenceItemViewModel> items)
        => items
            .OrderBy(item => SequenceScopeSort(item.MembershipScope))
            .ThenBy(item => TryParseSeriesPosition(item.GroupKey?.Replace("season-", string.Empty, StringComparison.OrdinalIgnoreCase)) ?? int.MaxValue)
            .ThenBy(item => item.PositionSort ?? item.PositionNumber ?? double.MaxValue)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static int SequenceScopeSort(string? membershipScope)
        => membershipScope switch
        {
            SeriesMembershipScopeNames.Supplementary => 1,
            SeriesMembershipScopeNames.CollectedContent => 2,
            SeriesMembershipScopeNames.BroaderContext => 3,
            SeriesMembershipScopeNames.Unpositioned => 1,
            _ => 0,
        };

    private static string? NormalizeSeriesTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            .ToArray());

        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? NormalizeSequenceContainerTitleForOptionMatch(string? value)
    {
        var normalized = NormalizeSeriesTitle(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 1 && string.Equals(words[0], "the", StringComparison.OrdinalIgnoreCase))
        {
            words.RemoveAt(0);
        }

        while (words.Count > 1 && IsGenericSequenceContainerWord(words[^1]))
        {
            words.RemoveAt(words.Count - 1);
        }

        return words.Count == 0 ? normalized : string.Join(' ', words);
    }

    private static bool IsGenericSequenceContainerWord(string value)
        => string.Equals(value, "collection", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "series", StringComparison.OrdinalIgnoreCase);

}
