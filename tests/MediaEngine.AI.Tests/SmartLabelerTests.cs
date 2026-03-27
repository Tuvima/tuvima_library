using MediaEngine.AI.Configuration;
using MediaEngine.AI.Features;
using MediaEngine.AI.Llama;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.AI.Tests;

/// <summary>
/// Unit tests for <see cref="SmartLabeler"/> — validates the business logic that
/// wraps the LLM response: year validation, confidence clamping, fallback behaviour,
/// TV season/episode validation, and whitespace trimming.
/// The LLM itself is mocked via <see cref="StubLlamaInferenceService"/> so no model
/// weights are required at test time.
/// </summary>
public sealed class SmartLabelerTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static SmartLabeler Build(StubLlamaInferenceService stub) =>
        new(stub, new AiSettings(), NullLogger<SmartLabeler>.Instance);

    // ── Valid result ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CleanAsync_ValidResult_ReturnsCleanedQuery()
    {
        var stub = StubLlamaInferenceService.ReturningJson(
            """{"title":"Dune","author":"Frank Herbert","year":1965,"season":null,"episode":null,"confidence":0.95}""");
        var labeler = Build(stub);

        var result = await labeler.CleanAsync("Dune.1965.FrankHerbert.epub");

        Assert.Equal("Dune", result.Title);
        Assert.Equal("Frank Herbert", result.Author);
        Assert.Equal(1965, result.Year);
        Assert.Equal(0.95, result.Confidence);
    }

    // ── Null LLM result falls back ─────────────────────────────────────────

    [Fact]
    public async Task CleanAsync_NullLlmResult_ReturnsFallback()
    {
        var stub = StubLlamaInferenceService.ReturningNull();
        var labeler = Build(stub);

        var result = await labeler.CleanAsync("some.weird.filename.epub");

        // Fallback: stem becomes title, confidence 0.1
        Assert.Equal("some.weird.filename", result.Title);
        Assert.Equal(0.1, result.Confidence);
        Assert.Null(result.Author);
    }

    // ── Year out of range is rejected ─────────────────────────────────────

    [Fact]
    public async Task CleanAsync_YearOutOfRange_Rejected()
    {
        var stub = StubLlamaInferenceService.ReturningJson(
            """{"title":"Old Book","author":null,"year":1200,"season":null,"episode":null,"confidence":0.8}""");
        var labeler = Build(stub);

        var result = await labeler.CleanAsync("OldBook.epub");

        Assert.Null(result.Year);
        Assert.Equal("Old Book", result.Title);
    }

    // ── Year within range is accepted ─────────────────────────────────────

    [Fact]
    public async Task CleanAsync_YearInRange_Accepted()
    {
        var stub = StubLlamaInferenceService.ReturningJson(
            """{"title":"Modern Book","author":null,"year":2020,"season":null,"episode":null,"confidence":0.9}""");
        var labeler = Build(stub);

        var result = await labeler.CleanAsync("ModernBook2020.epub");

        Assert.Equal(2020, result.Year);
    }

    // ── Confidence clamped above 1.0 ──────────────────────────────────────

    [Fact]
    public async Task CleanAsync_ConfidenceClamped_Above1()
    {
        var stub = StubLlamaInferenceService.ReturningJson(
            """{"title":"Dune","author":null,"year":null,"season":null,"episode":null,"confidence":1.5}""");
        var labeler = Build(stub);

        var result = await labeler.CleanAsync("Dune.epub");

        Assert.Equal(1.0, result.Confidence);
    }

    // ── Confidence clamped below 0.0 ──────────────────────────────────────

    [Fact]
    public async Task CleanAsync_ConfidenceClamped_BelowZero()
    {
        var stub = StubLlamaInferenceService.ReturningJson(
            """{"title":"Dune","author":null,"year":null,"season":null,"episode":null,"confidence":-0.5}""");
        var labeler = Build(stub);

        var result = await labeler.CleanAsync("Dune.epub");

        Assert.Equal(0.0, result.Confidence);
    }

    // ── TV metadata — valid season/episode accepted ───────────────────────

    [Fact]
    public async Task CleanAsync_TvMetadata_SeasonEpisodeValidated()
    {
        var stub = StubLlamaInferenceService.ReturningJson(
            """{"title":"Breaking Bad","author":null,"year":2008,"season":5,"episode":12,"confidence":0.9}""");
        var labeler = Build(stub);

        var result = await labeler.CleanAsync("Breaking.Bad.S05E12.mkv");

        Assert.Equal(5, result.Season);
        Assert.Equal(12, result.Episode);
    }

    // ── TV metadata — season too high is rejected ─────────────────────────

    [Fact]
    public async Task CleanAsync_TvMetadata_SeasonTooHigh_Rejected()
    {
        var stub = StubLlamaInferenceService.ReturningJson(
            """{"title":"Some Show","author":null,"year":null,"season":200,"episode":1,"confidence":0.7}""");
        var labeler = Build(stub);

        var result = await labeler.CleanAsync("some.show.s200e01.mkv");

        Assert.Null(result.Season);
        // Episode 1 is within range so it should be accepted
        Assert.Equal(1, result.Episode);
    }

    // ── Empty filename returns fallback ────────────────────────────────────

    [Fact]
    public async Task CleanAsync_EmptyFilename_ReturnsFallback()
    {
        var stub = StubLlamaInferenceService.ReturningNull();
        var labeler = Build(stub);

        var result = await labeler.CleanAsync(string.Empty);

        Assert.Equal(string.Empty, result.Title);
        Assert.Equal(0, result.Confidence);
    }

    // ── Author whitespace is trimmed ──────────────────────────────────────

    [Fact]
    public async Task CleanAsync_AuthorTrimmed()
    {
        var stub = StubLlamaInferenceService.ReturningJson(
            """{"title":"Dune","author":"  Frank Herbert  ","year":null,"season":null,"episode":null,"confidence":0.9}""");
        var labeler = Build(stub);

        var result = await labeler.CleanAsync("Dune.epub");

        Assert.Equal("Frank Herbert", result.Author);
    }

    // ── Empty author string becomes null ──────────────────────────────────

    [Fact]
    public async Task CleanAsync_EmptyAuthor_BecomesNull()
    {
        var stub = StubLlamaInferenceService.ReturningJson(
            """{"title":"Dune","author":"","year":null,"season":null,"episode":null,"confidence":0.9}""");
        var labeler = Build(stub);

        var result = await labeler.CleanAsync("Dune.epub");

        Assert.Null(result.Author);
    }
}

// ── Stub ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Test double for <see cref="ILlamaInferenceService"/>.
/// Returns a pre-configured JSON string (or null) without loading any model weights.
/// </summary>
internal sealed class StubLlamaInferenceService : ILlamaInferenceService
{
    private readonly string? _jsonResponse;

    private StubLlamaInferenceService(string? jsonResponse) =>
        _jsonResponse = jsonResponse;

    public static StubLlamaInferenceService ReturningJson(string json) =>
        new(json);

    public static StubLlamaInferenceService ReturningNull() =>
        new(null);

    public Task<string> InferAsync(
        AiModelRole role,
        string prompt,
        string? gbnfGrammar = null,
        CancellationToken ct = default) =>
        Task.FromResult(_jsonResponse ?? string.Empty);

    public Task<T?> InferJsonAsync<T>(
        AiModelRole role,
        string prompt,
        string gbnfGrammar,
        CancellationToken ct = default) where T : class
    {
        if (_jsonResponse is null)
            return Task.FromResult<T?>(null);

        try
        {
            var result = System.Text.Json.JsonSerializer.Deserialize<T>(
                _jsonResponse,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
            return Task.FromResult(result);
        }
        catch
        {
            return Task.FromResult<T?>(null);
        }
    }
}
