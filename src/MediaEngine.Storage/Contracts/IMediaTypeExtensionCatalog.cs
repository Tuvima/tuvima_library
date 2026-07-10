using MediaEngine.Domain.Enums;

namespace MediaEngine.Storage.Contracts;

/// <summary>
/// Provides media extension policy from <c>config/media_types.json</c>.
/// </summary>
public interface IMediaTypeExtensionCatalog
{
    IReadOnlySet<string> GetAllMediaExtensions();

    IReadOnlySet<string> GetExtensionsFor(MediaType mediaType);

    bool IsKnownMediaExtension(string? extension);

    bool IsUnambiguousExtension(string? extension);

    bool IsStrongFormatExtension(string? extension);

    bool IsVideoExtension(string? extension);

    MediaType ResolveMediaTypeFromExtension(string? extension);
}
