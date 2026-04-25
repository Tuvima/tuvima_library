using Dapper;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Display;

public sealed class DisplayFavoriteProjectionReader
{
    private readonly IDatabaseConnection _db;

    public DisplayFavoriteProjectionReader(IDatabaseConnection db)
    {
        _db = db;
    }

    public async Task<IReadOnlySet<Guid>> LoadAsync(Guid? profileId, CancellationToken ct)
    {
        if (!profileId.HasValue)
        {
            return new HashSet<Guid>();
        }

        using var conn = _db.CreateConnection();
        var ids = await conn.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT ci.work_id
            FROM collection_items ci
            INNER JOIN collections c ON c.id = ci.collection_id
            WHERE c.scope = 'user'
              AND c.profile_id = @ProfileId
              AND c.collection_type = 'Playlist'
              AND c.resolution = 'materialized'
              AND c.display_name = 'Favorites'
              AND c.is_enabled = 1;
            """,
            new { ProfileId = profileId.Value.ToString() },
            cancellationToken: ct));

        return ids.ToHashSet();
    }
}
