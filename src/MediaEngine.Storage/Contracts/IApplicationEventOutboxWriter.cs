using MediaEngine.Domain.Events;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage.Contracts;

public interface IApplicationEventOutboxWriter
{
    StoredApplicationEvent Append(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ApplicationEventDraft value,
        CancellationToken ct = default);
}
