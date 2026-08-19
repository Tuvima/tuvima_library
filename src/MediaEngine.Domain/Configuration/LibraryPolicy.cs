namespace MediaEngine.Domain.Configuration;

/// <summary>
/// Stable values used by <c>libraries.json</c> to distinguish product areas
/// without coupling the ingestion pipeline to dashboard routes.
/// </summary>
public static class LibraryKinds
{
    public const string Catalogued = "catalogued";
    public const string Personal = "personal";
    public const string Photos = "photos";

    public static bool IsValid(string? value) => value is Catalogued or Personal or Photos;
}

/// <summary>
/// Controls how far an ingested file may progress into external identity and
/// metadata providers. Local extraction always runs so files remain browsable.
/// </summary>
public static class LibraryMetadataPolicies
{
    public const string Enriched = "enriched";
    public const string LocalPreferred = "local_preferred";
    public const string LocalOnly = "local_only";
    public const string Manual = "manual";

    public static bool IsValid(string? value) =>
        value is Enriched or LocalPreferred or LocalOnly or Manual;

    public static bool BypassesExternalIdentity(string? value) =>
        value is LocalOnly or Manual;
}
