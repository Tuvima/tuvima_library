namespace MediaEngine.Api.Tests;

public sealed class RateLimitingGuardrailTests
{
    [Fact]
    public void Program_RunsRateLimiterBeforeApiKeyAuthentication()
    {
        var source = Read(@"src\MediaEngine.Api\Program.cs");

        var rateLimiterIndex = source.IndexOf("app.UseRateLimiter();", StringComparison.Ordinal);
        var apiKeyMiddlewareIndex = source.IndexOf("app.UseMiddleware<ApiKeyMiddleware>();", StringComparison.Ordinal);

        Assert.True(rateLimiterIndex >= 0, "app.UseRateLimiter() was not found in Program.cs.");
        Assert.True(apiKeyMiddlewareIndex >= 0, "app.UseMiddleware<ApiKeyMiddleware>() was not found in Program.cs.");
        Assert.True(
            rateLimiterIndex < apiKeyMiddlewareIndex,
            "app.UseRateLimiter() must run BEFORE app.UseMiddleware<ApiKeyMiddleware>() in the request pipeline. " +
            "ApiKeyMiddleware performs a database lookup for every X-Api-Key header, so an unauthenticated flood " +
            "must be throttled by the rate limiter first, or every request in the flood reaches the DB lookup " +
            "before any limiter can reject it.");
    }

    [Fact]
    public void Program_RegistersAGlobalRateLimiterDefault()
    {
        var source = Read(@"src\MediaEngine.Api\Program.cs");

        var addRateLimiterIndex = source.IndexOf("AddRateLimiter(options =>", StringComparison.Ordinal);
        Assert.True(addRateLimiterIndex >= 0, "builder.Services.AddRateLimiter(options => ...) was not found in Program.cs.");

        Assert.Contains(
            "options.GlobalLimiter",
            source,
            StringComparison.Ordinal);

        var globalLimiterIndex = source.IndexOf("options.GlobalLimiter", StringComparison.Ordinal);
        Assert.True(
            globalLimiterIndex > addRateLimiterIndex,
            "options.GlobalLimiter must be assigned inside the AddRateLimiter configuration block. Without a " +
            "GlobalLimiter, every route that does not explicitly opt into a named policy (key_generation, " +
            "streaming, general) receives no rate limiting at all — do not remove this default.");
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath)));
}
