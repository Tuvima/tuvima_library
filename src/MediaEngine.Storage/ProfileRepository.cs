using Dapper;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IProfileRepository"/>.
///
/// The seed profile (<see cref="Profile.SeedProfileId"/>) is protected:
/// <see cref="DeleteAsync"/> will return <see langword="false"/> for it.
///
/// Spec: Settings &amp; Management Layer — Identity &amp; Multi-User.
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

        using var conn = _db.CreateConnection();
        var rows = conn.Query<ProfileRow>("""
            SELECT id           AS Id,
                   display_name AS DisplayName,
                   avatar_color AS AvatarColor,
                   avatar_image_path AS AvatarImagePath,
                   role         AS Role,
                   pin_hash     AS PinHash,
                   created_at   AS CreatedAt,
                   navigation_config AS NavigationConfig
            FROM   profiles
            ORDER  BY created_at ASC;
            """).AsList();

        return Task.FromResult<IReadOnlyList<Profile>>(rows.ConvertAll(MapRow));
    }

    /// <inheritdoc/>
    public Task<Profile?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<ProfileRow>("""
            SELECT id           AS Id,
                   display_name AS DisplayName,
                   avatar_color AS AvatarColor,
                   avatar_image_path AS AvatarImagePath,
                   role         AS Role,
                   pin_hash     AS PinHash,
                   created_at   AS CreatedAt,
                   navigation_config AS NavigationConfig
            FROM   profiles
            WHERE  id = @id
            LIMIT  1;
            """, new { id = id.ToString() });

        return Task.FromResult(row is null ? null : MapRow(row));
    }

    /// <inheritdoc/>
    public Task InsertAsync(Profile profile, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(profile);

        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT INTO profiles (id, display_name, avatar_color, avatar_image_path, role, pin_hash, created_at, navigation_config)
            VALUES (@id, @name, @color, @avatarImagePath, @role, @pin, @created, @nav);
            """, new
        {
            id      = profile.Id.ToString(),
            name    = profile.DisplayName,
            color   = profile.AvatarColor,
            avatarImagePath = profile.AvatarImagePath,
            role    = profile.Role.ToString(),
            pin     = profile.PinHash,
            created = profile.CreatedAt.ToString("O"),
            nav     = profile.NavigationConfig,
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> UpdateAsync(Profile profile, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(profile);

        using var conn = _db.CreateConnection();
        var rows = conn.Execute("""
            UPDATE profiles
            SET    display_name      = @name,
                   avatar_color      = @color,
                   avatar_image_path = @avatarImagePath,
                   role              = @role,
                   pin_hash          = @pin,
                   navigation_config = @nav
            WHERE  id = @id;
            """, new
        {
            name  = profile.DisplayName,
            color = profile.AvatarColor,
            avatarImagePath = profile.AvatarImagePath,
            role  = profile.Role.ToString(),
            pin   = profile.PinHash,
            nav   = profile.NavigationConfig,
            id    = profile.Id.ToString(),
        });

        return Task.FromResult(rows > 0);
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // The seed "Owner" profile cannot be deleted.
        if (id == Profile.SeedProfileId)
            return Task.FromResult(false);

        using var conn = _db.CreateConnection();
        var rows = conn.Execute(
            "DELETE FROM profiles WHERE id = @id;",
            new { id = id.ToString() });

        return Task.FromResult(rows > 0);
    }

    // ── Private DTO + mapper ────────────────────────────────────────────────
    // SQLite stores GUIDs as TEXT and DateTimeOffset as ISO-8601 strings.
    // Dapper cannot convert these automatically, so we read into a flat string
    // DTO and convert in code.

    private sealed class ProfileRow
    {
        public string Id               { get; set; } = string.Empty;
        public string DisplayName      { get; set; } = string.Empty;
        public string AvatarColor      { get; set; } = string.Empty;
        public string? AvatarImagePath { get; set; }
        public string Role             { get; set; } = string.Empty;
        public string? PinHash         { get; set; }
        public string CreatedAt        { get; set; } = string.Empty;
        public string? NavigationConfig { get; set; }
    }

    private static Profile MapRow(ProfileRow r) => new()
    {
        Id               = Guid.Parse(r.Id),
        DisplayName      = r.DisplayName,
        AvatarColor      = r.AvatarColor,
        AvatarImagePath  = r.AvatarImagePath,
        Role             = Enum.Parse<ProfileRole>(r.Role),
        PinHash          = r.PinHash,
        CreatedAt        = DateTimeOffset.Parse(r.CreatedAt),
        NavigationConfig = r.NavigationConfig,
    };
}
