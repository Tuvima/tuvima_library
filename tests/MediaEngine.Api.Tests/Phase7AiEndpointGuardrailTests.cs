namespace MediaEngine.Api.Tests;

public sealed class Phase7AiEndpointGuardrailTests
{
    [Fact]
    public void AiEndpoints_ParseEveryConfiguredRoleAndRequireAdmin()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Api\Endpoints\AiEndpoints.cs");

        Assert.Contains("TryParseModelRole", source, StringComparison.Ordinal);
        Assert.Contains("text_fast", source, StringComparison.Ordinal);
        Assert.Contains("text_quality", source, StringComparison.Ordinal);
        Assert.Contains("text_scholar", source, StringComparison.Ordinal);
        Assert.Contains("text_cjk", source, StringComparison.Ordinal);
        Assert.Contains("audio", source, StringComparison.Ordinal);
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
