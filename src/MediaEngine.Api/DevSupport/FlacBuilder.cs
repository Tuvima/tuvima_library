using System.Text;

namespace MediaEngine.Api.DevSupport;

/// <summary>
/// Creates minimal valid FLAC files with Vorbis comment metadata for development seeding.
/// Produces files that pass <c>AudioProcessor.CanProcess</c> via "fLaC" magic bytes.
/// FLAC files are unambiguously classified as Music by AudioProcessor (0.95 confidence).
/// </summary>
public static class FlacBuilder
{
    /// <summary>
    /// Generates a minimal valid FLAC file as a byte array.
    /// Contains the fLaC marker, a STREAMINFO metadata block, and a VORBIS_COMMENT block.
    /// </summary>
    public static byte[] Create(
        string title,
        string artist,
        string? album = null,
        int year = 0,
        string? genre = null,
        int? trackNumber = null)
    {
        album ??= title;

        using var stream = new MemoryStream();

        // 1. fLaC stream marker
        stream.Write("fLaC"u8);

        // 2. STREAMINFO metadata block (type 0) — mandatory, always first
        //    34 bytes of STREAMINFO data (minimum)
        WriteMetadataBlockHeader(stream, blockType: 0, dataLength: 34, isLast: false);
        WriteStreamInfo(stream);

        // 3. VORBIS_COMMENT metadata block (type 4) — contains our tags
        byte[] vorbisData = BuildVorbisComment(title, artist, album, year, genre, trackNumber);
        WriteMetadataBlockHeader(stream, blockType: 4, dataLength: vorbisData.Length, isLast: true);
        stream.Write(vorbisData);

        // No audio frames needed — processors only read metadata headers.
        // A real FLAC decoder would reject this, but AudioProcessor/TagLib
        // will read the Vorbis comments successfully.

        return stream.ToArray();
    }

    /// <summary>
    /// Writes a FLAC metadata block header (4 bytes).
    /// Bit layout: [isLast:1][blockType:7][dataLength:24]
    /// </summary>
    private static void WriteMetadataBlockHeader(Stream stream, int blockType, int dataLength, bool isLast)
    {
        byte firstByte = (byte)((isLast ? 0x80 : 0x00) | (blockType & 0x7F));
        stream.WriteByte(firstByte);
        stream.WriteByte((byte)((dataLength >> 16) & 0xFF));
        stream.WriteByte((byte)((dataLength >> 8) & 0xFF));
        stream.WriteByte((byte)(dataLength & 0xFF));
    }

    /// <summary>
    /// Writes a minimal 34-byte STREAMINFO block.
    /// Uses dummy values — we only need this to satisfy the FLAC format requirement.
    /// </summary>
    private static void WriteStreamInfo(Stream stream)
    {
        // min_block_size (16-bit): 4096
        stream.WriteByte(0x10); stream.WriteByte(0x00);
        // max_block_size (16-bit): 4096
        stream.WriteByte(0x10); stream.WriteByte(0x00);
        // min_frame_size (24-bit): 0 (unknown)
        stream.WriteByte(0); stream.WriteByte(0); stream.WriteByte(0);
        // max_frame_size (24-bit): 0 (unknown)
        stream.WriteByte(0); stream.WriteByte(0); stream.WriteByte(0);
        // sample_rate (20-bit) | channels-1 (3-bit) | bps-1 (5-bit) | total_samples (36-bit)
        // 44100 Hz, 2 channels (stereo), 16 bps, 0 samples
        // 44100 = 0xAC44 → 20-bit = 0xAC440
        // channels-1 = 1 → 3-bit = 001
        // bps-1 = 15 → 5-bit = 01111
        // Packed: 0xAC44 0x10F0 0000 0000
        stream.WriteByte(0xAC); stream.WriteByte(0x44);
        stream.WriteByte(0x10); stream.WriteByte(0xF0);
        stream.WriteByte(0x00); stream.WriteByte(0x00);
        stream.WriteByte(0x00); stream.WriteByte(0x00);
        // MD5 signature (16 bytes): all zeros
        stream.Write(new byte[16]);
    }

    /// <summary>
    /// Builds a Vorbis Comment block as used in FLAC metadata.
    /// Format: vendor_string_length (LE32) + vendor_string + comment_count (LE32) + comments[]
    /// Each comment: length (LE32) + "KEY=value" UTF-8 bytes
    /// </summary>
    private static byte[] BuildVorbisComment(
        string title, string artist, string? album, int year, string? genre, int? trackNumber)
    {
        using var stream = new MemoryStream();
        const string vendor = "Tuvima Library Seed";

        // Vendor string
        byte[] vendorBytes = Encoding.UTF8.GetBytes(vendor);
        WriteLittleEndian32(stream, vendorBytes.Length);
        stream.Write(vendorBytes);

        // Collect comments
        var comments = new List<string>
        {
            $"TITLE={title}",
            $"ARTIST={artist}"
        };
        if (album is not null) comments.Add($"ALBUM={album}");
        if (year > 0) comments.Add($"DATE={year}");
        if (genre is not null) comments.Add($"GENRE={genre}");
        if (trackNumber is not null) comments.Add($"TRACKNUMBER={trackNumber}");

        // Comment count
        WriteLittleEndian32(stream, comments.Count);

        // Each comment
        foreach (string comment in comments)
        {
            byte[] commentBytes = Encoding.UTF8.GetBytes(comment);
            WriteLittleEndian32(stream, commentBytes.Length);
            stream.Write(commentBytes);
        }

        return stream.ToArray();
    }

    private static void WriteLittleEndian32(Stream stream, int value)
    {
        stream.WriteByte((byte)(value & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)((value >> 16) & 0xFF));
        stream.WriteByte((byte)((value >> 24) & 0xFF));
    }
}
