namespace MediaEngine.Domain.Enums;

/// <summary>
/// Classifies why a provider request failed, so the system can
/// decide whether to retry automatically or escalate to the user.
/// </summary>
public enum ProviderFailureType
{
    /// <summary>No failure — request succeeded.</summary>
    None = 0,

    /// <summary>
    /// Provider is unreachable (timeout, connection refused, DNS failure, 5xx).
    /// Items are queued as "Waiting for Provider" and retried automatically.
    /// </summary>
    ProviderDown = 1,

    /// <summary>
    /// Provider responded but returned zero results for the search.
    /// Item goes to "Needs Review" — the provider worked, it just couldn't find it.
    /// </summary>
    NoMatch = 2,

    /// <summary>
    /// Provider returned multiple equally-scored candidates.
    /// Item goes to "Needs Review" — user picks the right match.
    /// </summary>
    Ambiguous = 3,

    /// <summary>
    /// Provider returned 429 Too Many Requests.
    /// Item re-queued with short delay and retried in minutes.
    /// </summary>
    RateLimited = 4,
}
