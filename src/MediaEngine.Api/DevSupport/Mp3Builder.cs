using System.Text;

namespace MediaEngine.Api.DevSupport;

/// <summary>
/// Creates minimal valid MP3 files with ID3v2.3 tags for development seeding.
/// Produces files that pass <c>AudioProcessor.CanProcess</c>:
///   1) ID3v2.3 header with metadata frames
///   2) One silent MPEG1 Layer3 audio frame
/// </summary>
public static class Mp3Builder
{
    /// <summary>
    /// Generates a valid MP3 file as a byte array with ID3v2.3 tags.
    /// </summary>
    public static byte[] Create(
        string title,
        string artist,
        string? album = null,
        int year = 0,
        string language = "eng",
        string? narrator = null,
        string? asin = null,
        string? series = null,
        int? seriesPosition = null)
    {
        album ??= title;

        using var stream = new MemoryStream();

        // Build all ID3v2.3 frames first, then compute total size for the header.
        var frames = new MemoryStream();

        WriteTextFrame(frames, "TIT2", title);
        WriteTextFrame(frames, "TPE1", artist);
        WriteTextFrame(frames, "TALB", album);
        if (year > 0) WriteTextFrame(frames, "TYER", year.ToString());
        WriteTextFrame(frames, "TLAN", language);

        // Genre: "Audiobook" to help disambiguation heuristics.
        WriteTextFrame(frames, "TCON", "Audiobook");

        if (narrator is not null)
        {
            // Audiobook convention: TPE2 (AlbumArtist) = narrator
            WriteTextFrame(frames, "TPE2", narrator);
            WriteTxxxFrame(frames, "NARRATOR", narrator);

            // Write author explicitly so AudioProcessor can reliably distinguish
            // author (TPE1) from narrator (TPE2). Also write TXXX:AUTHOR and
            // a comment pattern as redundant signals.
            WriteTxxxFrame(frames, "AUTHOR", artist);
            WriteCommentFrame(frames, $"By: {artist}, Narrated by: {narrator}");
        }

        if (asin is not null)
            WriteTxxxFrame(frames, "ASIN", asin);

        if (series is not null)
            WriteTxxxFrame(frames, "SERIES", series);

        if (seriesPosition is not null)
            WriteTxxxFrame(frames, "SERIES_INDEX", seriesPosition.Value.ToString());

        byte[] frameData = frames.ToArray();

        // ID3v2.3 header: "ID3" + version 2.3 + no flags + syncsafe size.
        stream.Write("ID3"u8);
        stream.WriteByte(3);   // Version major: 3
        stream.WriteByte(0);   // Version minor: 0
        stream.WriteByte(0);   // Flags: none
        WriteSyncSafe(stream, frameData.Length);
        stream.Write(frameData);

        // One minimal MPEG1 Layer3 128kbps 44100Hz stereo silent frame.
        // Frame header: 0xFF 0xFB = sync + MPEG1/Layer3, 0x90 = 128kbps/44100Hz, 0x00 = padding/stereo
        // Frame size for 128kbps @ 44100Hz = 417 bytes (header + 413 bytes of silence).
        stream.WriteByte(0xFF);
        stream.WriteByte(0xFB);
        stream.WriteByte(0x90);
        stream.WriteByte(0x00);
        stream.Write(new byte[413]); // Silent audio data

        return stream.ToArray();
    }

    // ── ID3v2.3 frame helpers ─────────────────────────────────────────────────

    private static readonly Encoding Latin1 = Encoding.Latin1;

    /// <summary>
    /// Writes an ID3v2.3 text frame (encoding byte 0x00 = ISO-8859-1).
    /// Frame: [4-byte ID][4-byte big-endian size][2-byte flags][0x00 encoding][text bytes]
    /// </summary>
    private static void WriteTextFrame(Stream stream, string frameId, string text)
    {
        byte[] textBytes = Latin1.GetBytes(text);
        int dataSize = 1 + textBytes.Length; // encoding byte + text

        stream.Write(Encoding.ASCII.GetBytes(frameId));
        WriteBigEndianInt(stream, dataSize);
        stream.WriteByte(0); // Flags high
        stream.WriteByte(0); // Flags low
        stream.WriteByte(0); // Encoding: ISO-8859-1
        stream.Write(textBytes);
    }

    /// <summary>
    /// Writes an ID3v2.3 TXXX (user-defined text information) frame.
    /// Frame: [TXXX][size][flags][0x00 encoding][description\0][value]
    /// </summary>
    private static void WriteTxxxFrame(Stream stream, string description, string value)
    {
        byte[] descBytes = Latin1.GetBytes(description);
        byte[] valBytes = Latin1.GetBytes(value);
        int dataSize = 1 + descBytes.Length + 1 + valBytes.Length; // encoding + desc + null + value

        stream.Write("TXXX"u8);
        WriteBigEndianInt(stream, dataSize);
        stream.WriteByte(0); // Flags high
        stream.WriteByte(0); // Flags low
        stream.WriteByte(0); // Encoding: ISO-8859-1
        stream.Write(descBytes);
        stream.WriteByte(0); // Null separator
        stream.Write(valBytes);
    }

    /// <summary>
    /// Writes an ID3v2.3 COMM (comment) frame with empty language and description.
    /// Frame: [COMM][size][flags][0x00 encoding][3-byte lang][description\0][text]
    /// </summary>
    private static void WriteCommentFrame(Stream stream, string text)
    {
        byte[] textBytes = Latin1.GetBytes(text);
        // encoding(1) + lang(3) + description null terminator(1) + text
        int dataSize = 1 + 3 + 1 + textBytes.Length;

        stream.Write("COMM"u8);
        WriteBigEndianInt(stream, dataSize);
        stream.WriteByte(0); // Flags high
        stream.WriteByte(0); // Flags low
        stream.WriteByte(0); // Encoding: ISO-8859-1
        stream.Write("eng"u8); // Language
        stream.WriteByte(0); // Empty description + null terminator
        stream.Write(textBytes);
    }

    private static void WriteBigEndianInt(Stream stream, int value)
    {
        stream.WriteByte((byte)((value >> 24) & 0xFF));
        stream.WriteByte((byte)((value >> 16) & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)(value & 0xFF));
    }

    /// <summary>
    /// Writes a 4-byte syncsafe integer (each byte uses only 7 bits).
    /// Required by the ID3v2 specification for the tag header size field.
    /// </summary>
    private static void WriteSyncSafe(Stream stream, int value)
    {
        stream.WriteByte((byte)((value >> 21) & 0x7F));
        stream.WriteByte((byte)((value >> 14) & 0x7F));
        stream.WriteByte((byte)((value >> 7) & 0x7F));
        stream.WriteByte((byte)(value & 0x7F));
    }
}
