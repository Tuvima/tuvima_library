namespace MediaEngine.Api.Tests;

public sealed class Phase7AiEndpointGuardrailTests
{
    [Fact]
    public void AiEndpoints_ParseEveryConfiguredRoleAndRequireAdmin()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Api\Endpoints\AiEndpoints.cs");
        var roleSource = ReadRepoFile(@"src\MediaEngine.Domain\Enums\AiModelRole.cs");

        Assert.Contains("TryParseModelRole", source, StringComparison.Ordinal);
        Assert.Contains("AiModelDefinitions.ToRoleKey", source, StringComparison.Ordinal);
        Assert.Contains("Enum.GetValues<AiModelRole>().Select(ToRoleKey)", source, StringComparison.Ordinal);
        Assert.Contains("TextFast", roleSource, StringComparison.Ordinal);
        Assert.Contains("TextQuality", roleSource, StringComparison.Ordinal);
        Assert.Contains("TextScholar", roleSource, StringComparison.Ordinal);
        Assert.Contains("TextCjk", roleSource, StringComparison.Ordinal);
        Assert.Contains("Audio", roleSource, StringComparison.Ordinal);
        Assert.True(CountOccurrences(source, ".RequireAdmin()") >= 10);
    }

    [Fact]
    public void AiConfigSave_ValidatesBeforePersisting()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Api\Endpoints\AiEndpoints.cs");

        Assert.Contains("ValidateAiSettings", source, StringComparison.Ordinal);
        Assert.Contains("ValidationProblem", source, StringComparison.Ordinal);
        Assert.Contains("Idle unload seconds must be positive", source, StringComparison.Ordinal);
        Assert.Contains("Download URL must be an absolute URI", source, StringComparison.Ordinal);
        Assert.Contains("Catalog key must reference model_catalog", source, StringComparison.Ordinal);
        Assert.Contains("Requirement references an unknown model catalog key", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AiModelStatus_ExposesCatalogSelectionAndBenchmarkSuites()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Api\Endpoints\AiEndpoints.cs");

        Assert.Contains("GetAiBenchmarkSuites", source, StringComparison.Ordinal);
        Assert.Contains("SelectionRationale", source, StringComparison.Ordinal);
        Assert.Contains("ValidationWarnings", source, StringComparison.Ordinal);
        Assert.Contains("Capabilities", source, StringComparison.Ordinal);
        Assert.Contains("DiskStatus", source, StringComparison.Ordinal);
        Assert.Contains("MemoryEnvelopeMB", source, StringComparison.Ordinal);
        Assert.Contains("ChecksumStatus", source, StringComparison.Ordinal);
        Assert.Contains("ModelInventory inventory", source, StringComparison.Ordinal);
        Assert.Contains("inventory.GetModelPath(status.Role)", source, StringComparison.Ordinal);
        Assert.Equal(0, CountOccurrences(source, "configLoader.LoadAi<AiSettings>() ?? new AiSettings()"));
        Assert.DoesNotContain("?? new AiSettings()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AiOperations_ReturnTypedProblemsWithoutRawExceptionDetails()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Api\Endpoints\AiEndpoints.cs");

        Assert.Contains("unknown-model-role", source, StringComparison.Ordinal);
        Assert.Contains("AiBenchmarkRuntimeUnavailableException", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ex.Message}", source, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string pattern) =>
        value.Split(pattern, StringSplitOptions.None).Length - 1;

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            relativePath)));
}
