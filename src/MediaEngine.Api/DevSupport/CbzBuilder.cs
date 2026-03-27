using System.IO.Compression;
using System.Text;

namespace MediaEngine.Api.DevSupport;

/// <summary>
/// Creates minimal valid CBZ files with ComicInfo.xml for development seeding.
/// Produces files that pass <c>ComicProcessor.CanProcess</c>:
///   1) ZIP magic bytes (automatic from ZipArchive)
///   2) Contains image entries (.jpg)
///   3) No "mimetype" entry with EPUB content (excluded by ComicProcessor)
/// </summary>
public static class CbzBuilder
{
    /// <summary>
    /// Generates a valid CBZ file as a byte array.
    /// Contains a minimal JPEG image and a ComicInfo.xml with metadata.
    /// </summary>
    public static byte[] Create(
        string title,
        string? writer = null,
        string? series = null,
        int? number = null,
        int year = 0,
        string? genre = null,
        string? summary = null,
        string? publisher = null,
        string? penciller = null,
        int pageCount = 1)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // 1. Add dummy JPEG pages (minimal valid JPEG: SOI + EOI markers)
            byte[] minimalJpeg = [0xFF, 0xD8, 0xFF, 0xD9]; // SOI + EOI
            for (int i = 1; i <= pageCount; i++)
            {
                var entry = archive.CreateEntry($"page_{i:D3}.jpg", CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(minimalJpeg);
            }

            // 2. Add ComicInfo.xml with metadata
            string comicInfo = BuildComicInfoXml(title, writer, series, number, year, genre, summary, publisher, penciller, pageCount);
            var xmlEntry = archive.CreateEntry("ComicInfo.xml", CompressionLevel.Fastest);
            using (var xmlStream = new StreamWriter(xmlEntry.Open(), new UTF8Encoding(false)))
            {
                xmlStream.Write(comicInfo);
            }
        }

        return stream.ToArray();
    }

    private static string BuildComicInfoXml(
        string title, string? writer, string? series, int? number,
        int year, string? genre, string? summary, string? publisher,
        string? penciller, int pageCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<ComicInfo>");
        sb.AppendLine($"  <Title>{Escape(title)}</Title>");

        if (writer is not null)
            sb.AppendLine($"  <Writer>{Escape(writer)}</Writer>");
        if (series is not null)
            sb.AppendLine($"  <Series>{Escape(series)}</Series>");
        if (number is not null)
            sb.AppendLine($"  <Number>{number}</Number>");
        if (year > 0)
            sb.AppendLine($"  <Year>{year}</Year>");
        if (genre is not null)
            sb.AppendLine($"  <Genre>{Escape(genre)}</Genre>");
        if (summary is not null)
            sb.AppendLine($"  <Summary>{Escape(summary)}</Summary>");
        if (publisher is not null)
            sb.AppendLine($"  <Publisher>{Escape(publisher)}</Publisher>");
        if (penciller is not null)
            sb.AppendLine($"  <Penciller>{Escape(penciller)}</Penciller>");

        sb.AppendLine($"  <PageCount>{pageCount}</PageCount>");
        sb.AppendLine("</ComicInfo>");

        return sb.ToString();
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;")
             .Replace("<", "&lt;")
             .Replace(">", "&gt;")
             .Replace("\"", "&quot;");
}
