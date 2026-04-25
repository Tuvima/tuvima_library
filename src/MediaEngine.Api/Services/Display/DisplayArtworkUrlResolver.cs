namespace MediaEngine.Api.Services.Display;

internal static class DisplayArtworkUrlResolver
{
    public static string? Resolve(string? value, Guid assetId, string kind, string? state)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return string.Equals(state, "present", StringComparison.OrdinalIgnoreCase)
            ? $"/stream/{assetId}/{kind}"
            : null;
    }
}
