using System.Text;

namespace MediaEngine.Api.DevSupport;

/// <summary>
/// Creates minimal valid MP4 files with embedded metadata for development seeding.
/// Produces files that pass <c>VideoProcessor.CanProcess</c> via ftyp box detection.
/// </summary>
public static class Mp4Builder
{
    /// <summary>
    /// Generates a minimal valid MP4 file as a byte array.
    /// Contains an ftyp box (for format detection) and an moov box with
    /// embedded metadata (title, artist, year) in udta/meta atoms.
    /// </summary>
    public static byte[] Create(
        string title,
        string? director = null,
        int year = 0,
        string? showName = null,
        int? seasonNumber = null,
        int? episodeNumber = null)
    {
        using var stream = new MemoryStream();

        // ftyp box: file type box — identifies this as MP4
        // Structure: [size:4][type:4="ftyp"][major_brand:4="isom"][minor_version:4][compatible_brands...]
        WriteFtypBox(stream);

        // moov box: movie box — contains metadata
        // We embed a minimal udta box with a ©nam (title) and ©ART (artist) atom.
        WriteMoovBox(stream, title, director, year, showName, seasonNumber, episodeNumber);

        // mdat box: media data box — empty (no actual video frames)
        WriteMdatBox(stream);

        return stream.ToArray();
    }

    private static void WriteFtypBox(Stream stream)
    {
        // ftyp box: 20 bytes
        // size(4) + "ftyp"(4) + major_brand "isom"(4) + minor_version(4) + compatible "isom"(4)
        WriteBigEndian32(stream, 20);         // box size
        stream.Write("ftyp"u8);              // box type
        stream.Write("isom"u8);              // major brand
        WriteBigEndian32(stream, 0x200);      // minor version
        stream.Write("isom"u8);              // compatible brand
    }

    private static void WriteMoovBox(Stream stream, string title, string? director, int year,
        string? showName = null, int? seasonNumber = null, int? episodeNumber = null)
    {
        // Build the udta content first to know sizes
        using var udtaContent = new MemoryStream();

        // ©nam atom (title)
        if (!string.IsNullOrEmpty(title))
            WriteItunesStringAtom(udtaContent, "\u00A9nam", title);

        // ©ART atom (artist/director)
        if (!string.IsNullOrEmpty(director))
            WriteItunesStringAtom(udtaContent, "\u00A9ART", director);

        // ©day atom (year)
        if (year > 0)
            WriteItunesStringAtom(udtaContent, "\u00A9day", year.ToString());

        // tvsh atom (TV show name)
        if (!string.IsNullOrEmpty(showName))
            WriteItunesStringAtom(udtaContent, "tvsh", showName);

        // tvsn atom (TV season number)
        if (seasonNumber.HasValue)
            WriteItunesStringAtom(udtaContent, "tvsn", seasonNumber.Value.ToString());

        // tves atom (TV episode number)
        if (episodeNumber.HasValue)
            WriteItunesStringAtom(udtaContent, "tves", episodeNumber.Value.ToString());

        byte[] udtaData = udtaContent.ToArray();

        // udta box: 8 (header) + content
        int udtaSize = 8 + udtaData.Length;

        // mvhd box: minimal movie header (108 bytes for version 0)
        int mvhdSize = 108;

        // moov box: 8 (header) + mvhd + udta
        int moovSize = 8 + mvhdSize + (udtaData.Length > 0 ? udtaSize : 0);

        WriteBigEndian32(stream, moovSize);
        stream.Write("moov"u8);

        // Minimal mvhd (movie header) box — version 0
        WriteBigEndian32(stream, mvhdSize);
        stream.Write("mvhd"u8);
        stream.Write(new byte[mvhdSize - 8]); // zeroed — timescale=0, duration=0, etc.

        // udta box with metadata atoms
        if (udtaData.Length > 0)
        {
            WriteBigEndian32(stream, udtaSize);
            stream.Write("udta"u8);
            stream.Write(udtaData);
        }
    }

    private static void WriteMdatBox(Stream stream)
    {
        // Empty mdat box: just the header
        WriteBigEndian32(stream, 8);
        stream.Write("mdat"u8);
    }

    private static void WriteItunesStringAtom(Stream stream, string atomName, string value)
    {
        byte[] valueBytes = Encoding.UTF8.GetBytes(value);
        // data atom: 8 (header) + 8 (type + locale) + value
        int dataSize = 8 + 8 + valueBytes.Length;
        // outer atom: 8 (header) + data atom
        int atomSize = 8 + dataSize;

        // Atom name as bytes (4 bytes, may include © = 0xA9 in Latin-1)
        byte[] nameBytes = Encoding.Latin1.GetBytes(atomName);

        WriteBigEndian32(stream, atomSize);
        stream.Write(nameBytes.AsSpan(0, 4));

        // data sub-atom
        WriteBigEndian32(stream, dataSize);
        stream.Write("data"u8);
        WriteBigEndian32(stream, 1); // type: UTF-8 text
        WriteBigEndian32(stream, 0); // locale
        stream.Write(valueBytes);
    }

    private static void WriteBigEndian32(Stream stream, int value)
    {
        stream.WriteByte((byte)((value >> 24) & 0xFF));
        stream.WriteByte((byte)((value >> 16) & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)(value & 0xFF));
    }
}
