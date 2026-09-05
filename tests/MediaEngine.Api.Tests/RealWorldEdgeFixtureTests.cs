using System.Reflection;
using MediaEngine.Api.DevSupport;
using MediaEngine.Ingestion;
using MediaEngine.Processors.Processors;

namespace MediaEngine.Api.Tests;

public sealed class RealWorldEdgeFixtureTests
{
    [Fact]
    public async Task Builder_ProducesManifestAndProcessableRealWorldEdgeFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tuvima_edge_test_{Guid.NewGuid():N}");
        try
        {
            await RealWorldEdgeFixtureBuilder.CreateAsync(root);

            Assert.True(File.Exists(Path.Combine(root, "MANIFEST.json")));
            var audioDirectory = Path.Combine(root, "audiobooks", "Andy Weir", "Project Hail Mary");
            var audioFiles = Directory.GetFiles(audioDirectory, "*.mp3");
            Assert.Equal(3, audioFiles.Length);

            var audio = await new AudioProcessor().ProcessAsync(audioFiles[0]);
            Assert.Contains(audio.Claims, claim => claim.Key == "book_title" && claim.Value == "Project Hail Mary");
            Assert.Contains(audio.Claims, claim => claim.Key == "audiobook_part_count" && claim.Value == "3");
            Assert.Contains(audio.Claims, claim => claim.Key == "sidecar_chapter_count" && claim.Value == "3");

            var azw3 = Path.Combine(root, "books", "Noam Chomsky", "Manufacturing Consent", "Manufacturing Consent.azw3");
            var book = await new AzW3Processor().ProcessAsync(azw3);
            Assert.False(book.IsCorrupt);
            Assert.Contains(book.Claims, claim => claim.Key == "title" && claim.Value == "Manufacturing Consent" && claim.Confidence > 0.9);
            Assert.Contains(book.Claims, claim => claim.Key == "reader_support" && claim.Value == "external");

            var iso = Path.Combine(root, "movies", "Edge Disc (2004).iso");
            var disc = await new DiscImageProcessor().ProcessAsync(iso);
            Assert.False(disc.IsCorrupt);
            Assert.Contains(disc.Claims, claim => claim.Key == "playback_support" && claim.Value == "disc-image-requires-extraction");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(".fuse_hidden000e325b0001315a")]
    [InlineData("video.mp4.partial")]
    [InlineData("download.mkv.crdownload")]
    public void IngestionScanner_IgnoresTransientFilesystemArtifacts(string fileName)
    {
        var method = typeof(IngestionEngine).GetMethod("IsTransientInputPath", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.True(Assert.IsType<bool>(method!.Invoke(null, [Path.Combine("C:\\fixtures", fileName)])));
    }
}
