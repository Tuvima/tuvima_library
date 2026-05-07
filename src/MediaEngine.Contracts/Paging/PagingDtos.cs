using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Paging;

public sealed record PagedRequest(
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("limit")] int Limit)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 250;

    public static PagedRequest From(int? offset, int? limit, int defaultLimit = DefaultLimit, int maxLimit = MaxLimit)
    {
        var safeOffset = Math.Max(0, offset ?? 0);
        var safeDefault = Math.Clamp(defaultLimit, 1, maxLimit);
        var safeLimit = Math.Clamp(limit ?? safeDefault, 1, maxLimit);
        return new PagedRequest(safeOffset, safeLimit);
    }
}

public sealed record PagedResponse<T>(
    [property: JsonPropertyName("items")] IReadOnlyList<T> Items,
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("has_more")] bool HasMore,
    [property: JsonPropertyName("total_count")] int? TotalCount = null,
    [property: JsonPropertyName("next_cursor")] string? NextCursor = null)
{
    public static PagedResponse<T> FromPage(IReadOnlyList<T> pagePlusOne, PagedRequest request, int? totalCount = null)
    {
        var hasMore = pagePlusOne.Count > request.Limit;
        var items = hasMore
            ? pagePlusOne.Take(request.Limit).ToList()
            : pagePlusOne;
        var nextOffset = request.Offset + items.Count;
        return new PagedResponse<T>(
            items,
            request.Offset,
            request.Limit,
            hasMore,
            totalCount,
            hasMore ? nextOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
    }
}
