namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Related hubs result from the cascade algorithm (series → author → genre → explore).
/// </summary>
public sealed class RelatedHubsViewModel
{
    /// <summary>The section heading to display (e.g. "More in Dune", "More by Frank Herbert").</summary>
    public string SectionTitle { get; init; } = string.Empty;

    /// <summary>Machine reason: series | author | genre | explore.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Related hubs to display in the swimlane.</summary>
    public IReadOnlyList<HubViewModel> Hubs { get; init; } = [];
}
