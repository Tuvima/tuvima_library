using System.Data;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite GUID conversion helpers.
/// Internal GUIDs are stored as 16-byte BLOBs using RFC4122/network byte order.
/// API contracts still expose ordinary string GUIDs.
/// </summary>
public static class GuidSql
{
    public static byte[] ToBlob(Guid value)
    {
        var bytes = new byte[16];
        value.TryWriteBytes(bytes, bigEndian: true, out _);
        return bytes;
    }

    public static object? ToDb(Guid? value) =>
        value.HasValue ? ToBlob(value.Value) : DBNull.Value;

    public static Guid FromDb(object value)
    {
        if (value is byte[] bytes)
            return new Guid(bytes, bigEndian: true);

        if (value is string text)
            return Guid.Parse(text);

        if (value is Guid guid)
            return guid;

        throw new InvalidCastException($"Cannot convert SQLite value of type {value.GetType().Name} to Guid.");
    }

    public static Guid? FromDbNullable(object? value)
    {
        if (value is null or DBNull)
            return null;

        return FromDb(value);
    }

    public static string ToText(object value) => FromDb(value).ToString("D");
}
