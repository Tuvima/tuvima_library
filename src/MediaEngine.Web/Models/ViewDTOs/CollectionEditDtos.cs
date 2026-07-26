namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// A single entry in the field diff grid. Compares current canonical value
/// against a provider's search result value.
/// </summary>
public sealed record FieldDiffEntry(
    string Key,
    string DisplayName,
    string? CurrentValue,
    double? CurrentConfidence,
    string? ProviderValue,
    bool IsDifferent,
    bool IsSelected);
