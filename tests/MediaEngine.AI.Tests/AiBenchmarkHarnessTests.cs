using MediaEngine.AI.Configuration;
using MediaEngine.AI.Infrastructure;
using MediaEngine.AI.Llama;
using MediaEngine.Domain.Enums;

namespace MediaEngine.AI.Tests;

public sealed class AiBenchmarkHarnessTests
{
    [Fact]
    public async Task TextSuite_ExecutesVersionedFixturesAndDerivesPassingReport()
    {
        var harness = new AiBenchmarkHarness();
        var runner = new AssertionEchoRunner();

        var report = await harness.RunAsync(
            "text_instant",
            "qwen3_0_6b_q8",
            runner,
            new(AllowHardwareBenchmark: true, AllowModelExecution: true));

        Assert.True(report.Passed);
        Assert.Equal(1, report.JsonValidityRate);
        Assert.Equal(1, report.TaskPassRate);
        Assert.Empty(report.MissingCases);
        Assert.All(runner.Requests, request => Assert.Equal("v1", request.FixtureVersion));
    }

    [Fact]
    public async Task TextSuite_RefusesImplicitModelExecution()
    {
        var harness = new AiBenchmarkHarness();

        var error = await Assert.ThrowsAsync<AiBenchmarkExecutionBlockedException>(() => harness.RunAsync(
            "text_instant", "qwen3_0_6b_q8", new AssertionEchoRunner(), new()));

        Assert.Equal("evaluation_opt_in_required", error.Code);
        Assert.Contains(error.BlockingReasons, reason => reason.Contains("explicit opt-in", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LocalRunner_RejectsUnsupportedRuntimeWithTypedBlocker()
    {
        var runner = new LocalTextBenchmarkModelRunner(new FakeInference(), new AiSettings());
        var request = new AiBenchmarkExecutionRequest("v1", "audio_fast", "audio_fast", "whisper_small",
            "case", "audio", "{}", []);

        var error = await Assert.ThrowsAsync<AiBenchmarkRuntimeUnavailableException>(() => runner.ExecuteAsync(request, default));

        Assert.Equal("unsupported_benchmark_runtime", error.Code);
        Assert.Equal("audio_fast", error.Role);
    }

    [Fact]
    public async Task LocalRunner_PromptContainsSchemaButNotEvaluatorOracle()
    {
        var inference = new FakeInference();
        var runner = new LocalTextBenchmarkModelRunner(inference, new AiSettings());
        var request = new AiBenchmarkExecutionRequest("v1", "text_instant", "text_fast", "qwen3_0_6b_q8",
            "oracle", "qid_disambiguation", "{\"input\":\"choose from supplied evidence\"}", ["qid"]);

        await runner.ExecuteAsync(request, default);

        Assert.Contains("qid", inference.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Q2", inference.Prompt, StringComparison.Ordinal);
    }

    private sealed class AssertionEchoRunner : IAiBenchmarkModelRunner
    {
        public List<AiBenchmarkExecutionRequest> Requests { get; } = [];

        public Task<AiBenchmarkModelResult> ExecuteAsync(AiBenchmarkExecutionRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var property = Assert.Single(request.AllowedRootProperties);
            var value = request.CaseKey switch
            {
                "intent_search_space_horror" => "movie",
                "filename_clean_book" => "Title",
                _ => "value",
            };
            return Task.FromResult(new AiBenchmarkModelResult($"{{\"{property}\":\"{value}\"}}", true, 10));
        }
    }

    private sealed class FakeInference : ITextInferenceService
    {
        public string Prompt { get; private set; } = "";

        public Task<string> InferAsync(AiModelRole role, string prompt, string? gbnfGrammar = null, CancellationToken ct = default)
        {
            Prompt = prompt;
            return Task.FromResult("{}");
        }

        public Task<T?> InferJsonAsync<T>(AiModelRole role, string prompt, string gbnfGrammar, CancellationToken ct = default) where T : class =>
            Task.FromResult<T?>(null);
    }
}
