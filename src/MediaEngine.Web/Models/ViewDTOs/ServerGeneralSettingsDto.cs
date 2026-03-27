using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Server identity and regional settings exchanged with the Engine's
/// <c>GET /settings/server-general</c> and <c>PUT /settings/server-general</c> endpoints.
/// </summary>
public sealed record ServerGeneralSettingsDto(
    [property: JsonPropertyName("server_name")]          string ServerName          = "Tuvima Library",
    [property: JsonPropertyName("language")]              string Language            = "en",
    [property: JsonPropertyName("display_language")]      string DisplayLanguage     = "en",
    [property: JsonPropertyName("metadata_language")]     string MetadataLanguage    = "en",
    [property: JsonPropertyName("additional_languages")]  List<string>? AdditionalLanguages = null,
    [property: JsonPropertyName("accept_any_language")]   bool AcceptAnyLanguage     = true,
    [property: JsonPropertyName("country")]               string Country             = "US",
    [property: JsonPropertyName("date_format")]           string DateFormat          = "system",
    [property: JsonPropertyName("time_format")]           string TimeFormat          = "system");
