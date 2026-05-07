namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// View model for a single stat card in the dashboard stats bar.
/// </summary>
public sealed class StatCardModel
{
    public required string Id { get; init; }
    public required string Icon { get; init; }
    public required int Count { get; init; }
    public required string Label { get; init; }
    public required string Color { get; init; }
    public bool IsActive { get; set; } = true;
}

