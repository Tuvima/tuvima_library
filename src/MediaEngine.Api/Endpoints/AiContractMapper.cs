using System.Text.Json;
using MediaEngine.AI.Configuration;
using MediaEngine.AI.Infrastructure;
using MediaEngine.Contracts.Ai;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;

namespace MediaEngine.Api.Endpoints;

internal static class AiContractMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static AiConfigDto ToContract(AiSettings settings) =>
        JsonSerializer.Deserialize<AiConfigDto>(
            JsonSerializer.Serialize(settings, JsonOptions),
            JsonOptions)
        ?? throw new InvalidOperationException("The AI configuration could not be mapped to its wire contract.");

    internal static AiSettings ToSettings(AiConfigDto contract) =>
        JsonSerializer.Deserialize<AiSettings>(
            JsonSerializer.Serialize(contract, JsonOptions),
            JsonOptions)
        ?? throw new InvalidOperationException("The AI wire contract could not be mapped to configuration.");

    internal static HardwareProfileDto ToContract(HardwareProfile profile) => new()
    {
        Tier = profile.Tier,
        Backend = profile.Backend,
        GpuName = profile.GpuName,
        TokensPerSecond = profile.TokensPerSecond,
        AvailableRamMb = profile.AvailableRamMb,
        BenchmarkedAt = profile.BenchmarkedAt,
    };

    internal static AiBenchmarkSuiteDto ToContract(AiBenchmarkSuite suite) => new()
    {
        Key = suite.Key,
        Role = suite.OperationalRole ?? AiModelDefinitions.ToRoleKey(suite.Role),
        Gates = new AiBenchmarkGatesDto
        {
            TargetWarmLatencyMs = suite.Gates.TargetWarmLatencyMs,
            MaxWarmLatencyMs = suite.Gates.MaxWarmLatencyMs,
            MinJsonValidityRate = suite.Gates.MinJsonValidityRate,
            MinTaskPassRate = suite.Gates.MinTaskPassRate,
            MaxHallucinationRate = suite.Gates.MaxHallucinationRate,
            MaxWer = suite.Gates.MaxWer,
            MaxTimestampDriftMs = suite.Gates.MaxTimestampDriftMs,
        },
        Cases = suite.Cases.Select(testCase => new AiBenchmarkCaseDto
        {
            Key = testCase.Key,
            Feature = testCase.Feature,
            FixtureDescription = testCase.FixtureDescription,
            RequiresJson = testCase.RequiresJson,
            FixtureInputJson = testCase.FixtureInputJson,
            ExpectedAssertions = testCase.ExpectedAssertions?
                .Select(ToContract)
                .ToList(),
            ExpectedRootProperties = testCase.ExpectedRootProperties?.ToList(),
            Assertions = testCase.Assertions.Select(ToContract).ToList(),
            AllowedRootProperties = testCase.AllowedRootProperties.ToList(),
        }).ToList(),
    };

    internal static AiBenchmarkReportDto ToContract(AiBenchmarkReport report) => new()
    {
        SuiteKey = report.SuiteKey,
        Role = report.Role,
        CatalogKey = report.CatalogKey,
        EvaluatedAt = report.EvaluatedAt,
        Passed = report.Passed,
        JsonValidityRate = report.JsonValidityRate,
        TaskPassRate = report.TaskPassRate,
        HallucinationRate = report.HallucinationRate,
        WorstWordErrorRate = report.WorstWordErrorRate,
        WorstTimestampDriftMs = report.WorstTimestampDriftMs,
        WorstLatencyMs = report.WorstLatencyMs,
        MissingCases = report.MissingCases.ToList(),
        Failures = report.Failures.ToList(),
    };

    internal static IntentSearchResponse ToContract(IntentSearchResult result) => new()
    {
        Genres = result.Genres,
        Moods = result.Moods,
        YearFrom = result.YearFrom,
        YearTo = result.YearTo,
        MediaTypes = result.MediaTypes,
        Keywords = result.Keywords,
        Confidence = result.Confidence,
        OriginalQuery = result.OriginalQuery,
    };

    internal static UrlExtractionResponse ToContract(UrlExtractionResult result) => new()
    {
        Success = result.Success,
        Fields = result.Fields,
        Confidence = result.Confidence,
        ErrorMessage = result.ErrorMessage,
    };

    private static AiBenchmarkAssertionDto ToContract(AiBenchmarkAssertion assertion) => new()
    {
        Property = assertion.Property,
        ExpectedValue = assertion.ExpectedValue,
    };
}
