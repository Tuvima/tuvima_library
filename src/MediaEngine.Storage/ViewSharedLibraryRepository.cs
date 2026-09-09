using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.PersonalMedia;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class ViewSharedLibraryRepository(IDatabaseConnection database) : IViewSharedLibraryRepository
{
    public Task<ViewSharedLibrary> GetAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var row = connection.QuerySingle<LibraryRow>(new CommandDefinition("""
            SELECT library_id AS LibraryId, created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM view_shared_library WHERE singleton_key = 1;
            """, cancellationToken: ct));
        return Task.FromResult(new ViewSharedLibrary(
            row.LibraryId, ParseDate(row.CreatedAt), ParseDate(row.UpdatedAt)));
    }

    public Task<IReadOnlyList<ViewSharedSource>> GetSourcesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var rows = connection.Query<SourceRow>(new CommandDefinition("""
            SELECT id AS Id, library_id AS LibraryId, source_type AS SourceType,
                   name AS Name, source_key AS SourceKey, last_activity_at AS LastActivityAt,
                   storage_mode AS StorageMode, relative_path AS RelativePath,
                   external_path AS ExternalPath, include_subdirectories AS IncludeSubdirectories,
                   enabled AS Enabled,
                   COALESCE((SELECT include_in_timeline FROM view_source_policies
                              WHERE source_id = view_sources.id), 0) AS IncludeInTimeline,
                   created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM view_sources WHERE scope_kind = 'shared' AND personal_space_id IS NULL
             ORDER BY name COLLATE NOCASE, id;
            """, cancellationToken: ct));
        return Task.FromResult<IReadOnlyList<ViewSharedSource>>(rows.Select(Map).ToList());
    }

    public Task<ViewSharedSource> UpsertSourceAsync(ViewSharedSource source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateId(source.LibraryId, nameof(source));
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Name);
        if (source.StorageMode == ViewSourceStorageMode.Managed
            && (string.IsNullOrWhiteSpace(source.RelativePath) || !string.IsNullOrWhiteSpace(source.ExternalPath)))
        {
            throw new ArgumentException("A managed Shared source requires only a relative path.", nameof(source));
        }

        if (source.StorageMode == ViewSourceStorageMode.Linked
            && (string.IsNullOrWhiteSpace(source.ExternalPath) || !string.IsNullOrWhiteSpace(source.RelativePath)))
        {
            throw new ArgumentException("A linked Shared source requires only an external path.", nameof(source));
        }

        return database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            var sharedLibraryId = connection.QuerySingle<Guid>(new CommandDefinition(
                "SELECT library_id FROM view_shared_library WHERE singleton_key = 1;",
                transaction: transaction, cancellationToken: token));
            if (source.LibraryId != sharedLibraryId)
            {
                throw new InvalidOperationException("A Shared source must use the singleton Shared library identity.");
            }

            var id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id;
            var now = DateTimeOffset.UtcNow;
            var createdAt = source.CreatedAt == default ? now : source.CreatedAt;
            var changed = connection.Execute(new CommandDefinition("""
                INSERT INTO view_sources
                    (id, scope_kind, personal_space_id, library_id, source_type, name, source_key,
                     storage_mode, relative_path, external_path, include_subdirectories, enabled,
                     last_activity_at, created_at, updated_at)
                VALUES (@id, 'shared', NULL, @LibraryId, @SourceType, @Name, @SourceKey,
                        @StorageMode, @RelativePath, @ExternalPath, @IncludeSubdirectories, @Enabled,
                        @LastActivityAt, @createdAt, @now)
                ON CONFLICT(id) DO UPDATE SET
                    source_type = excluded.source_type, name = excluded.name,
                    source_key = excluded.source_key, storage_mode = excluded.storage_mode,
                    relative_path = excluded.relative_path, external_path = excluded.external_path,
                    include_subdirectories = excluded.include_subdirectories, enabled = excluded.enabled,
                    last_activity_at = excluded.last_activity_at, updated_at = excluded.updated_at
                WHERE view_sources.scope_kind = 'shared'
                  AND view_sources.personal_space_id IS NULL
                  AND view_sources.library_id = excluded.library_id;
                """, new
            {
                id,
                source.LibraryId,
                SourceType = ToStorage(source.SourceType),
                Name = source.Name.Trim(),
                SourceKey = NullIfWhiteSpace(source.SourceKey),
                StorageMode = ToStorage(source.StorageMode),
                RelativePath = NullIfWhiteSpace(source.RelativePath),
                ExternalPath = NullIfWhiteSpace(source.ExternalPath),
                source.IncludeSubdirectories,
                source.Enabled,
                source.LastActivityAt,
                createdAt,
                now,
            }, transaction, cancellationToken: token));
            if (changed == 0)
            {
                throw new InvalidOperationException("A source identity cannot move into or out of the Shared library.");
            }

            connection.Execute(new CommandDefinition("""
                INSERT INTO view_source_policies (source_id, include_in_timeline, updated_at)
                VALUES (@id, @IncludeInTimeline, @now)
                ON CONFLICT(source_id) DO UPDATE SET
                    include_in_timeline = excluded.include_in_timeline,
                    updated_at = excluded.updated_at;
                """, new { id, source.IncludeInTimeline, now }, transaction, cancellationToken: token));
            return source with { Id = id, Name = source.Name.Trim(), CreatedAt = createdAt, UpdatedAt = now };
        }, ct);
    }

    public Task<bool> DeleteSourceAsync(Guid sourceId, CancellationToken ct = default)
    {
        ValidateId(sourceId, nameof(sourceId));
        return database.ExecuteWriteAsync((connection, transaction, token) =>
            connection.Execute(new CommandDefinition("""
                DELETE FROM view_sources
                 WHERE id = @sourceId AND scope_kind = 'shared' AND personal_space_id IS NULL
                   AND NOT EXISTS (SELECT 1 FROM local_file_sources WHERE source_id = @sourceId);
                """, new { sourceId }, transaction, cancellationToken: token)) > 0, ct);
    }

    private static ViewSharedSource Map(SourceRow row) => new(
        row.Id, row.LibraryId, ParseSourceType(row.SourceType), row.Name, row.SourceKey,
        ParseNullableDate(row.LastActivityAt), ParseDate(row.CreatedAt), ParseDate(row.UpdatedAt),
        ParseStorageMode(row.StorageMode), row.RelativePath, row.ExternalPath,
        row.IncludeSubdirectories, row.Enabled, row.IncludeInTimeline);

    private static string ToStorage(ViewSourceType value) => value switch
    {
        ViewSourceType.Folder => "folder",
        ViewSourceType.BrowserUpload => "browser_upload",
        ViewSourceType.DeviceImport => "device_import",
        ViewSourceType.MobileBackup => "mobile_backup",
        ViewSourceType.Network => "network",
        ViewSourceType.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static ViewSourceType ParseSourceType(string value) => value switch
    {
        "folder" => ViewSourceType.Folder,
        "browser_upload" => ViewSourceType.BrowserUpload,
        "device_import" => ViewSourceType.DeviceImport,
        "mobile_backup" => ViewSourceType.MobileBackup,
        "network" => ViewSourceType.Network,
        "other" => ViewSourceType.Other,
        _ => throw new InvalidOperationException($"Unsupported stored source type '{value}'."),
    };

    private static string ToStorage(ViewSourceStorageMode value) => value switch
    {
        ViewSourceStorageMode.Managed => "managed",
        ViewSourceStorageMode.Linked => "linked",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static ViewSourceStorageMode ParseStorageMode(string value) => value switch
    {
        "managed" => ViewSourceStorageMode.Managed,
        "linked" => ViewSourceStorageMode.Linked,
        _ => throw new InvalidOperationException($"Unsupported stored Shared source storage mode '{value}'."),
    };

    private static DateTimeOffset ParseDate(string value) => DateTimeOffset.Parse(value);
    private static DateTimeOffset? ParseNullableDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("ID is required.", parameterName);
        }
    }

    private sealed class LibraryRow
    {
        public Guid LibraryId { get; init; }
        public string CreatedAt { get; init; } = string.Empty;
        public string UpdatedAt { get; init; } = string.Empty;
    }

    private sealed class SourceRow
    {
        public Guid Id { get; init; }
        public Guid LibraryId { get; init; }
        public string SourceType { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? SourceKey { get; init; }
        public string? LastActivityAt { get; init; }
        public string StorageMode { get; init; } = "managed";
        public string? RelativePath { get; init; }
        public string? ExternalPath { get; init; }
        public bool IncludeSubdirectories { get; init; }
        public bool Enabled { get; init; }
        public bool IncludeInTimeline { get; init; }
        public string CreatedAt { get; init; } = string.Empty;
        public string UpdatedAt { get; init; } = string.Empty;
    }
}
