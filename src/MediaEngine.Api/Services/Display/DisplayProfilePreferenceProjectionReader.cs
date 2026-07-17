using Dapper;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Display;

public sealed class DisplayProfilePreferenceProjectionReader
{
    private readonly IDatabaseConnection _db;

    public DisplayProfilePreferenceProjectionReader(IDatabaseConnection db)
    {
        _db = db;
    }

    public async Task<IReadOnlySet<Guid>> LoadHiddenWorkIdsAsync(
        Guid? profileId,
        CancellationToken ct)
    {
        if (!profileId.HasValue)
            return new HashSet<Guid>();

        using var connection = _db.CreateConnection();
        var ids = await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT work_id
            FROM profile_work_preferences
            WHERE profile_id = @profileId AND is_hidden = 1;
            """,
            new { profileId = profileId.Value },
            cancellationToken: ct));
        return ids.ToHashSet();
    }
}
