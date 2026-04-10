namespace MediaEngine.Ingestion.Services;

/// <summary>
/// Classifies a tagger exception so the auto re-tag sweep can decide between
/// "schedule a retry in the next off-hours window" (transient) and "route to
/// the Action Center as <c>WritebackFailed</c>" (terminal).
/// </summary>
public static class RetagFailureClassifier
{
    /// <summary>
    /// Classification outcome. <see cref="Locked"/> and <see cref="IoFailed"/>
    /// are transient — the worker schedules a retry. <see cref="Corrupt"/> and
    /// <see cref="Unknown"/> are terminal — the worker routes to review.
    /// </summary>
    public enum Outcome
    {
        Locked,
        IoFailed,
        Corrupt,
        Unknown,
    }

    /// <summary>
    /// Inspects an exception thrown by an <see cref="MediaEngine.Ingestion.Contracts.IMetadataTagger"/>
    /// implementation and returns the appropriate routing outcome.
    /// </summary>
    public static Outcome Classify(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        // TagLib throws ArgumentException with "Not-a-Number" when an MP4
        // contains a NaN duration field — the file is structurally broken.
        if (ex is ArgumentException argEx && argEx.Message.Contains("Not-a-Number", StringComparison.OrdinalIgnoreCase))
            return Outcome.Corrupt;

        // The OS reports a sharing violation while a player or sync app holds
        // the file open. Transient — try again in the next off-hours window.
        if (ex is IOException io)
        {
            var msg = io.Message ?? string.Empty;
            if (msg.Contains("being used by another process", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("locked", StringComparison.OrdinalIgnoreCase))
                return Outcome.Locked;

            return Outcome.IoFailed;
        }

        // ACL or read-only file. We treat this as a hard failure after retries.
        if (ex is UnauthorizedAccessException)
            return Outcome.Locked;

        // TagLib generally surfaces structural problems (bad headers, truncated
        // files) as ArgumentException or its own CorruptFileException type. We
        // can't reference the TagLib type without dragging it into a constants
        // file, so we match by full type name.
        var typeName = ex.GetType().FullName ?? string.Empty;
        if (typeName.Contains("CorruptFileException", StringComparison.OrdinalIgnoreCase))
            return Outcome.Corrupt;

        return Outcome.Unknown;
    }

    /// <summary>
    /// True when the outcome should be retried in the next off-hours window
    /// (subject to <c>max_retry_attempts</c>).
    /// </summary>
    public static bool IsTransient(Outcome outcome)
        => outcome == Outcome.Locked || outcome == Outcome.IoFailed;
}
