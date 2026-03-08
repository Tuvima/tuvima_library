namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Defines a content-type lane in the navigation dock.
/// Each lane filters the library to specific media types and offers
/// contextual sub-items (tabs within the lane page).
/// </summary>
public sealed record LaneDefinition(
    string Key,
    string Label,
    string Icon,
    string[] MediaTypes,
    string[] SubItems);
