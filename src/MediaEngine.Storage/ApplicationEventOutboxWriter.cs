using System.Text;
using Dapper;
using MediaEngine.Domain.Authorization;
using MediaEngine.Domain.Events;
using MediaEngine.Storage.Contracts;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage;

public sealed class ApplicationEventOutboxWriter : IApplicationEventOutboxWriter
{
    private const int MaximumPayloadBytes = 65_536;

    public StoredApplicationEvent Append(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ApplicationEventDraft value,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(value);
        if (value.Version != 1 || string.IsNullOrWhiteSpace(value.EventType) ||
            string.IsNullOrWhiteSpace(value.Subject.Type) || string.IsNullOrWhiteSpace(value.Subject.Id))
        {
            throw new ArgumentException("Application event identity and version are required.", nameof(value));
        }

        if (value.Subject.FeatureId is { } feature &&
            feature != AccountFeatureId.Read && feature != AccountFeatureId.Watch && feature != AccountFeatureId.Listen)
        {
            throw new ArgumentException("Application event feature provenance must be read, watch, or listen.", nameof(value));
        }

        var payloadJson = value.Payload.GetRawText();
        if (Encoding.UTF8.GetByteCount(payloadJson) > MaximumPayloadBytes)
        {
            throw new ArgumentException("Application event payload exceeds 65536 UTF-8 bytes.", nameof(value));
        }

        connection.Execute("""
            INSERT OR IGNORE INTO storage_metadata(key,value)
            VALUES ('application_event_server_id', @serverId);
            """, new { serverId = Guid.NewGuid().ToString("D") }, transaction);
        var serverId = connection.QuerySingle<string>(
            "SELECT value FROM storage_metadata WHERE key='application_event_server_id';", transaction: transaction);
        return ApplicationEventRepository.Insert(connection, transaction, new(
            0, Guid.NewGuid(), value.EventType, value.Version, value.OccurredAt, serverId, value.Subject, payloadJson));
    }
}
