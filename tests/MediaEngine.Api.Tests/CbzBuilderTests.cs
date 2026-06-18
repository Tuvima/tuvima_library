using System.IO.Compression;
using MediaEngine.Api.DevSupport;

namespace MediaEngine.Api.Tests;

public sealed class CbzBuilderTests
{
    [Fact]
    public void Create_GeneratesIssueSpecificCoverPages()
    {
        var issueOne = CbzBuilder.Create(
            "Saga Chapter One",
            writer: "Brian K. Vaughan",
            series: "Saga",
            number: 1,
            year: 2012,
            genre: "Science Fiction");
        var issueTwo = CbzBuilder.Create(
            "Saga Chapter Two",
            writer: "Brian K. Vaughan",
            series: "Saga",
            number: 2,
            year: 2012,
            genre: "Science Fiction");

        var firstCover = ReadEntry(issueOne, "page_001.jpg");
        var secondCover = ReadEntry(issueTwo, "page_001.jpg");

        Assert.NotEqual(firstCover, secondCover);
        Assert.True(firstCover.Length > 100);
        Assert.True(secondCover.Length > 100);
    }

    private static byte[] ReadEntry(byte[] archiveBytes, string entryName)
    {
        using var stream = new MemoryStream(archiveBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"Missing {entryName}");
        using var entryStream = entry.Open();
        using var output = new MemoryStream();
        entryStream.CopyTo(output);
        return output.ToArray();
    }
}
