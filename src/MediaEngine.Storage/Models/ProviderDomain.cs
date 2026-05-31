using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>
/// The media domain a provider specializes in for UI grouping and provider routing.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProviderDomain
{
    /// <summary>Applies across all media types.</summary>
    Universal,

    /// <summary>E-book oriented provider.</summary>
    Ebook,

    /// <summary>Audiobook oriented provider.</summary>
    Audiobook,

    /// <summary>Comic and graphic novel oriented provider.</summary>
    Comic,

    /// <summary>Film and TV oriented provider.</summary>
    Video,

    /// <summary>Music oriented provider.</summary>
    Music,
}
