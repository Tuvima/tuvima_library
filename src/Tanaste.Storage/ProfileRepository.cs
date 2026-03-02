using Microsoft.Data.Sqlite;
using Tanaste.Domain.Aggregates;
using Tanaste.Domain.Contracts;
using Tanaste.Domain.Enums;
using Tanaste.Storage.Contracts;

namespace Tanaste.Storage;

/// <summary>
/// ORM-less SQLite implementation of <see cref="IProfileRepository"/>.
///
/// The seed profile (<see cref="Profile.SeedProfileId"/>) is protected:
/// <see cref="DeleteAsync"/> will return <see langword="false"/> for it.
///
/// Spec: Settings & Management Layer — Identity & Multi-User.
/// </summary>
public sealed class ProfileRepository : IProfileRepository
{
    private readonly IDatabaseConnection _db;

    public ProfileRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, display_name, avatar_color, role, pin_hash, created_at, navigation_config
            FROM   profiles
            ORDER  BY created_at ASC;
            """;

        var results = new List<Profile>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(MapRow(reader));
        }

        return Task.FromResult<IReadOnlyList<Profile>>(results);
    }

    /// <inheritdoc/>
    public Task<Profile?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, display_name, avatar_color, role, pin_hash, created_at, navigation_config
            FROM   profiles
            WHERE  id = @id
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = cmd.ExecuteReader();
        var result = reader.Read() ? MapRow(reader) : null;
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task InsertAsync(Profile profile, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(profile);

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO profiles (id, display_name, avatar_color, role, pin_hash, created_at, navigation_config)
            VALUES (@id, @name, @color, @role, @pin, @created, @nav);
            """;
        cmd.Parameters.AddWithValue("@id",      profile.Id.ToString());
        cmd.Parameters.AddWithValue("@name",    profile.DisplayName);
        cmd.Parameters.AddWithValue("@color",   profile.AvatarColor);
        cmd.Parameters.AddWithValue("@role",    profile.Role.ToString());
        cmd.Parameters.AddWithValue("@pin",     profile.PinHash ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@created", profile.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@nav",     profile.NavigationConfig ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> UpdateAsync(Profile profile, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(profile);

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE profiles
            SET    display_name      = @name,
                   avatar_color      = @color,
                   role              = @role,
                   pin_hash          = @pin,
                   navigation_config = @nav
            WHERE  id = @id;
            """;
        cmd.Parameters.AddWithValue("@name",  profile.DisplayName);
        cmd.Parameters.AddWithValue("@color", profile.AvatarColor);
        cmd.Parameters.AddWithValue("@role",  profile.Role.ToString());
        cmd.Parameters.AddWithValue("@pin",   profile.PinHash ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@nav",   profile.NavigationConfig ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@id",    profile.Id.ToString());
        cmd.ExecuteNonQuery();

        using var changesCmd = conn.CreateCommand();
        changesCmd.CommandText = "SELECT changes();";
        var rows = Convert.ToInt64(changesCmd.ExecuteScalar()!);

        return Task.FromResult(rows > 0);
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // The seed "Owner" profile cannot be deleted.
        if (id == Profile.SeedProfileId)
            return Task.FromResult(false);

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM profiles WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        cmd.ExecuteNonQuery();

        using var changesCmd = conn.CreateCommand();
        changesCmd.CommandText = "SELECT changes();";
        var rows = Convert.ToInt64(changesCmd.ExecuteScalar()!);

        return Task.FromResult(rows > 0);
    }

    // ── Row mapper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Maps the current reader row to a <see cref="Profile"/>.
    /// Column ordinals: 0=id, 1=display_name, 2=avatar_color, 3=role, 4=pin_hash, 5=created_at, 6=navigation_config.
    /// </summary>
    private static Profile MapRow(SqliteDataReader r) => new()
    {
        Id               = Guid.Parse(r.GetString(0)),
        DisplayName      = r.GetString(1),
        AvatarColor      = r.GetString(2),
        Role             = Enum.Parse<ProfileRole>(r.GetString(3)),
        PinHash          = r.IsDBNull(4) ? null : r.GetString(4),
        CreatedAt        = DateTimeOffset.Parse(r.GetString(5)),
        NavigationConfig = r.IsDBNull(6) ? null : r.GetString(6),
    };
}
