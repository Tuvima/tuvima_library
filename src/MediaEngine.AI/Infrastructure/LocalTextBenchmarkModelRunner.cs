using System.Diagnostics;
using System.Text;
using MediaEngine.AI.Configuration;
using MediaEngine.AI.Llama;
using MediaEngine.Domain.Enums;

namespace MediaEngine.AI.Infrastructure;

/// <summary>Executes only the four currently integrated text roles.</summary>
public sealed class LocalTextBenchmarkModelRunner : IAiBenchmarkModelRunner
{
    private readonly ITextInferenceService _inference;
    private readonly AiSettings _settings;

    public LocalTextBenchmarkModelRunner(ITextInferenceService inference, AiSettings settings)
    {
        _inference = inference;
        _settings = settings;
    }

    public async Task<AiBenchmarkModelResult> ExecuteAsync(
        AiBenchmarkExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryMapTextRole(request.Role, out var role))
        {
            throw new AiBenchmarkRuntimeUnavailableException(
                "unsupported_benchmark_runtime",
                request.Role,
                "Only integrated LLamaSharp text roles can execute through this runner. Audio, embedding, function, and multimodal roles require dedicated runners.");
        }

        var configuredCatalogKey = _settings.Models.GetByRole(role).CatalogKey;
        if (!string.Equals(configuredCatalogKey, request.CatalogKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new AiBenchmarkRuntimeUnavailableException(
                "catalog_role_mismatch",
                request.Role,
                "The requested catalog model is not the executable model configured for this role.");
        }

        var prompt = BuildPrompt(request);
        var stopwatch = Stopwatch.StartNew();
        var output = await _inference.InferAsync(role, prompt, ct: cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        return new(output, !string.IsNullOrWhiteSpace(output), checked((int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue)));
    }

    private static string BuildPrompt(AiBenchmarkExecutionRequest request)
    {
        var properties = request.AllowedRootProperties.Count == 0
            ? "Return one JSON value."
            : $"Return exactly one JSON object with only these root properties: {string.Join(", ", request.AllowedRootProperties)}.";
        var builder = new StringBuilder()
            .AppendLine("You are running a deterministic Tuvima evaluation fixture. Do not add facts not present in INPUT.")
            .AppendLine(properties)
            .AppendLine($"FEATURE: {request.Feature}")
            .AppendLine("INPUT:")
            .Append(request.InputJson);
        return builder.ToString();
    }

    private static bool TryMapTextRole(string value, out AiModelRole role)
    {
        role = value.ToLowerInvariant() switch
        {
            "text_fast" => AiModelRole.TextFast,
            "text_quality" => AiModelRole.TextQuality,
            "text_scholar" => AiModelRole.TextScholar,
            "text_cjk" => AiModelRole.TextCjk,
            _ => default,
        };
        return value.StartsWith("text_", StringComparison.OrdinalIgnoreCase)
            && value.ToLowerInvariant() is "text_fast" or "text_quality" or "text_scholar" or "text_cjk";
    }
}

public sealed class AiBenchmarkRuntimeUnavailableException : InvalidOperationException
{
    public AiBenchmarkRuntimeUnavailableException(string code, string role, string detail) : base(detail)
    {
        Code = code;
        Role = role;
    }

    public string Code { get; }
    public string Role { get; }
}
