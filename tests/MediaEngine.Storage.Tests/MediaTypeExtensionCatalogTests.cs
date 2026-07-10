using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Services;

namespace MediaEngine.Storage.Tests;

public sealed class MediaTypeExtensionCatalogTests
{
    [Fact]
    public void Catalog_ReadsMediaExtensionsFromConfiguration()
    {
        using var loader = new ConfigurationDirectoryLoader(FindRepoDir("config"));
        var catalog = new MediaTypeExtensionCatalog(loader);

        Assert.True(catalog.IsKnownMediaExtension(".flac"));
        Assert.True(catalog.IsKnownMediaExtension("epub"));
        Assert.True(catalog.IsUnambiguousExtension(".epub"));
        Assert.False(catalog.IsUnambiguousExtension(".mp3"));
        Assert.True(catalog.IsStrongFormatExtension(".cbz"));
        Assert.False(catalog.IsStrongFormatExtension(".pdf"));
        Assert.True(catalog.IsVideoExtension(".mkv"));

        Assert.Equal(MediaType.Audiobooks, catalog.ResolveMediaTypeFromExtension(".m4b"));
        Assert.Equal(MediaType.Music, catalog.ResolveMediaTypeFromExtension(".mp3"));
        Assert.Equal(MediaType.Movies, catalog.ResolveMediaTypeFromExtension(".mkv"));
        Assert.Equal(MediaType.Books, catalog.ResolveMediaTypeFromExtension(".epub"));
        Assert.Equal(MediaType.Comics, catalog.ResolveMediaTypeFromExtension(".cbz"));
    }

    private static string FindRepoDir(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find {Path.Combine(parts)} from {AppContext.BaseDirectory}");
    }
}
