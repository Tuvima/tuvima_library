using System.Data;
using Dapper;

namespace MediaEngine.Storage;

/// <summary>
/// Registers custom Dapper type handlers for types that SQLite stores as TEXT
/// but .NET represents as structs (Guid, DateTimeOffset).  Call
/// <see cref="Configure"/> once at startup before any Dapper queries execute.
/// </summary>
public static class DapperConfiguration
{
    private static bool _configured;

    /// <summary>Register all custom type handlers.  Safe to call multiple times.</summary>
    public static void Configure()
    {
        if (_configured) return;

        SqlMapper.AddTypeHandler(new GuidTypeHandler());
        SqlMapper.AddTypeHandler(new DateTimeOffsetTypeHandler());
        SqlMapper.AddTypeHandler(new NullableGuidTypeHandler());
        SqlMapper.AddTypeHandler(new NullableDateTimeOffsetTypeHandler());

        // Tell Dapper that Guid columns come back as strings from SQLite.
        SqlMapper.RemoveTypeMap(typeof(Guid));
        SqlMapper.RemoveTypeMap(typeof(Guid?));

        _configured = true;
    }

    /// <summary>Guid ↔ TEXT (lowercase string representation).</summary>
    private sealed class GuidTypeHandler : SqlMapper.TypeHandler<Guid>
    {
        public override Guid Parse(object value) =>
            Guid.Parse((string)value);

        public override void SetValue(IDbDataParameter parameter, Guid value)
        {
            parameter.DbType = DbType.String;
            parameter.Value  = value.ToString();
        }
    }

    /// <summary>Guid? ↔ TEXT (nullable).</summary>
    private sealed class NullableGuidTypeHandler : SqlMapper.TypeHandler<Guid?>
    {
        public override Guid? Parse(object value) =>
            value is DBNull or null ? null : Guid.Parse((string)value);

        public override void SetValue(IDbDataParameter parameter, Guid? value)
        {
            parameter.DbType = DbType.String;
            parameter.Value  = value.HasValue ? value.Value.ToString() : DBNull.Value;
        }
    }

    /// <summary>DateTimeOffset ↔ TEXT (ISO-8601 round-trip format).</summary>
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

    /// <summary>DateTimeOffset? ↔ TEXT (nullable, ISO-8601 round-trip format).</summary>
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
