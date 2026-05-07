namespace MediaEngine.Api.Tests;

public sealed class ProblemDetailsGuardrailTests
{
    [Fact]
    public void Program_RegistersStructuredSafeExceptionHandling()
    {
        var source = Read(@"src\MediaEngine.Api\Program.cs");

        Assert.Contains("builder.Services.AddProblemDetails", source, StringComparison.Ordinal);
        Assert.Contains("app.UseExceptionHandler", source, StringComparison.Ordinal);
        Assert.Contains("application/problem+json", source, StringComparison.Ordinal);
        Assert.Contains("traceId", source, StringComparison.Ordinal);
        Assert.Contains("The request failed. Check Engine logs with the trace id for details.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", source, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath)));
}
