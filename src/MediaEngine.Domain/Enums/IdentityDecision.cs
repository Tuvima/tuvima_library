namespace MediaEngine.Domain.Enums;

/// <summary>
/// The outcome of an identity evaluation by the decision service.
/// Workers act on this verdict without checking thresholds themselves.
/// </summary>
public enum IdentityDecision
{
    /// <summary>Auto-accept, proceed to organization.</summary>
    Accept,

    /// <summary>Accept with lower match confidence (text-resolved, ambiguous retail).</summary>
    ProvisionalAccept,

    /// <summary>Route to Action Center with root cause.</summary>
    Review,

    /// <summary>Transient failure, re-queue for later attempt.</summary>
    RetryLater,

    /// <summary>Permanently reject candidate.</summary>
    Reject
}
