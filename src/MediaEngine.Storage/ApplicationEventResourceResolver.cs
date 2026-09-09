using Dapper;
using MediaEngine.Domain.Authorization;
using MediaEngine.Domain.Events;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class ApplicationEventResourceResolver(IDatabaseConnection database) : IApplicationEventResourceResolver
{
    public Task<ApplicationEventResourceProvenance?> ResolveAsync(Guid assetId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var row = connection.QuerySingleOrDefault<Row>("""
            SELECT a.library_id AS LibraryId, w.media_type AS MediaType
            FROM media_assets a
            JOIN editions e ON e.id=a.edition_id
            JOIN works w ON w.id=e.work_id
            WHERE a.id=@assetId;
            """, new { assetId });
        if (row is null || !Guid.TryParse(row.LibraryId, out var libraryId) || libraryId == Guid.Empty)
        {
            return Task.FromResult<ApplicationEventResourceProvenance?>(null);
        }

        var feature = row.MediaType switch
        {
            "Books" or "Comics" => AccountFeatureId.Read,
            "Movies" or "TV" => AccountFeatureId.Watch,
            "Audiobooks" or "Music" => AccountFeatureId.Listen,
            _ => (AccountFeatureId?)null,
        };
        return Task.FromResult(feature is null ? null : new ApplicationEventResourceProvenance(libraryId, feature.Value));
    }

    private sealed record Row(string? LibraryId, string? MediaType);
}
