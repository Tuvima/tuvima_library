using MediaEngine.Domain.Services;

namespace MediaEngine.Domain.Tests;

public sealed class MediaMimeTypesTests
{
    [Theory]
    [InlineData("cover.jpg", "image/jpeg")]
    [InlineData("cover.jpeg", "image/jpeg")]
    [InlineData("COVER.PNG", "image/png")]
    [InlineData("cover.webp", "image/webp")]
    [InlineData("cover.gif", "image/gif")]
    [InlineData("cover.bmp", "image/bmp")]
    [InlineData("cover.svg", "image/svg+xml")]
    [InlineData("cover.avif", "image/avif")]
    [InlineData("cover.ico", "image/x-icon")]
    [InlineData(".png", "image/png")]
    [InlineData("no-extension", "application/octet-stream")]
    [InlineData("cover.tiff", "application/octet-stream")]
    public void GetImageMimeType_MapsExtensionToMimeType(string input, string expected)
    {
        Assert.Equal(expected, MediaMimeTypes.GetImageMimeType(input));
    }

    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("image/gif", ".gif")]
    [InlineData("image/webp", ".webp")]
    [InlineData("image/svg+xml", ".svg")]
    [InlineData("image/bmp", ".bmp")]
    [InlineData("image/avif", ".avif")]
    [InlineData("image/x-icon", ".ico")]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/jpg", ".jpg")]
    public void InferImageExtension_MapsContentTypeToExtension(string contentType, string expected)
    {
        Assert.Equal(expected, MediaMimeTypes.InferImageExtension(contentType));
    }

    [Theory]
    [InlineData("https://example.com/covers/cover.jpg", ".jpg")]
    [InlineData("https://example.com/covers/cover.png?size=l", ".png")]
    [InlineData("/local/staging/cover.webp", ".webp")]
    public void InferImageExtension_ExtractsExtensionFromUrlOrPath(string url, string expected)
    {
        Assert.Equal(expected, MediaMimeTypes.InferImageExtension(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InferImageExtension_ReturnsNull_ForNullOrBlankInput(string? value)
    {
        Assert.Null(MediaMimeTypes.InferImageExtension(value));
    }

    [Fact]
    public void InferImageExtension_ReturnsNull_WhenUrlHasNoExtension()
    {
        Assert.Null(MediaMimeTypes.InferImageExtension("https://example.com/covers/latest"));
    }

    [Fact]
    public void InferImageExtension_ReturnsNull_ForUnrecognizedContentType()
    {
        Assert.Null(MediaMimeTypes.InferImageExtension("image/tiff"));
    }
}
