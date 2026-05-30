using System.Data;
using Dapper;

namespace MediaEngine.Storage;

/// <summary>
/// Registers custom Dapper type handlers for types that SQLite stores in
/// storage-specific shapes but .NET represents as structs. Call
/// <see cref="Configure"/> once at startup before any Dapper queries execute.
/// </summary>
public static class DapperConfiguration
{
    private static bool _configured;

    /// <summary>Register all custom type handlers. Safe to call multiple times.</summary>
    public static void Configure()
    {
        if (_configured) return;

        SqlMapper.AddTypeHandler(new GuidTypeHandler());
        SqlMapper.AddTypeHandler(new DateTimeOffsetTypeHandler());
        SqlMapper.AddTypeHandler(new NullableGuidTypeHandler());
        SqlMapper.AddTypeHandler(new NullableDateTimeOffsetTypeHandler());

        // Force Guid values through the custom BLOB handler.
        SqlMapper.RemoveTypeMap(typeof(Guid));
        SqlMapper.RemoveTypeMap(typeof(Guid?));

        _configured = true;
    }

    /// <summary>Guid stored as BLOB (16-byte RFC4122/network byte order).</summary>
    private sealed class GuidTypeHandler : SqlMapper.TypeHandler<Guid>
    {
        public override Guid Parse(object value) =>
            GuidSql.FromDb(value);

        public override void SetValue(IDbDataParameter parameter, Guid value)
        {
            parameter.DbType = DbType.Binary;
            parameter.Value  = GuidSql.ToBlob(value);
        }
    }

    /// <summary>Guid? stored as BLOB (nullable).</summary>
    private sealed class NullableGuidTypeHandler : SqlMapper.TypeHandler<Guid?>
    {
        public override Guid? Parse(object value) =>
            GuidSql.FromDbNullable(value);

        public override void SetValue(IDbDataParameter parameter, Guid? value)
        {
            parameter.DbType = DbType.Binary;
            parameter.Value  = GuidSql.ToDb(value);
        }
    }

    /// <summary>DateTimeOffset stored as TEXT (ISO-8601 round-trip format).</summary>
    private sealed class DateTimeOffsetTypeHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value) =>
            DateTimeOffset.Parse((string)value);

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            parameter.DbType = DbType.String;
            parameter.Value  = value.ToString("o");
        }
    }

    /// <summary>DateTimeOffset? stored as TEXT (nullable, ISO-8601 round-trip format).</summary>
    private sealed class NullableDateTimeOffsetTypeHandler : SqlMapper.TypeHandler<DateTimeOffset?>
    {
        public override DateTimeOffset? Parse(object value) =>
            value is DBNull or null ? null : DateTimeOffset.Parse((string)value);

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset? value)
        {
            parameter.DbType = DbType.String;
            parameter.Value  = value.HasValue ? value.Value.ToString("o") : DBNull.Value;
        }
    }
}
