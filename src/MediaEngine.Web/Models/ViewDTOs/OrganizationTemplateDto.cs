using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Represents the file organization template exchanged with the Engine's
/// <c>GET /settings/organization-template</c> and <c>PUT /settings/organization-template</c> endpoints.
/// </summary>
public sealed record OrganizationTemplateDto(
    [property: JsonPropertyName("template")] string Template,
    [property: JsonPropertyName("preview")]  string? Preview);
