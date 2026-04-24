namespace MediaEngine.Api.Endpoints;

internal static class ApiImageUrls
{
    public static string? BuildPersonHeadshotUrl(
        Guid personId,
        string? localHeadshotPath,
        string? remoteHeadshotUrl)
        => !string.IsNullOrWhiteSpace(localHeadshotPath) || !string.IsNullOrWhiteSpace(remoteHeadshotUrl)
            ? $"/persons/{personId}/headshot"
            : null;

    public static string? BuildCharacterPortraitUrl(
        Guid? portraitId,
        string? localPortraitPath,
        string? remotePortraitUrl)
        => portraitId.HasValue
           && (!string.IsNullOrWhiteSpace(localPortraitPath) || !string.IsNullOrWhiteSpace(remotePortraitUrl))
            ? $"/library/portraits/{portraitId.Value}"
            : null;
}
