using System.Data;
using Dapper;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Enums;

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
        SqlMapper.AddTypeHandler(new EnumTextTypeHandler<WikidataLinkStatus>(
            AggregateStateSerializer.ParseWikidataLinkStatus,
            AggregateStateSerializer.ToStorageValue));
        SqlMapper.AddTypeHandler(new EnumTextTypeHandler<WorkMatchLevel>(
            AggregateStateSerializer.ParseWorkMatchLevel,
            AggregateStateSerializer.ToStorageValue));
        SqlMapper.AddTypeHandler(new EnumTextTypeHandler<CollectionType>(
            AggregateStateSerializer.ParseCollectionType,
            AggregateStateSerializer.ToStorageValue));
        SqlMapper.AddTypeHandler(new EnumTextTypeHandler<CollectionScope>(
            AggregateStateSerializer.ParseCollectionScope,
            AggregateStateSerializer.ToStorageValue));
        SqlMapper.AddTypeHandler(new EnumTextTypeHandler<CollectionResolution>(
            AggregateStateSerializer.ParseCollectionResolution,
            AggregateStateSerializer.ToStorageValue));
        SqlMapper.AddTypeHandler(new EnumTextTypeHandler<CollectionMatchMode>(
            AggregateStateSerializer.ParseCollectionMatchMode,
            AggregateStateSerializer.ToStorageValue));
        SqlMapper.AddTypeHandler(new EnumTextTypeHandler<CollectionSortDirection>(
            AggregateStateSerializer.ParseCollectionSortDirection,
            AggregateStateSerializer.ToStorageValue));
        SqlMapper.AddTypeHandler(new EnumTextTypeHandler<CollectionUniverseStatus>(
            AggregateStateSerializer.ParseCollectionUniverseStatus,
            AggregateStateSerializer.ToStorageValue));

        // Force Guid values through the custom BLOB handler.
        SqlMapper.RemoveTypeMap(typeof(Guid));
        SqlMapper.RemoveTypeMap(typeof(Guid?));
        SqlMapper.RemoveTypeMap(typeof(WikidataLinkStatus));
        SqlMapper.RemoveTypeMap(typeof(WorkMatchLevel));
        SqlMapper.RemoveTypeMap(typeof(CollectionType));
        SqlMapper.RemoveTypeMap(typeof(CollectionScope));
        SqlMapper.RemoveTypeMap(typeof(CollectionResolution));
        SqlMapper.RemoveTypeMap(typeof(CollectionMatchMode));
        SqlMapper.RemoveTypeMap(typeof(CollectionSortDirection));
        SqlMapper.RemoveTypeMap(typeof(CollectionUniverseStatus));

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

    private sealed class EnumTextTypeHandler<TEnum>(
        Func<string, TEnum> parse,
        Func<TEnum, string> serialize) : SqlMapper.TypeHandler<TEnum>
        where TEnum : struct, Enum
    {
        public override TEnum Parse(object value) =>
            parse(value as string
                ?? throw new InvalidOperationException(
                    $"Expected TEXT storage for {typeof(TEnum).Name}, received {value.GetType().Name}."));

        public override void SetValue(IDbDataParameter parameter, TEnum value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = serialize(value);
        }
    }
}
