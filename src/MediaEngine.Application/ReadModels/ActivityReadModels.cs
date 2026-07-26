namespace MediaEngine.Application.ReadModels;

public sealed record ActivityBatchQuery(
    string? Search,
    string? MediaType,
    string? Status,
    string? Source,
    string? EventType,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    int Offset,
    int Limit,
    string? Sort = null,
    string? SortDirection = null);
