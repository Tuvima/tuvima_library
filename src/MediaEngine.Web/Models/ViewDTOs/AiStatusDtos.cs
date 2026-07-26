namespace MediaEngine.Web.Models.ViewDTOs;

public sealed class AiProblemDetailsDto
{
    public string Type { get; init; } = "about:blank";
    public string Title { get; init; } = "AI operation failed";
    public int? Status { get; init; }
    public string Detail { get; init; } = "The Engine could not complete the operation.";
    public List<string> BlockingReasons { get; init; } = [];

    public string ToUserMessage()
    {
        var parts = new List<string> { Title };
        if (!string.IsNullOrWhiteSpace(Detail) && !string.Equals(Detail, Title, StringComparison.Ordinal))
            parts.Add(Detail);
        parts.AddRange(BlockingReasons.Where(reason => !string.IsNullOrWhiteSpace(reason)));
        return string.Join(" ", parts.Distinct(StringComparer.Ordinal));
    }
}

public sealed record AiOperationResultDto(bool Succeeded, AiProblemDetailsDto? Problem = null)
{
    public static AiOperationResultDto Success() => new(true);
    public static AiOperationResultDto Failure(AiProblemDetailsDto problem) => new(false, problem);
}

public sealed record AiOperationResultDto<T>(bool Succeeded, T? Value = default, AiProblemDetailsDto? Problem = null)
{
    public static AiOperationResultDto<T> Success(T value) => new(true, value);
    public static AiOperationResultDto<T> Failure(AiProblemDetailsDto problem) => new(false, default, problem);
}
