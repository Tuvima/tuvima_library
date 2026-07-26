namespace MediaEngine.Domain.Services;

/// <summary>
/// Shared helpers for picking the first meaningful value out of several
/// candidate strings. Centralizes a pattern that was previously copied —
/// with subtly different signatures (nullable vs. non-nullable return) —
/// across dozens of Engine and Dashboard files. See
/// <c>SharedPrimitivesGuardrailTests</c> for the full list of call sites
/// still awaiting migration onto this shared helper.
/// </summary>
public static class StringHelpers
{
    /// <summary>
    /// Returns the first value in <paramref name="values"/> that is neither
    /// <c>null</c> nor whitespace-only, or <c>null</c> if every candidate is
    /// missing or blank.
    /// </summary>
    public static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    /// <summary>
    /// Returns the first value in <paramref name="values"/> that is neither
    /// <c>null</c> nor whitespace-only, or <paramref name="fallback"/> when every
    /// candidate is missing or blank. Use this overload at call sites whose
    /// contract requires a non-nullable <see cref="string"/> result.
    /// </summary>
    public static string FirstNonBlankOr(string fallback, params string?[] values)
        => FirstNonBlank(values) ?? fallback;

    /// <summary>
    /// Returns <c>null</c> when <paramref name="value"/> is <c>null</c> or
    /// whitespace-only; otherwise returns <paramref name="value"/> unchanged.
    /// </summary>
    public static string? BlankToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
