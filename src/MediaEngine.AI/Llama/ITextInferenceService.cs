using MediaEngine.Domain.Enums;

namespace MediaEngine.AI.Llama;

public enum InferenceOutcomeStatus
{
    Success,
    TimedOut,
    Cancelled,
    ModelUnavailable,
    InvalidResponse,
    Failed,
}

/// <summary>Per-request inference controls. Instances are immutable and never alter shared model config.</summary>
public sealed record InferenceRequestOptions(
    double Temperature,
    int MaxTokens,
    TimeSpan Timeout);

public sealed record InferenceOutcome<T>(
    InferenceOutcomeStatus Status,
    T? Value,
    string? Error,
    TimeSpan Duration,
    int Attempts = 1)
{
    public bool IsSuccess => Status == InferenceOutcomeStatus.Success;
}

public interface ITextInferenceService
{
    Task<string> InferAsync(
        AiModelRole role,
        string prompt,
        string? gbnfGrammar = null,
        CancellationToken ct = default);

    Task<T?> InferJsonAsync<T>(
        AiModelRole role,
        string prompt,
        string gbnfGrammar,
        CancellationToken ct = default) where T : class;

    Task<InferenceOutcome<string>> InferWithOutcomeAsync(
        AiModelRole role,
        string prompt,
        string? gbnfGrammar = null,
        InferenceRequestOptions? options = null,
        CancellationToken ct = default) => throw new NotSupportedException();

    Task<InferenceOutcome<T>> InferJsonWithOutcomeAsync<T>(
        AiModelRole role,
        string prompt,
        string gbnfGrammar,
        InferenceRequestOptions? options = null,
        CancellationToken ct = default) where T : class => throw new NotSupportedException();
}
