namespace MediaEngine.Web.Models.ViewDTOs;

public sealed record DevHarnessRunResult(
    bool Succeeded,
    int StatusCode,
    string Endpoint,
    string? ContentType,
    string Body,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    long ElapsedMs)
{
    public bool IsJson =>
        ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true
        || Body.TrimStart().StartsWith('{')
        || Body.TrimStart().StartsWith('[');

    public bool IsHtml =>
        ContentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true
        || Body.Contains("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase);
}
