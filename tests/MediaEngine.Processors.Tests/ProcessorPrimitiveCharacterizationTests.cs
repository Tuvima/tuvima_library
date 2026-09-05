using System.Text.RegularExpressions;
using MediaEngine.Domain.Enums;
using MediaEngine.Processors;
using MediaEngine.Processors.Contracts;
using MediaEngine.Processors.Processors;

namespace MediaEngine.Processors.Tests;

public sealed class ProcessorPrimitiveCharacterizationTests
{
    [Fact]
    public async Task FormatProcessors_PreserveCorruptResultClassification()
    {
        var filePath = Path.Combine(
            Path.GetTempPath(),
            $"processor_primitives_{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(filePath, [0x00, 0x01, 0x02]);

        try
        {
            var processors = new (IMediaProcessor Processor, MediaType ExpectedType)[]
            {
                (new AudioProcessor(), MediaType.Unknown),
                (new VideoProcessor(new StubVideoMetadataExtractor()), MediaType.Movies),
                (new ComicProcessor(), MediaType.Comics),
                (new EpubProcessor(), MediaType.Books),
                (new PdfProcessor(), MediaType.Books),
                (new AzW3Processor(), MediaType.Books),
                (new DiscImageProcessor(), MediaType.Movies),
            };

            foreach (var (processor, expectedType) in processors)
            {
                var result = await processor.ProcessAsync(filePath);

                Assert.Equal(filePath, result.FilePath);
                Assert.Equal(expectedType, result.DetectedType);
                Assert.True(result.IsCorrupt);
                Assert.False(string.IsNullOrWhiteSpace(result.CorruptReason));
                Assert.Empty(result.Claims);
            }
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task PdfProcessor_PreservesClaimValuesAndConfidence()
    {
        var filePath = Path.Combine(
            Path.GetTempPath(),
            $"Processor.Claims_{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(filePath, "%PDF-1.7\n%%EOF"u8.ToArray());

        try
        {
            var result = await new PdfProcessor().ProcessAsync(filePath);

            Assert.Collection(
                result.Claims,
                title =>
                {
                    Assert.Equal("title", title.Key);
                    Assert.StartsWith("Processor Claims ", title.Value);
                    Assert.Equal(0.50, title.Confidence);
                },
                container =>
                {
                    Assert.Equal("container", container.Key);
                    Assert.Equal("PDF", container.Value);
                    Assert.Equal(1.0, container.Confidence);
                });
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void ProcessorImplementations_UseSharedPrimitiveFactories()
    {
        var processorsDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MediaEngine.Processors",
            "Processors");
        var implementationFiles = Directory
            .EnumerateFiles(processorsDirectory, "*Processor.cs")
            .Where(path => !path.EndsWith("ProcessorPrimitives.cs", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(8, implementationFiles.Length);

        foreach (var file in implementationFiles)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("new ExtractedClaim", source, StringComparison.Ordinal);
            Assert.DoesNotMatch(
                new Regex(@"\bIsCorrupt\s*=\s*true\b", RegexOptions.CultureInvariant),
                source);
            Assert.DoesNotContain("new FileStream(", source, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
