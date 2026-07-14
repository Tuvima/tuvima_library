namespace MediaEngine.Domain.Constants;

/// <summary>
/// Persisted values for <c>works.wikidata_status</c>. These are strings at the
/// storage/API boundary, so constants preserve the current wire format while
/// preventing new spelling variants in application code.
/// </summary>
public static class WorkWikidataStatus
{
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Missing = "missing";
    public const string Manual = "manual";
    public const string ProviderOnly = "provider_only";
    public const string AutoAligned = "auto_aligned";
    public const string UserConfirmed = "user_confirmed";
    public const string UserReplaced = "user_replaced";
    public const string UserRejected = "user_rejected";
}

public static class WorkWikidataMatchSource
{
    public const string Retail = "retail";
    public const string User = "user";
}

public static class WorkIdentityMatchLevel
{
    public const string Work = "work";
    public const string Edition = "edition";
    public const string RetailOnly = "retail_only";
}
